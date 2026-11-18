namespace Zharfa.Api.DataQuality;

using System;
using Microsoft.Extensions.Configuration;
using Zharfa.Api.Analytics;
using Zharfa.Api.Platform;

/// <summary>
/// Data-quality settings (data-model §3.4). <see cref="EarliestKey"/> is the earliest plausible document date as a
/// Jalali day key (yyyy*10000 + mm*100 + dd); documents before it are flagged as invalid dates. Read once at startup;
/// an invalid value refuses startup and names the key.
/// </summary>
public sealed record DataQualityOptions(int EarliestKey, string EarliestText)
{
    public const string ConfigurationKey = "DataQuality__EarliestJalaliDate";
    public const string DefaultEarliest = "1390-01-01";

    public static DataQualityOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = ConfigurationKeys.Get(configuration, ConfigurationKey) ?? DefaultEarliest;
        if (!JalaliInput.TryParseDate(raw, out var date))
        {
            throw new InvalidOperationException(
                $"مقدار کلید پیکربندی {ConfigurationKey} معتبر نیست؛ قالب yyyy-mm-dd لازم است (مثلاً {DefaultEarliest}). " +
                $"Configuration key {ConfigurationKey} is invalid; use yyyy-mm-dd.");
        }

        var key = date.Year * 10000 + date.Month * 100 + date.Day;
        var text = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
        return new DataQualityOptions(key, text);
    }
}
