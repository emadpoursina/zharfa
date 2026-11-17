namespace Zharfa.Api.Platform;

using System;

/// <summary>
/// Install data mode (FR-020): exactly one of "production" | "demo", set by
/// server configuration only (<c>Zharfa__DataMode</c>) and never changeable
/// from the UI or API. Registered as a singleton and read by the health
/// endpoint, the startup gate, <c>[DemoOnly]</c> and every data label.
/// </summary>
public sealed class DataModeOptions
{
    public const string ConfigurationKey = "Zharfa__DataMode";
    public const string Production = "production";
    public const string Demo = "demo";

    public DataModeOptions(string mode)
    {
        Mode = mode.Trim().ToLowerInvariant();
    }

    /// <summary>Normalized mode: <c>production</c> or <c>demo</c>.</summary>
    public string Mode { get; }

    public bool IsProduction => Mode == Production;

    public bool IsDemo => Mode == Demo;

    /// <summary>Value for <c>dataLabel</c> in API payloads (FR-024 / FR-022).</summary>
    public string DataLabel => IsProduction ? "real" : "demo";

    /// <summary>
    /// Pure validation of the raw configuration value. Returns
    /// <c>null</c> when valid; otherwise a Persian + English error naming
    /// the configuration key (used verbatim to refuse startup).
    /// </summary>
    public static string? Validate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"کلید پیکربندی {ConfigurationKey} تنظیم نشده است؛ باید \"production\" یا \"demo\" باشد. " +
                   $"Configuration key {ConfigurationKey} is required and must be \"production\" or \"demo\".";
        }

        var normalized = raw.Trim();
        if (!string.Equals(normalized, Production, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, Demo, StringComparison.OrdinalIgnoreCase))
        {
            return $"مقدار کلید پیکربندی {ConfigurationKey} نامعتبر است (\"{normalized}\")؛ فقط \"production\" یا \"demo\" مجاز است. " +
                   $"Configuration key {ConfigurationKey} has an invalid value; only \"production\" or \"demo\" is allowed.";
        }

        return null;
    }

    /// <summary>
    /// Raw configured value of <see cref="ConfigurationKey"/>, read through
    /// <see cref="ConfigurationKeys"/> so the environment form
    /// (<c>Zharfa:DataMode</c> in the configuration tree) is found.
    /// </summary>
    public static string? ReadConfiguredMode(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
        ConfigurationKeys.Get(configuration, ConfigurationKey);

    /// <summary>Validates and constructs the options; throws when invalid.</summary>
    public static DataModeOptions FromConfiguration(string? raw)
    {
        var error = Validate(raw);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        return new DataModeOptions(raw!);
    }
}
