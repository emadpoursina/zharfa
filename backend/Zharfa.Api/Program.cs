// Zharfa API scaffold — Demo 1.
//
// Constraints (see AGENTS.md / Demo 1 PRD §7):
//  - Read-only analytics: every KPI is calculated server-side from approved
//    PostgreSQL views; no client talks to the database directly.
//  - Secrets (DB credentials, AI keys) come from server environment / Docker
//    secrets only — never from the frontend bundle, the repo, or logs.
//  - Demo 1 has no Mabna / SQL Server connection. The post-Demo-1 Mabna read
//    seam (Mabna/, spec 002) is registered but disabled unless
//    ConnectionStrings__Mabna is set; all Mabna access goes through IMabnaReader.

using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Security;

// `reconcile purchasing …` runs the developer CLI instead of the web host (spec 003 FR-017, T055).
var isReconcileCommand = Zharfa.Api.Purchasing.ReconcileCommandLine.IsReconcile(args);

var builder = WebApplication.CreateBuilder(args);
if (isReconcileCommand)
{
    // The CLI prints one JSON line on stdout: every log line goes to stderr instead.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
}

// Data mode (FR-020): required, server-only; refuse to start when missing/invalid
// before anything else touches configuration or the database.
var dataMode = DataModeOptions.FromConfiguration(DataModeOptions.ReadConfiguredMode(builder.Configuration));
builder.Services.AddSingleton(dataMode);

// Sync, source-map and purchasing settings (contracts/cli-and-config.md §2).
// Built once, through the blank-tolerant reader (an empty Sync__PageSize falls back to 5000).
var syncSettings = SyncOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(syncSettings));
builder.Services.Configure<MabnaSyncOptions>(builder.Configuration.GetSection(MabnaSyncOptions.SectionName));
builder.Services.Configure<PurchasingOptions>(builder.Configuration.GetSection(PurchasingOptions.SectionName));

// Fail fast, before any database work, on settings that cannot be honoured: an unknown
// Sync__TimeZone, a Sync__PageSize above the effective MabnaSource__MaxRows, or a
// Purchasing__DiscountBasis that is invalid or, in production, differs from the verified
// basis in the source map (FR-014: hard refusal, never silent).
SyncOptions.ResolveTimeZone(syncSettings.TimeZone);
var startupMabnaSourceSettings = builder.Configuration.GetSection(MabnaSourceOptions.SectionName).Get<MabnaSourceOptions>() ?? new MabnaSourceOptions();
SyncOptions.ValidatePageSize(syncSettings.PageSize, startupMabnaSourceSettings.EffectiveMaxRows);

var startupPurchasingSettings = builder.Configuration.GetSection(PurchasingOptions.SectionName).Get<PurchasingOptions>() ?? new PurchasingOptions();
var startupSourceMapFile = builder.Configuration.GetSection(MabnaSyncOptions.SectionName).Get<MabnaSyncOptions>()?.SourceMapFile;
var verifiedDiscountBasis = SourceMap.LoadFromFile(startupSourceMapFile).Map?.DiscountBasisValue;
var discountBasisError = PurchasingDiscountBasis.Validate(
    startupPurchasingSettings.DiscountBasis, dataMode.IsProduction, verifiedDiscountBasis);
if (discountBasisError is not null)
{
    throw new InvalidOperationException(discountBasisError);
}

// Data-quality settings (spec 005, US4): the earliest plausible document date. An invalid value refuses startup and names the key.
var dataQualityOptions = Zharfa.Api.DataQuality.DataQualityOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(dataQualityOptions);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors.Select(e =>
                {
                    if (!string.IsNullOrWhiteSpace(e.ErrorMessage) &&
                        e.ErrorMessage.Any(c => c >= '\u0600' && c <= '\u06FF'))
                    {
                        return e.ErrorMessage;
                    }

                    var key = kvp.Key?.Trim().TrimStart('$', '.');
                    return !string.IsNullOrWhiteSpace(key)
                        ? $"فیلد {key} نامعتبر است."
                        : "داده‌های ورودی نامعتبر است.";
                }))
                .ToList();

            string message;
            if (errors.Count == 0 || (errors.Count == 1 && errors[0] == "داده‌های ورودی نامعتبر است."))
            {
                message = "داده‌های ورودی نامعتبر است.";
            }
            else
            {
                var distinctErrors = errors.Where(err => err != "داده‌های ورودی نامعتبر است.").Distinct().ToList();
                message = distinctErrors.Count > 0
                    ? "داده‌های ورودی نامعتبر است: " + string.Join("، ", distinctErrors)
                    : "داده‌های ورودی نامعتبر است.";
            }

            return new BadRequestObjectResult(new { message });
        };
    });

