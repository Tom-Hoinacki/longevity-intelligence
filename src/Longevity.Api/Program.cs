using Longevity.Api.DependencyInjection;
using Longevity.Api.Diagnostics;
using Longevity.Api.HumanReview;
using Longevity.Api.Workflow;
using Longevity.Api.PublicEvidence;
using Longevity.Infrastructure.ModelProviders.OpenRouter;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "O ";
});

builder.Services.AddLongevityInfrastructure(builder.Configuration);
var orchestratorEnabled = builder.Configuration.GetSection("WorkflowOrchestrator").GetValue<bool>("Enabled");
var intakeEnabled = builder.Configuration.GetSection("WorkflowIntake").GetValue<bool>("Enabled");
if (orchestratorEnabled || intakeEnabled)
{
    builder.Services.AddLongevityApplication();
}
if (orchestratorEnabled)
{
    builder.Services.AddOpenRouterClaimExtractionModel(builder.Configuration);
    builder.Services.AddWorkflowOrchestrator(builder.Configuration);
    builder.Services.AddHostedService<WorkflowOrchestratorBackgroundService>();
}
builder.Services.AddLongevityDiagnostics(builder.Configuration);
builder.Services.AddHumanReviewApi(builder.Configuration);
builder.Services.AddPublicEvidenceApi(builder.Configuration);
builder.Services.AddWorkflowIntakeApi(builder.Configuration);

var app = builder.Build();

app.MapLongevityDiagnostics();
app.MapHumanReviewApi();
app.MapPublicEvidenceApi();
app.MapWorkflowIntakeApi();

app.Run();
