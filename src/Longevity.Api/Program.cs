using Longevity.Api.DependencyInjection;
using Longevity.Api.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "O ";
});

builder.Services.AddLongevityApplication();
builder.Services.AddLongevityInfrastructure(builder.Configuration);
builder.Services.AddWorkflowOrchestrator(builder.Configuration);
builder.Services.AddHostedService<WorkflowOrchestratorBackgroundService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "longevity-api", status = "running" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.Run();
