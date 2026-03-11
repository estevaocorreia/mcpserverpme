using Mcpserver.Shared.Telemetry;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddMcpOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:Endpoint"]
                           ?? "http://localhost:4317";

        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(
                serviceName: TelemetryConfig.ServiceName,
                serviceVersion: TelemetryConfig.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] =
                    configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
                ["host.name"] = Environment.MachineName
            });

        services.AddOpenTelemetry()
            // ── TRACES ──────────────────────────────────────────────
            .WithTracing(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation(opt =>
                {
                    opt.RecordException = true;
                    // ignora health-check para não poluir os traces
                    opt.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation(opt =>
                {
                    opt.RecordException = true;
                })
                .AddSource(TelemetryConfig.ServiceName)   // spans manuais
                .AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri(otlpEndpoint);
                    opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                }))

            // ── METRICS ─────────────────────────────────────────────
            .WithMetrics(builder => builder
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TelemetryConfig.ServiceName)    // métricas customizadas
                .AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri(otlpEndpoint);
                    opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                }));

        return services;
    }
}