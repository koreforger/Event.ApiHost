using KoreForge.AppLifecycle.Flows;
using KoreForge.AppLifecycle.Hosting;
using Microsoft.Extensions.Logging;

namespace Event.ApiHost.Lifecycle;

// Settings are loaded early by KoreForge.Settings.SettingsReloadBackgroundService,
// registered before AppLifecycle. Do NOT load settings here — they will be in
// IConfiguration before request processing begins.
internal sealed class BootstrapStep : IFlowStep<StartupContext>
{
    private readonly ILogger<BootstrapStep> _logger;

    public BootstrapStep(ILogger<BootstrapStep> logger)
    {
        _logger = logger;
    }

    public Task<FlowOutcome> ExecuteAsync(StartupContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ApiHost bootstrap complete.");
        return Task.FromResult(FlowOutcome.Success);
    }
}
