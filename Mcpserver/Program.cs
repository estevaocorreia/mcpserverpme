using Mcpserver.Application.Interfaces;
using Mcpserver.Application.Services;
using Mcpserver.Infrastructure.Data;
using Mcpserver.Infrastructure.Repositories;
using Mcpserver.Shared.Observability;
using Mcpserver.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Settings.Configuration;
using Serilog.Sinks.OpenTelemetry;

var useHttp = args.Any(a => a.Equals("--http", StringComparison.OrdinalIgnoreCase));

// ── Helpers ────────────────────────────────────────────────────────────────

// Lê o endpoint do OTel do appsettings (ex: http://localhost:4317)
static string GetOtelEndpoint(Microsoft.Extensions.Configuration.IConfiguration cfg)
    => cfg["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

// Constrói o ResourceBuilder com info do serviço
static ResourceBuilder BuildResource()
    => ResourceBuilder.CreateDefault()
        .AddService("mcpserver", serviceVersion: "1.0.0");

// Configura o Serilog lendo do appsettings + sink OpenTelemetry para logs
static Serilog.Core.Logger BuildSerilog(
    Microsoft.Extensions.Configuration.IConfiguration cfg,
    ConfigurationReaderOptions opts)
{
    var otelEndpoint = GetOtelEndpoint(cfg);
    return new LoggerConfiguration()
        .ReadFrom.Configuration(cfg, opts)
        .WriteTo.OpenTelemetry(o =>
        {
            o.Endpoint = otelEndpoint + "/v1/logs"; // OTLP HTTP
            o.Protocol = OtlpProtocol.HttpProtobuf;
            o.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "mcpserver",
                ["service.version"] = "1.0.0"
            };
        })
        .CreateLogger();
}

// Configura OpenTelemetry (métricas + traces) com exporter OTLP
static IOpenTelemetryBuilder ConfigureOtel(
    IOpenTelemetryBuilder otel,
    Microsoft.Extensions.Configuration.IConfiguration cfg)
{
    var endpoint = new Uri(GetOtelEndpoint(cfg));

    return otel
        .WithTracing(t => t
            .SetResourceBuilder(BuildResource())
            .AddSource(McpMetrics.ActivitySource.Name)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            }))
        .WithMetrics(m => m
            .SetResourceBuilder(BuildResource())
            .AddMeter("Mcpserver.Tools")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(o =>
            {
                o.Endpoint = endpoint;
                o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            }));
}

// Opções do Serilog para single-file publish
var serilogOpts = new ConfigurationReaderOptions(
    typeof(ConsoleLoggerConfigurationExtensions).Assembly,
    typeof(Serilog.Sinks.File.FileSink).Assembly
);

// ── Modo STDIO ─────────────────────────────────────────────────────────────
if (!useHttp)
{
    var builder = Host.CreateApplicationBuilder(args);

    Log.Logger = BuildSerilog(builder.Configuration, serilogOpts);
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, dispose: true);

    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

    builder.Services.AddScoped<IAlarmRepository, AlarmRepository>();
    builder.Services.AddScoped<AlarmService>();
    builder.Services.AddScoped<IMeterRepository, MeterRepository>();
    builder.Services.AddScoped<IMeterService, MeterService>();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<RandomNumberTools>()
        .WithTools<AlarmTools>()
        .WithTools<MeterTools>();

    ConfigureOtel(builder.Services.AddOpenTelemetry(), builder.Configuration);

    try
    {
        await builder.Build().RunAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "MCP STDIO finalizou com erro");
        throw;
    }
    finally
    {
        Log.CloseAndFlush();
    }

    return;
}

// ── Modo HTTP ──────────────────────────────────────────────────────────────
var web = WebApplication.CreateBuilder(args);

Log.Logger = BuildSerilog(web.Configuration, serilogOpts);
web.Logging.ClearProviders();
web.Logging.AddSerilog(Log.Logger, dispose: true);

web.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(web.Configuration.GetConnectionString("Default")));

web.Services.AddCors(o =>
    o.AddPolicy("mcp", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

web.Services.AddScoped<IAlarmRepository, AlarmRepository>();
web.Services.AddScoped<AlarmService>();
web.Services.AddScoped<IMeterRepository, MeterRepository>();
web.Services.AddScoped<IMeterService, MeterService>();

web.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<AlarmTools>()
    .WithTools<MeterTools>();

ConfigureOtel(web.Services.AddOpenTelemetry(), web.Configuration);

var app = web.Build();
app.UseCors("mcp");
app.UseRouting();
app.MapMcp();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}