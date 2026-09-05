using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaycomEntraProvisioner.Core.GroupRules;
using PaycomEntraProvisioner.Core.Mapping;
using PaycomEntraProvisioner.Core.Sync;
using PaycomEntraProvisioner.Data;
using PaycomEntraProvisioner.Graph;
using PaycomEntraProvisioner.Paycom;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddPaycomIntegration(builder.Configuration);
builder.Services.AddEntraIntegration(builder.Configuration);
builder.Services.AddProvisionerData(builder.Configuration);

builder.Services.AddScoped<MappingEngine>();
builder.Services.AddScoped<GroupRuleEvaluator>();
builder.Services.AddScoped<SyncOrchestrator>();

var app = builder.Build();

// Apply pending migrations on startup so a fresh environment stands itself
// up without a separate deployment step. Safe to leave in production for
// this kind of low-traffic internal tool; swap for a release-time
// migration step if you'd rather keep schema changes out of app startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProvisionerDbContext>();
    db.Database.Migrate();
}

app.Run();