builder.Services.AddDbContext<ZharfaDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Zharfa");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddScoped<ISalesAnalytics, SalesAnalytics>();
builder.Services.AddScoped<IInventoryAnalytics, InventoryAnalytics>();
builder.Services.AddScoped<IReceivablesAnalytics, ReceivablesAnalytics>();
builder.Services.AddScoped<IReconciliationService, ReconciliationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<AuditLogger>();

builder.Services.AddMabnaSource(builder.Configuration);

// Purchasing sync (spec 003, US2): store, runner, coordinator and the nightly scheduler.
builder.Services.AddPurchasingSync(builder.Configuration, builder.Environment, dataMode);
builder.Services.AddSingleton<PurchasingReconciliation>();
// Overview (spec 003, US5): server-side purchasing figures for Manager and Admin (contracts/api.md §3).
builder.Services.AddScoped<PurchasingOverviewService>();
// Purchasing reports (spec 004): report engine, vendor directory and profile, vendor comparison (contracts/api.md).
builder.Services.AddScoped<Zharfa.Api.Purchasing.Reports.PurchasingReportService>();
builder.Services.AddScoped<Zharfa.Api.Purchasing.Reports.VendorDirectoryService>();
builder.Services.AddScoped<Zharfa.Api.Purchasing.Reports.VendorComparisonService>();
builder.Services.AddScoped<Zharfa.Api.Purchasing.Reports.ExcelExportService>();
builder.Services.AddScoped<Zharfa.Api.SavedReports.SavedReportService>();
// Users and roles administration (research R4): the admin actions and the last-Admin transaction.
builder.Services.AddScoped<Zharfa.Api.Users.UserAdminService>();
// Audit log (spec 005, US3): the in-memory report-open tracker (one per process, research R9) and the read-only query.
builder.Services.AddSingleton<Zharfa.Api.Audit.ReportOpenTracker>();
builder.Services.AddScoped<Zharfa.Api.Audit.AuditLogService>();
builder.Services.AddScoped<Zharfa.Api.DataQuality.DataQualityService>();
builder.Services.AddScoped<Zharfa.Api.DataQuality.DataQualityNoticeService>();

var capturePath = ConfigurationKeys.Get(builder.Configuration, "AiProvider__CapturePath")
    ?? builder.Configuration["AiProvider:CapturePath"]
    ?? builder.Configuration["AiProvider__CapturePath"];

if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(capturePath))
{
    builder.Services.AddTransient<CloudRequestCaptureHandler>();
    builder.Services.AddHttpClient("ai", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    }).AddHttpMessageHandler<CloudRequestCaptureHandler>();
}
else
{
    builder.Services.AddHttpClient("ai", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}

builder.Services.AddSingleton<OpenAiCompatibleChatClient>();
builder.Services.AddSingleton<AiQuestionGate>();
builder.Services.AddScoped<AiProviderSettings>();
builder.Services.AddScoped<IAiProviderSettings>(sp => sp.GetRequiredService<AiProviderSettings>());
builder.Services.AddScoped<OllamaAiProvider>();
builder.Services.AddScoped<CloudAiProvider>();
builder.Services.AddScoped<ActiveAiProvider>();
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<ActiveAiProvider>());
builder.Services.AddScoped<AnalystTools>();
builder.Services.AddScoped<PurchasingAnalystTools>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "zharfa_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Role, activity and forced-change flag are re-read from app_user on every request (research R3).
        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync("{\"message\":\"احراز هویت انجام نشده است یا نشست منقضی شده است.\"}");
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsync("{\"message\":\"شما دسترسی لازم برای انجام این عملیات را ندارید.\"}");
        };
    });

