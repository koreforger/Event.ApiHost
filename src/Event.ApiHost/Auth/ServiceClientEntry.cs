namespace Event.ApiHost.Auth;

internal sealed class ServiceClientEntry
{
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
    public string AllowedRoutesPattern { get; set; } = "*";
    public bool Enabled { get; set; } = true;
}
