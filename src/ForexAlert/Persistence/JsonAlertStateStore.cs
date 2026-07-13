using System.Text.Json;
using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.Persistence;

public sealed class JsonAlertStateStore : IAlertStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, AlertDeliveryState>? _state;

    public JsonAlertStateStore(IOptions<PersistenceOptions> options) :
        this(options, AppContext.BaseDirectory)
    {
    }

    public JsonAlertStateStore(IOptions<PersistenceOptions> options, string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        string configuredPath = options.Value.AlertStatePath;
        _path = Path.IsPathFullyQualified(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, basePath);
    }

    public async ValueTask<AlertDeliveryState?> GetAsync(
        AlertKey key,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AlertDeliveryState> state = await GetOrLoadAsync(cancellationToken).ConfigureAwait(false);
            return state.TryGetValue(ToStorageKey(key), out AlertDeliveryState? value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SetAsync(
        AlertKey key,
        AlertDeliveryState deliveryState,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AlertDeliveryState> state = await GetOrLoadAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<string, AlertDeliveryState> updated = new(state, StringComparer.Ordinal)
            {
                [ToStorageKey(key)] = deliveryState with
                {
                    LastSentUtc = deliveryState.LastSentUtc.ToUniversalTime(),
                },
            };
            await SaveAtomicallyAsync(updated, cancellationToken).ConfigureAwait(false);
            _state = updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, AlertDeliveryState>> GetOrLoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return _state;
        }

        if (!File.Exists(_path))
        {
            _state = new Dictionary<string, AlertDeliveryState>(StringComparer.Ordinal);
            return _state;
        }

        string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AlertDeliveryState>? current =
                JsonSerializer.Deserialize<Dictionary<string, AlertDeliveryState>>(json, SerializerOptions);
            _state = new Dictionary<string, AlertDeliveryState>(
                current ?? new Dictionary<string, AlertDeliveryState>(),
                StringComparer.Ordinal);
            return _state;
        }
        catch (JsonException currentSchemaException)
        {
            try
            {
                Dictionary<string, DateTimeOffset>? legacy =
                    JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json, SerializerOptions);
                _state = (legacy ?? new Dictionary<string, DateTimeOffset>())
                    .ToDictionary(
                        static pair => pair.Key,
                        static pair => new AlertDeliveryState(pair.Value.ToUniversalTime(), string.Empty),
                        StringComparer.Ordinal);
                return _state;
            }
            catch (JsonException)
            {
                throw new InvalidDataException(
                    $"Alert state file '{_path}' is malformed. Repair or remove it before restarting ForexAlert.",
                    currentSchemaException);
            }
        }
    }

    private async Task SaveAtomicallyAsync(
        Dictionary<string, AlertDeliveryState> state,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4_096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ToStorageKey(AlertKey key) => $"{key.RuleName}|{key.CanonicalSymbol}";
}
