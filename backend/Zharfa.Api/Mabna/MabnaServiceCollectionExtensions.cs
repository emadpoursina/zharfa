namespace Zharfa.Api.Mabna;

using Microsoft.Extensions.Options;

public static class MabnaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Mabna read seam. Always safe to call: with no connection
    /// string the connector is disabled, <see cref="IMabnaReader.IsEnabled"/>
    /// is false, and nothing connects. No endpoints, ingestion or KPI code
    /// are registered here (blocked on spec open questions 1, 4–7).
    /// </summary>
    public static IServiceCollection AddMabnaSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MabnaSourceOptions>()
            .Bind(configuration.GetSection(MabnaSourceOptions.SectionName))
            .PostConfigure(o =>
            {
                // ConnectionStrings__Mabna takes precedence over MabnaSource__ConnectionString.
                var fromConnectionStrings = configuration.GetConnectionString("Mabna");
                if (!string.IsNullOrWhiteSpace(fromConnectionStrings))
                {
                    o.ConnectionString = fromConnectionStrings;
                }
            });

        services.AddSingleton<MabnaConnectionFactory>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MabnaSourceOptions>>().Value;
            return new MabnaObjectPolicy(MabnaObjectPolicy.LoadEntries(options));
        });
        services.AddSingleton<MabnaReadOnlyGate>();
        services.AddSingleton<IMabnaReader, MabnaReader>();
        services.AddHostedService<MabnaStartupCheck>();
        return services;
    }
}

/// <summary>
/// Logs the connector state at startup (no secrets) and, when enabled, runs
/// the read-only permission check eagerly in the background so a writable
/// login is reported at boot rather than on first use. Never blocks or fails
/// startup: an unreachable server leaves the connector unchecked and the
/// check is retried on the first read.
/// </summary>
internal sealed class MabnaStartupCheck(
    IOptions<MabnaSourceOptions> options,
    MabnaObjectPolicy policy,
    MabnaReadOnlyGate gate,
    ILogger<MabnaStartupCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.IsEnabled)
        {
            logger.LogInformation("Mabna source disabled (no ConnectionStrings__Mabna / MabnaSource__ConnectionString).");
            return;
        }

        if (!string.IsNullOrWhiteSpace(o.AllowlistFile) && !File.Exists(o.AllowlistFile))
        {
            logger.LogWarning("Mabna allowlist file is configured but not found; only MabnaSource__AllowedObjects entries apply.");
        }

        foreach (var rejected in policy.RejectedConfigEntries)
        {
            logger.LogWarning("Mabna allowlist entry '{Entry}' ignored (invalid name or hard exclusion).", rejected);
        }

        logger.LogInformation("Mabna source enabled: {Options}; {AllowedCount} allowlisted objects.", o.ToString(), policy.AllowedCount);
        if (policy.AllowedCount == 0)
        {
            logger.LogWarning("Mabna allowlist is empty: every read will be refused.");
        }

        try
        {
            await gate.EnsureReadAllowedAsync(stoppingToken);
        }
        catch (MabnaSourceUnavailableException)
        {
            // Already logged by the gate without secrets.
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A BackgroundService exception would stop the host; the API must start without Mabna.
            logger.LogWarning("Mabna startup check failed [{ExceptionType}]; connector stays unchecked.", ex.GetType().Name);
        }
    }
}