// Permission policies (research R1, contracts/api.md §0): one policy per permission id. A refused call goes through
// AuditingAuthorizationResultHandler, which answers 401/403 in the Persian envelope and writes access.denied on a 403.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
    }
});

var app = builder.Build();

// Run forward-only SQL migrations at startup
await Zharfa.Api.Data.MigrationRunner.RunAsync(app.Configuration, app.Logger);

// Data-mode gate: record the mode on first start; refuse a mode flip or a
// production install that still carries demo seed files (FR-020/FR-021).
await DataModeGate.RunAsync(app.Configuration, dataMode, app.Logger);

// Developer CLI: reconcile purchasing (FR-017). It returns its exit code (0 pass, 1 mismatch, 2 error)
// before the first-run accounts, startup recovery and the web pipeline run, so the CLI never marks a
// sync job of a running API as interrupted.
if (isReconcileCommand)
{
    return await Zharfa.Api.Purchasing.ReconcileCli.RunAsync(app.Services, args, CancellationToken.None);
}

// Production first-run accounts from Bootstrap__* (never logged).
using (var bootstrapScope = app.Services.CreateScope())
{
    var bootstrapDb = bootstrapScope.ServiceProvider.GetRequiredService<ZharfaDbContext>();
    await BootstrapAccounts.EnsureAsync(dataMode, app.Configuration, bootstrapDb, app.Logger);
}

// Sync startup recovery: a run interrupted by a restart is recorded as failed before the
// scheduler can start a new one (it only acts when no other process holds the sync lock).
await app.Services.RecoverInterruptedSyncRunsAsync(app.Logger);

// Source map check at startup: a warning (no secrets) when the map is invalid; the Data sync page shows why.
await app.Services.WarnIfSourceMapInvalidAsync(app.Logger);

// Global exception handling middleware guaranteeing Persian error envelope and no sensitive leaks
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ArgumentException ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Zharfa.Api.ErrorMiddleware");
        logger.LogWarning("Bad request [{ExceptionType}]: {ErrorMessage}", ex.GetType().Name, RedactionHelper.Scrub(ex.Message));

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json; charset=utf-8";

            var rawMessage = ex.Message ?? string.Empty;
            var paramIdx = rawMessage.IndexOf("(Parameter '", StringComparison.OrdinalIgnoreCase);
            if (paramIdx >= 0)
            {
                rawMessage = rawMessage[..paramIdx].Trim();
            }
            var scrubbed = RedactionHelper.Scrub(rawMessage);
            var message = string.IsNullOrWhiteSpace(scrubbed) ? "داده‌های ورودی نامعتبر است." : scrubbed;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
        }
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Zharfa.Api.ErrorMiddleware");
        logger.LogError("Unhandled exception [{ExceptionType}]: {ErrorMessage}", ex.GetType().Name, RedactionHelper.Scrub(ex.Message));

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"خطایی در پردازش درخواست رخ داد.\"}");
        }
    }
});

// Persian envelope for framework-level status codes (404 unknown route, 405 bad method, etc.)
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    response.ContentType = "application/json; charset=utf-8";

    var message = response.StatusCode switch
    {
        StatusCodes.Status404NotFound => "مسیر درخواستی یافت نشد.",
        StatusCodes.Status405MethodNotAllowed => "روش درخواستی برای این مسیر مجاز نیست.",
        StatusCodes.Status401Unauthorized => "احراز هویت انجام نشده است یا نشست منقضی شده است.",
        StatusCodes.Status403Forbidden => "شما دسترسی لازم برای انجام این عملیات را ندارید.",
        _ => "خطایی در پردازش درخواست رخ داد."
    };

    await response.WriteAsync(JsonSerializer.Serialize(new { message }));
});

// Scaffold-level health endpoint used by Compose, the nginx proxy (/api/*) and validation.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    component = "zharfa-api",
    demoData = dataMode.IsDemo,
}));

app.UseAuthentication();
// A session that must change its password first may only reach the change itself, the profile and sign-out (research R6).
app.UseMiddleware<PasswordChangeGate>();
app.UseAuthorization();

app.MapControllers();

app.Run();

return 0;
