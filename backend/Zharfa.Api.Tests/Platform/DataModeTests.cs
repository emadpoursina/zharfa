namespace Zharfa.Api.Tests.Platform;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Zharfa.Api.Platform;

public class DataModeTests
{
    // ---- T006: mode parsing ----

    [Fact]
    public void Valid_production_mode_is_accepted_case_insensitively()
    {
        Assert.Null(DataModeOptions.Validate("production"));
        Assert.Null(DataModeOptions.Validate("  PRODUCTION  "));
    }

    [Fact]
    public void Valid_demo_mode_is_accepted_case_insensitively()
    {
        Assert.Null(DataModeOptions.Validate("demo"));
        Assert.Null(DataModeOptions.Validate("Demo"));
    }

    [Fact]
    public void Missing_mode_returns_error_naming_the_key()
    {
        var error = DataModeOptions.Validate(null);
        Assert.NotNull(error);
        Assert.Contains(DataModeOptions.ConfigurationKey, error);

        Assert.NotNull(DataModeOptions.Validate(""));
        Assert.NotNull(DataModeOptions.Validate("   "));
    }

    [Fact]
    public void Invalid_mode_returns_error_naming_the_key_and_value()
    {
        var error = DataModeOptions.Validate("staging");
        Assert.NotNull(error);
        Assert.Contains(DataModeOptions.ConfigurationKey, error);
        Assert.Contains("staging", error);
    }

    [Fact]
    public void Options_expose_flags_and_data_label()
    {
        var production = DataModeOptions.FromConfiguration("production");
        Assert.True(production.IsProduction);
        Assert.False(production.IsDemo);
        Assert.Equal("real", production.DataLabel);

        var demo = DataModeOptions.FromConfiguration("demo");
        Assert.True(demo.IsDemo);
        Assert.False(demo.IsProduction);
        Assert.Equal("demo", demo.DataLabel);
    }

    [Fact]
    public void FromConfiguration_throws_on_missing_or_invalid_mode()
    {
        Assert.Throws<InvalidOperationException>(() => DataModeOptions.FromConfiguration(null));
        Assert.Throws<InvalidOperationException>(() => DataModeOptions.FromConfiguration("other"));
    }

    // ---- T007: startup gate ----

    [Fact]
    public void First_start_with_valid_mode_is_allowed()
    {
        var decision = DataModeGate.Decide("demo", stored: null, appliedSeedNames: Array.Empty<string>());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Flip_of_stored_mode_is_refused()
    {
        var decision = DataModeGate.Decide("production", stored: "demo", appliedSeedNames: Array.Empty<string>());
        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Error);
        Assert.Contains("demo", decision.Error);
        Assert.Contains("production", decision.Error);

        var back = DataModeGate.Decide("demo", stored: "production", appliedSeedNames: Array.Empty<string>());
        Assert.False(back.Allowed);
    }

    [Fact]
    public void Matching_stored_mode_is_allowed()
    {
        Assert.True(DataModeGate.Decide("production", "production", Array.Empty<string>()).Allowed);
        Assert.True(DataModeGate.Decide("demo", "demo", Array.Empty<string>()).Allowed);
    }

    [Fact]
    public void Production_with_seed_names_recorded_is_refused()
    {
        var decision = DataModeGate.Decide(
            "production",
            stored: null,
            appliedSeedNames: new[] { "0001_core.sql", "0002_sales_cash_users.sql" });
        Assert.False(decision.Allowed);
        Assert.Contains("0001_core.sql", decision.Error);
    }

