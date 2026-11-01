// Zharfa API scaffold — Demo 1.
//
// Constraints (see AGENTS.md / Demo 1 PRD §7):
//  - Read-only analytics: every KPI is calculated server-side from approved
//    PostgreSQL views; no client talks to the database directly.
//  - Secrets (DB credentials, AI keys) come from server environment / Docker
//    secrets only — never from the frontend bundle, the repo, or logs.
//  - Demo 1 has no Mabna / SQL Server connection of any kind.

using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zharfa.Api.Ai;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpClient("ai", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<AnalystTools>();
builder.Services.AddScoped<OllamaAiProvider>();
builder.Services.AddScoped<CloudAiProvider>();
builder.Services.AddScoped<IAiProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var providerName = config["AiProvider:Name"] ?? config["AiProvider__Name"] ?? string.Empty;
    if (string.Equals(providerName.Trim(), "ollama", StringComparison.OrdinalIgnoreCase))
    {
        return sp.GetRequiredService<OllamaAiProvider>();
    }
    return sp.GetRequiredService<CloudAiProvider>();
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "zharfa_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

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

builder.Services.AddAuthorization();

var app = builder.Build();

// Run forward-only SQL migrations at startup
await Zharfa.Api.Data.MigrationRunner.RunAsync(app.Configuration, app.Logger);

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
    demoData = true,
}));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
