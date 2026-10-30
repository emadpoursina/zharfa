// Zharfa API scaffold — Demo 1.
//
// Constraints (see AGENTS.md / Demo 1 PRD §7):
//  - Read-only analytics: every KPI is calculated server-side from approved
//    PostgreSQL views; no client talks to the database directly.
//  - Secrets (DB credentials, AI keys) come from server environment / Docker
//    secrets only — never from the frontend bundle, the repo, or logs.
//  - Demo 1 has no Mabna / SQL Server connection of any kind.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

// Scaffold-level health endpoint used by Compose, the nginx proxy (/api/*) and validation.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    component = "zharfa-api",
    demoData = true,
}));

app.MapControllers();

app.Run();
