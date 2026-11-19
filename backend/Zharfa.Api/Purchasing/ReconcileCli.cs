namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Entry point of <c>dotnet run --project backend/Zharfa.Api -- reconcile purchasing …</c> and of the
/// API image (<c>docker compose run --rm api reconcile purchasing …</c>). The web host is not started.
/// Prints one JSON line on stdout (logs go to stderr) and returns the exit code: 0 pass, 1 mismatch, 2 error.
/// </summary>
public static class ReconcileCli
{
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        var isVendorBalance = ReconcileCommandLine.IsVendorBalance(args);
        var parsed = isVendorBalance
            ? ReconcileCommandLine.ParseVendorBalance(args)
            : ReconcileCommandLine.Parse(args);
        if (parsed.Command is null)
        {
            Console.Out.WriteLine(UsageErrorLine(parsed.Error ?? ReconcileCommandLine.Usage));
            return 2;
        }

        if (isVendorBalance)
        {
            // Phase 6 (specs/008-vendor-financials contracts/cli-and-config.md): the vendor-balance command
            // beside purchasing; `reconcile purchasing` is untouched.
            var vendorBalance = services.GetRequiredService<VendorBalanceReconciliation>();
            var vendorReport = await vendorBalance.RunAsync(parsed.Command, cancellationToken);
            Console.Out.WriteLine(vendorReport.ToJsonLine(parsed.Command.ShowAmounts));
            return vendorReport.ExitCode;
        }

        var service = services.GetRequiredService<PurchasingReconciliation>();
        var report = await service.RunAsync(parsed.Command, cancellationToken);
        Console.Out.WriteLine(report.ToJsonLine(parsed.Command.ShowAmounts));
        return report.ExitCode;
    }

    private static string UsageErrorLine(string message) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["outcome"] = ReconcileOutcome.Error,
            ["signOffBlocked"] = true,
            ["reason"] = message,
        }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
