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
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Settings.Configuration;
using Serilog.Sinks.OpenTelemetry;

var useHttp = args.Any(a => a.Equals("--http", StringComparison.OrdinalIgnoreCase));

// ── Helpers ────────────────────────────────────────────────────────────────

static string GetOtelEndpoint(IConfiguration cfg)
    => cfg["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";

static ResourceBuilder BuildResource()
    => ResourceBuilder.CreateDefault()
        .AddService("mcpserver", serviceVersion: "1.0.0");

static Serilog.Core.Logger BuildSerilog(
    IConfiguration cfg,
    ConfigurationReaderOptions opts)
{
    var otelEndpoint = GetOtelEndpoint(cfg);

    return new LoggerConfiguration()
        .ReadFrom.Configuration(cfg, opts)
        .WriteTo.OpenTelemetry(o =>
        {
            o.Endpoint = otelEndpoint + "/v1/logs";
            o.Protocol = OtlpProtocol.HttpProtobuf;
            o.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "mcpserver",
                ["service.version"] = "1.0.0"
            };
        })
        .CreateLogger();
}

static IOpenTelemetryBuilder ConfigureOtel(
    IOpenTelemetryBuilder otel,
    IConfiguration cfg)
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
                o.Protocol = OtlpExportProtocol.Grpc;
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
                o.Protocol = OtlpExportProtocol.Grpc;
            }));
}

static void RegisterAppServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlServer(configuration.GetConnectionString("Default")));

    services.AddScoped<IAlarmRepository, AlarmRepository>();
    services.AddScoped<AlarmService>();

    services.AddScoped<IMeterRepository, MeterRepository>();
    services.AddScoped<IMeterService, MeterService>();
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

    RegisterAppServices(builder.Services, builder.Configuration);

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

RegisterAppServices(web.Services, web.Configuration);

web.Services.AddCors(options =>
{
    options.AddPolicy("mcp", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

web.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<AlarmTools>()
    .WithTools<MeterTools>();

ConfigureOtel(web.Services.AddOpenTelemetry(), web.Configuration);

var app = web.Build();

// Healthcheck simples
app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "mcpserver",
    mode = "http",
    utc = DateTime.UtcNow
}));

app.UseCors("mcp");
app.UseRouting();

// Log de entrada no /mcp
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        Log.Information(
            "MCP REQ Method={Method} Path={Path} QueryString={QueryString} Accept={Accept} ContentType={ContentType} UserAgent={UserAgent}",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.Request.QueryString.ToString(),
            ctx.Request.Headers.Accept.ToString(),
            ctx.Request.ContentType ?? "",
            ctx.Request.Headers.UserAgent.ToString()
        );
    }

    await next();

    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        Log.Information(
            "MCP RESP Method={Method} Path={Path} StatusCode={StatusCode} ResponseContentType={ResponseContentType}",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.Response.StatusCode,
            ctx.Response.ContentType ?? ""
        );
    }
});

// Endpoint MCP
app.MapMcp("/mcp");

try
{
    Log.Information("Iniciando MCP HTTP em /mcp");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCP HTTP finalizou com erro");
    throw;
}
finally
{
    Log.CloseAndFlush();
}