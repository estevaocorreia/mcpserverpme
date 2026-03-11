namespace Mcpserver.Shared.Telemetry;

public static class TelemetryConfig
{
    public const string ServiceName = "mcpserver";
    public const string ServiceVersion = "1.0.0";

    // ActivitySource usado nos serviços para criar spans manuais
    public static readonly System.Diagnostics.ActivitySource ActivitySource =
        new(ServiceName, ServiceVersion);
}