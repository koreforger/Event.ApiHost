using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.ApiHost.Auth;

internal sealed class ServiceApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ServiceClientStore _store;

    public ServiceApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ServiceClientStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ServiceApiKeyDefaults.ApiKeyHeader, out var apiKeyValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var apiKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        var client = _store.FindByApiKey(apiKey);
        if (client is null)
        {
            Logger.LogWarning("Rejected unknown API key for client lookup.");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var claims = new List<Claim>
        {
            new(ServiceApiKeyDefaults.ClientIdClaimType, client.ClientId),
            new(ClaimTypes.Name, client.ClientId),
        };

        foreach (var role in client.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
