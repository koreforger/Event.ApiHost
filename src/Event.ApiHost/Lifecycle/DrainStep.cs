using KoreForge.AppLifecycle.Flows;
using KoreForge.AppLifecycle.Hosting;
using Microsoft.Extensions.Logging;

namespace Event.ApiHost.Lifecycle;

internal sealed class DrainStep : IFlowStep<ShutdownContext>
{
    private readonly ILogger<DrainStep> _logger;

    public DrainStep(ILogger<DrainStep> logger)
    {
        _logger = logger;
    }

    public Task<FlowOutcome> ExecuteAsync(ShutdownContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ApiHost drain complete.");
        return Task.FromResult(FlowOutcome.Success);
    }
}
