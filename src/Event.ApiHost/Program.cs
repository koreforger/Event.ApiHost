using Event.ApiHost.Auth;
using Event.ApiHost.Composition;
using Event.ApiHost.Lifecycle;
using Event.FraudIntegrationControllers.CashoutOrchestrator.V1;
using Event.FraudIntegrationControllers.NedbankIdMFA.V1;
using Event.FraudIntegrationControllers.Party.V1;
using Event.FraudIntegrationControllers.Person.V1;
using Event.FraudIntegrationControllers.SASVIFinancialCrimeCaseManagement.V1;
using Event.FraudIntegrationControllers.SuspendAccount.V1;
using Event.FraudIntegrationControllers.SuspendAccount.V2;
using Event.FraudIntegrationControllers.UserDetails.V1;
using Event.FraudIntegrationControllers.UserFederationDetail.V1;
using Event.FraudIntegrationControllers.UserGroupFederationDetail.V1;
using Event.FraudIntegrationControllers.UserGroupFederationDetail.V2;
using Event.FraudIntegrationControllers.UserstateForNID.V1;
using KoreForge.AppLifecycle;
using KoreForge.Logging.Serilog;
using Microsoft.AspNetCore.Authentication;
using KoreForge.Metrics;
using KoreForge.Metrics.AspNet;
using KoreForge.Settings.Extensions;
using KoreForge.SwaggerControllers;
using KoreForge.Web.Authorization.Core;
using KoreForge.Web.Authorization.Dynamic;
using KoreForge.Web.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// -- Settings (SQL-backed) --
builder.Configuration.AddKoreForgeSettings(opts =>
{
    opts.ApplicationId = "Event.ApiHost";
    opts.PollingInterval = TimeSpan.FromMinutes(1);
});
builder.Services.AddKoreForgeSettingsServices(builder.Configuration);

// -- Logging --
builder.Services.AddKFSerilogLogging();

// -- Metrics --
builder.Services.AddKoreForgeMetrics();

// -- Application lifecycle --
// Lifecycle steps are resolved from DI; register them explicitly.
builder.Services.AddTransient<BootstrapStep>();
builder.Services.AddTransient<DrainStep>();
builder.Services.AddApplicationLifecycleManager(opts =>
{
    opts.Startup
        .Flow("Bootstrap")
        .BeginWith<BootstrapStep>()
        .EndFlow();

    opts.Shutdown
        .Flow("Drain")
        .BeginWith<DrainStep>()
        .EndFlow();
});

// -- Incoming authentication (JWT Bearer or Service API Key) --
// ServiceApiKeyDefaults.MultiScheme is a PolicyScheme that forwards to the correct
// handler based on the presence of the X-Api-Key header.  Settings are NOT loaded
// here — they come from appsettings files and KoreForge.Settings background service.
builder.Services.AddSingleton<ServiceClientStore>();
builder.Services
    .AddAuthentication(opts =>
    {
        opts.DefaultScheme = ServiceApiKeyDefaults.MultiScheme;
        opts.DefaultChallengeScheme = ServiceApiKeyDefaults.MultiScheme;
    })
    .AddPolicyScheme(ServiceApiKeyDefaults.MultiScheme, ServiceApiKeyDefaults.MultiSchemeDisplayName, opts =>
    {
        opts.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.ContainsKey(ServiceApiKeyDefaults.ApiKeyHeader)
                ? ServiceApiKeyDefaults.AuthenticationScheme
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    })
    .AddScheme<AuthenticationSchemeOptions, ServiceApiKeyAuthenticationHandler>(
        ServiceApiKeyDefaults.AuthenticationScheme, _ => { });

builder.Services.AddAuthorization();

// -- KoreForge role + dynamic method authorization --
builder.Services.AddRoleAuthorizationCore();
builder.Services.AddDynamicMethodAuthorization([]);

// -- Outbound auth credentials for Nedbank APIs --
builder.Services.Configure<ExternalAuthOptions>(
    builder.Configuration.GetSection("ExternalAuth"));

// -- FraudIntegrationControllers service registrations --
builder.Services.AddCashoutOrchestratorV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddNedbankIdMFAV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddPartyV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddPersonV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddSASVIFinancialCrimeCaseManagementV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddSuspendAccountV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddSuspendAccountV2Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddUserDetailsV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddUserFederationDetailV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddUserGroupFederationDetailV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddUserGroupFederationDetailV2Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));
builder.Services.AddUserstateForNIDV1Services(
    opts => builder.Configuration.GetSection("ExternalAuth").Bind(opts));

// -- Health checks --
builder.Services.AddHealthChecks();

// -- Controllers + FraudIntegration controller parts --
builder.Services
    .AddControllers()
    .AddFraudIntegrationControllers();

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseApplicationLifecycleManager();

app.UseAuthentication();
app.UseAuthorization();
app.UseDynamicMethodAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapKfHealthEndpoints();
app.MapMonitoringEndpoints();
app.MapControllers();

app.Run();

public partial class Program { }
