using Microsoft.Extensions.Configuration;

namespace Event.ApiHost.Auth;

internal sealed class ServiceClientStore
{
    private readonly IReadOnlyDictionary<string, ServiceClientEntry> _byApiKey;

    public ServiceClientStore(IConfiguration configuration)
    {
        var list = configuration.GetSection("ServiceClients").Get<List<ServiceClientEntry>>() ?? [];
        _byApiKey = list
            .Where(c => c.Enabled && !string.IsNullOrEmpty(c.ApiKey))
            .ToDictionary(c => c.ApiKey, StringComparer.Ordinal);
    }

    public ServiceClientEntry? FindByApiKey(string apiKey) =>
        _byApiKey.TryGetValue(apiKey, out var entry) ? entry : null;
}
