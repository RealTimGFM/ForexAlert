using ForexAlert.Domain;
using Microsoft.Extensions.Options;

namespace ForexAlert.Configuration;

public sealed class ConfiguredCurrencyPairs
{
    public ConfiguredCurrencyPairs(IOptions<CurrencyPairOptions> options)
    {
        Values = options.Value.Pairs.Select(CurrencyPair.Parse).ToArray();
    }

    public IReadOnlyList<CurrencyPair> Values { get; }
}
