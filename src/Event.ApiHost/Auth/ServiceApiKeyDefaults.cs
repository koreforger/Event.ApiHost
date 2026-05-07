namespace Event.ApiHost.Auth;

internal static class ServiceApiKeyDefaults
{
    public const string AuthenticationScheme = "ServiceApiKey";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ClientIdClaimType = "client_id";
    public const string MultiScheme = "MultiScheme";
    public const string MultiSchemeDisplayName = "JWT Bearer or Service API Key";
}
