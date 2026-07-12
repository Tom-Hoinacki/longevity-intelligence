using Longevity.Api.DependencyInjection;
using Longevity.Api.Diagnostics;
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
builder.Services.AddLongevityDiagnostics(builder.Configuration);

var app = builder.Build();

app.MapLongevityDiagnostics();

app.Run();