    [Fact]
    public void Demo_with_seed_names_recorded_is_allowed()
    {
        var decision = DataModeGate.Decide(
            "demo",
            stored: "demo",
            appliedSeedNames: new[] { "0001_core.sql" });
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Invalid_configured_mode_is_refused_by_gate()
    {
        var decision = DataModeGate.Decide("banana", stored: null, appliedSeedNames: Array.Empty<string>());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void First_start_records_mode_via_store_decision()
    {
        // First start: stored is null and decision is allowed — the runner
        // stores the mode (covered by RunAsync; here we assert the trigger).
        var decision = DataModeGate.Decide("production", null, Array.Empty<string>());
        Assert.True(decision.Allowed);
        Assert.Null(decision.Error);
    }

    // ---- T080: configuration-level reads (FR-020) ----

    /// <summary>
    /// The .NET environment provider stores <c>Zharfa__DataMode</c> as <c>Zharfa:DataMode</c>;
    /// an in-memory source with the colon key is the same shape the provider produces.
    /// </summary>
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var map = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            map[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(map).Build();
    }

    [Fact]
    public void Mode_set_as_environment_form_is_read_from_colon_key()
    {
        var configuration = Config(("Zharfa:DataMode", "demo"));

        Assert.Equal("demo", DataModeOptions.ReadConfiguredMode(configuration));
        Assert.True(DataModeOptions.FromConfiguration(DataModeOptions.ReadConfiguredMode(configuration)).IsDemo);
    }

    [Fact]
    public void Mode_set_with_literal_double_underscore_key_is_also_read()
    {
        var configuration = Config(("Zharfa__DataMode", "production"));

        Assert.True(DataModeOptions.FromConfiguration(DataModeOptions.ReadConfiguredMode(configuration)).IsProduction);
    }

    [Fact]
    public void Missing_or_blank_mode_in_configuration_refuses_and_names_the_key()
    {
        Assert.Null(DataModeOptions.ReadConfiguredMode(Config()));
        Assert.Null(DataModeOptions.ReadConfiguredMode(Config(("Zharfa:DataMode", "   "))));

        var error = Assert.Throws<InvalidOperationException>(() =>
            DataModeOptions.FromConfiguration(DataModeOptions.ReadConfiguredMode(Config())));
        Assert.Contains(DataModeOptions.ConfigurationKey, error.Message);
    }

    [Fact]
    public void Invalid_mode_in_configuration_is_refused()
    {
        var configuration = Config(("Zharfa:DataMode", "staging"));

        Assert.Throws<InvalidOperationException>(() =>
            DataModeOptions.FromConfiguration(DataModeOptions.ReadConfiguredMode(configuration)));
    }

    [Fact]
    public void Config_key_helper_prefers_colon_form_and_ignores_blank_values()
    {
        var configuration = Config(("Zharfa:DataMode", "  "), ("Zharfa__DataMode", "demo"));
        Assert.Equal("demo", ConfigurationKeys.Get(configuration, "Zharfa__DataMode"));

        Assert.Null(ConfigurationKeys.Get(Config(("Zharfa:DataMode", "")), "Zharfa__DataMode"));
    }

    [Fact]
    public void Config_key_helper_keeps_passwords_untrimmed()
    {
        var configuration = Config(("Bootstrap:AdminPassword", "  spaced password  "));

        Assert.Equal("  spaced password  ", ConfigurationKeys.Get(configuration, "Bootstrap__AdminPassword"));
    }

    // ---- T081: bootstrap accounts through the same helper ----

    private static (string Key, string? Value)[] ValidBootstrap(string prefix = "Bootstrap:") =>
    [
        (prefix + "AdminUsername", "admin.user"),
        (prefix + "AdminPassword", "admin-password-123"),
        (prefix + "ManagerUsername", "manager.user"),
        (prefix + "ManagerPassword", "manager-password-123"),
    ];

    [Fact]
    public void Bootstrap_values_in_environment_form_are_accepted()
    {
        Assert.Null(BootstrapAccounts.Validate(Config(ValidBootstrap())));
    }

    [Fact]
    public void Bootstrap_values_with_literal_double_underscore_keys_are_accepted()
    {
        Assert.Null(BootstrapAccounts.Validate(Config(ValidBootstrap("Bootstrap__"))));
    }

    [Fact]
    public void Bootstrap_missing_manager_password_is_refused()
    {
        var values = ValidBootstrap().Where(v => v.Key != "Bootstrap:ManagerPassword").ToArray();

        Assert.NotNull(BootstrapAccounts.Validate(Config(values)));
    }

    [Fact]
    public void Bootstrap_short_password_is_refused_in_environment_form()
    {
        var values = ValidBootstrap().Where(v => v.Key != "Bootstrap:AdminPassword")
            .Append(("Bootstrap:AdminPassword", "short")).ToArray();

        var error = BootstrapAccounts.Validate(Config(values));
        Assert.NotNull(error);
        Assert.Contains(BootstrapAccounts.MinPasswordLength.ToString(), error);
    }
}
