using Longevity.Api.DependencyInjection;
using Longevity.Api.Diagnostics;
using Longevity.Api.HumanReview;
using Longevity.Api.Workflow;
using Longevity.Api.PublicEvidence;
using Longevity.Infrastructure.ModelProviders.OpenRouter;
using Longevity.Api.PrivateProfile;

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
builder.Services.AddMarketIntelligenceApi(builder.Configuration);
builder.Services.AddWorkflowIntakeApi(builder.Configuration);
builder.Services.AddPrivateProfileApi();

var app = builder.Build();

app.UsePrivateProfileSafeObservability();
app.UseAuthentication();
app.UseAuthorization();

app.MapLongevityDiagnostics();
app.MapHumanReviewApi();
app.MapPublicEvidenceApi();
app.MapMarketIntelligenceApi();
app.MapWorkflowIntakeApi();
app.MapPrivateProfileApi();

app.Run();

