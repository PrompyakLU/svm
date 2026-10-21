using Microsoft.AspNetCore.HttpOverrides;
using SvmApi.Api.Auth;
using SvmApi.Api.Middleware;
using SvmApi.Infrastructure;
using SvmApi.Infrastructure.Admin;
using SvmApi.Infrastructure.Assets;
using SvmApi.Infrastructure.Audit;
using SvmApi.Infrastructure.Dashboard;
using SvmApi.Infrastructure.Intake;
using SvmApi.Infrastructure.Notifications;
using SvmApi.Infrastructure.Persistence.Seed;
using SvmApi.Infrastructure.Reference;
using SvmApi.Infrastructure.Remediation;
using SvmApi.Infrastructure.Sla;
using SvmApi.Infrastructure.Verification;
using SvmApi.Infrastructure.Workflow;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Minimal observability, D3-11): JSON in prod, readable in dev ---
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
}
else
{
    builder.Logging.AddJsonConsole();
}

// --- Services ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Infrastructure seam (binds IntegrationOptions; DbContext/adapters added in later waves)
builder.Services.AddSvmAudit(builder.Configuration);
builder.Services.AddSvmInfrastructure(builder.Configuration);
builder.Services.AddSvmAuth(builder.Configuration, builder.Environment);
builder.Services.AddSvmSeeding(builder.Configuration);
builder.Services.AddSvmReferenceData();
builder.Services.AddSvmAssets(builder.Configuration);
builder.Services.AddSvmIntake(builder.Configuration);
builder.Services.AddSvmWorkflow();
builder.Services.AddSvmNotifications();
builder.Services.AddSvmRemediation(builder.Configuration);
builder.Services.AddSvmSla(builder.Configuration);
builder.Services.AddSvmVerification();
builder.Services.AddSvmDashboard();
builder.Services.AddSvmAdmin();

// Behind ALB/reverse proxy honor X-Forwarded-* (set before auth — workshop C7)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// --- Middleware pipeline ---
app.UseForwardedHeaders();
app.UseMiddleware<RequestIdMiddleware>();

// Centralized exception handling → RFC 7807 ProblemDetails (no stack trace/secret leak — NFR-40)
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

// AuthModule (Task 3.3): no-store for auth routes -> rate limiter -> authentication -> authorization
app.UseSvmAuth();

// Liveness health endpoint (operations.md — basic, not under /api/v1)
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.0.0" }))
   .WithName("Health")
   .AllowAnonymous();

app.MapControllers();

// Idempotent seed (Seed:Enabled only; never migrates)
await app.Services.SeedDatabaseAsync();

app.Run();
