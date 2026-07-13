using Longevity.Api.DependencyInjection;
using Longevity.Api.Diagnostics;
using Longevity.Api.HumanReview;
using Longevity.Api.Workflow;
using Longevity.Api.PublicEvidence;
using Longevity.Api.PrivateProfile;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "O ";
});

builder.Services.AddLongevityInfrastructure(builder.Configuration);
if (builder.Configuration.GetSection("WorkflowOrchestrator").GetValue<bool>("Enabled"))
{
    builder.Services.AddLongevityApplication();
    builder.Services.AddWorkflowOrchestrator(builder.Configuration);
    builder.Services.AddHostedService<WorkflowOrchestratorBackgroundService>();
}
builder.Services.AddLongevityDiagnostics(builder.Configuration);
builder.Services.AddHumanReviewApi(builder.Configuration);
builder.Services.AddPublicEvidenceApi(builder.Configuration);
builder.Services.AddPrivateProfileApi();

var app = builder.Build();

app.UsePrivateProfileSafeObservability();
app.UseAuthentication();
app.UseAuthorization();

app.MapLongevityDiagnostics();
app.MapHumanReviewApi();
app.MapPublicEvidenceApi();
app.MapPrivateProfileApi();

app.Run();
