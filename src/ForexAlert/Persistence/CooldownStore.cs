using System.Text.Json;
using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.Persistence;

public sealed class CooldownStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, AlertDeliveryState>? _state;

    public CooldownStore(IOptions<ForexAlertOptions> options) : this(options.Value.CooldownStatePath, AppContext.BaseDirectory) { }

    public CooldownStore(string configuredPath, string basePath)
    {
        _path = Path.IsPathFullyQualified(configuredPath) ? Path.GetFullPath(configuredPath) : Path.GetFullPath(configuredPath, basePath);
    }

    public async Task<bool> CanSendAsync(AlertCandidate candidate, DateTimeOffset nowUtc, TimeSpan cooldown, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AlertDeliveryState> state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!state.TryGetValue(Key(candidate), out AlertDeliveryState? sent)) return true;
            return sent.CandidateFingerprint != candidate.DeliveryFingerprint && nowUtc.ToUniversalTime() >= sent.LastSentUtc.Add(cooldown);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkSentAsync(AlertCandidate candidate, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, AlertDeliveryState> updated = new(await LoadAsync(cancellationToken).ConfigureAwait(false), StringComparer.Ordinal)
            {
                [Key(candidate)] = new(nowUtc.ToUniversalTime(), candidate.DeliveryFingerprint),
            };
            await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            _state = updated;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, AlertDeliveryState>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;
        if (!File.Exists(_path)) return _state = new(StringComparer.Ordinal);
        try
        {
            string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            return _state = new(JsonSerializer.Deserialize<Dictionary<string, AlertDeliveryState>>(json, JsonOptions) ?? [], StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Cooldown file '{_path}' is malformed. Repair or remove it before restarting ForexAlert.", exception);
        }
    }

    private async Task SaveAsync(Dictionary<string, AlertDeliveryState> state, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4_096, true))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static string Key(AlertCandidate candidate) => $"{candidate.RuleName}|{candidate.CanonicalSymbol}";
}
