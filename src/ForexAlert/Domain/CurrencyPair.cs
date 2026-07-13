using System.Diagnostics.CodeAnalysis;

namespace ForexAlert.Domain;

public readonly struct CurrencyPair : IEquatable<CurrencyPair>
{
    private CurrencyPair(string baseCurrency, string quoteCurrency)
    {
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
    }

    public string BaseCurrency { get; }

    public string QuoteCurrency { get; }

    public string CanonicalSymbol => $"{BaseCurrency}/{QuoteCurrency}";

    public CurrencyPair Inverse() => new(QuoteCurrency, BaseCurrency);

    public static CurrencyPair Parse(string value)
    {
        if (!TryParse(value, out CurrencyPair pair))
        {
            throw new FormatException($"'{value}' is not a valid currency pair. Expected two different three-letter codes, for example EUR/USD.");
        }

        return pair;
    }

    public static bool TryParse(
        [NotNullWhen(true)] string? value,
        out CurrencyPair pair)
    {
        pair = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToUpperInvariant();
        string[] parts = normalized.Split('/');
        if (parts.Length != 2 || !IsCurrencyCode(parts[0]) || !IsCurrencyCode(parts[1]) || parts[0] == parts[1])
        {
            return false;
        }

        pair = new CurrencyPair(parts[0], parts[1]);
        return true;
    }

    public bool Equals(CurrencyPair other) =>
        string.Equals(BaseCurrency, other.BaseCurrency, StringComparison.Ordinal) &&
        string.Equals(QuoteCurrency, other.QuoteCurrency, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CurrencyPair other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BaseCurrency, QuoteCurrency);

    public override string ToString() => CanonicalSymbol;

    public static bool operator ==(CurrencyPair left, CurrencyPair right) => left.Equals(right);

    public static bool operator !=(CurrencyPair left, CurrencyPair right) => !left.Equals(right);

    private static bool IsCurrencyCode(string value) =>
        value.Length == 3 && value.All(static character => character is >= 'A' and <= 'Z');
}
