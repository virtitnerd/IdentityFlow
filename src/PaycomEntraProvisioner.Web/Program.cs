using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PaycomEntraProvisioner.Core.GroupRules;
using PaycomEntraProvisioner.Core.Mapping;
using PaycomEntraProvisioner.Core.Sync;
using PaycomEntraProvisioner.Data;
using PaycomEntraProvisioner.Graph;
using PaycomEntraProvisioner.Paycom;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

var adminAppRole = builder.Configuration["AzureAd:AdminAppRole"] ?? "ProvisionerAdmin";
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(adminAppRole)
        .Build();
});

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

builder.Services.AddPaycomIntegration(builder.Configuration);
builder.Services.AddEntraIntegration(builder.Configuration);
builder.Services.AddProvisionerData(builder.Configuration);

builder.Services.AddScoped<MappingEngine>();
builder.Services.AddScoped<GroupRuleEvaluator>();
builder.Services.AddScoped<SyncOrchestrator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProvisionerDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
