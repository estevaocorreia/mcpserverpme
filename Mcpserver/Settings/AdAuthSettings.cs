namespace Mcpserver.Settings;

public class AzureAdSettings
{
    public const string Section = "AzureAd";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class ActiveDirectorySettings
{
    public const string Section = "ActiveDirectory";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public string BaseDn { get; set; } = string.Empty;
    public string BindUser { get; set; } = string.Empty;
    public string BindPass { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = false;
}