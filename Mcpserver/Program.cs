using Mcpserver.Application.Interfaces;
using Mcpserver.Application.Services;
using Mcpserver.Infrastructure.Data;
using Mcpserver.Infrastructure.Repositories;
using Mcpserver.Shared.Observability;
using Mcpserver.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Serilog.Events;
using Serilog.Settings.Configuration;
using Serilog.Sinks.OpenTelemetry;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Unhandled exception capturada pelo AppDomain. IsTerminating={IsTerminating}", e.IsTerminating);
        Console.Error.WriteLine($"[FATAL] UnhandledException capturada. IsTerminating={e.IsTerminating}. Exception={ex}");
    }
    catch (Exception logEx)
    {
        Console.Error.WriteLine($"[FATAL] Falha ao registrar UnhandledException: {logEx}");
    }
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    try
    {
        Log.Fatal(e.Exception, "Unobserved task exception capturada pelo TaskScheduler");
        Console.Error.WriteLine($"[FATAL] UnobservedTaskException capturada: {e.Exception}");
        e.SetObserved();
    }
    catch (Exception logEx)
    {
        Console.Error.WriteLine($"[FATAL] Falha ao registrar UnobservedTaskException: {logEx}");
    }
};

var useHttp = args.Any(a => a.Equals("--http", StringComparison.OrdinalIgnoreCase));

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
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .MinimumLevel.Override("System", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console()
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

var serilogOpts = new ConfigurationReaderOptions(
    typeof(ConsoleLoggerConfigurationExtensions).Assembly,
    typeof(Serilog.Sinks.File.FileSink).Assembly
);

// ─────────────────────────────────────────────────────────────────────────────
// MODO STDIO
// ─────────────────────────────────────────────────────────────────────────────
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
        Log.Information("Iniciando MCP em modo STDIO");
        Console.WriteLine("[INFO] Iniciando MCP em modo STDIO");

        var host = builder.Build();

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStarted.Register(() =>
        {
            Log.Information("STDIO ApplicationStarted disparado");
            Console.WriteLine("[INFO] STDIO ApplicationStarted disparado");
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            Log.Warning("STDIO ApplicationStopping disparado");
            Console.WriteLine("[WARN] STDIO ApplicationStopping disparado");
        });

        lifetime.ApplicationStopped.Register(() =>
        {
            Log.Warning("STDIO ApplicationStopped disparado");
            Console.WriteLine("[WARN] STDIO ApplicationStopped disparado");
        });

        await host.RunAsync();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "MCP STDIO finalizou com erro");
        Console.Error.WriteLine($"[FATAL] MCP STDIO finalizou com erro: {ex}");
        throw;
    }
    finally
    {
        Log.CloseAndFlush();
    }

    return;
}

// ─────────────────────────────────────────────────────────────────────────────
// MODO HTTP
// ─────────────────────────────────────────────────────────────────────────────
var web = WebApplication.CreateBuilder(args);

Log.Logger = BuildSerilog(web.Configuration, serilogOpts);

web.Logging.ClearProviders();
web.Logging.AddSerilog(Log.Logger, dispose: true);

RegisterAppServices(web.Services, web.Configuration);

web.WebHost.UseUrls("http://0.0.0.0:8080");

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

app.Lifetime.ApplicationStarted.Register(() =>
{
    Log.Information("HTTP ApplicationStarted disparado");
    Console.WriteLine("[INFO] HTTP ApplicationStarted disparado");
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    Log.Warning("HTTP ApplicationStopping disparado");
    Console.WriteLine("[WARN] HTTP ApplicationStopping disparado");
});

app.Lifetime.ApplicationStopped.Register(() =>
{
    Log.Warning("HTTP ApplicationStopped disparado");
    Console.WriteLine("[WARN] HTTP ApplicationStopped disparado");
});

var lifetimeCts = new CancellationTokenSource();

_ = Task.Run(async () =>
{
    while (!lifetimeCts.Token.IsCancellationRequested)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();

            var workingSetMb = Math.Round(proc.WorkingSet64 / 1024d / 1024d, 2);
            var privateMemoryMb = Math.Round(proc.PrivateMemorySize64 / 1024d / 1024d, 2);
            var threads = proc.Threads.Count;

            Log.Information(
                "Heartbeat PID={Pid} WorkingSetMB={WorkingSetMB} PrivateMemoryMB={PrivateMemoryMB} Threads={Threads}",
                Environment.ProcessId,
                workingSetMb,
                privateMemoryMb,
                threads
            );

            Console.WriteLine(
                $"[HEARTBEAT] PID={Environment.ProcessId} WorkingSetMB={workingSetMb} PrivateMemoryMB={privateMemoryMb} Threads={threads}"
            );
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao registrar heartbeat");
            Console.Error.WriteLine($"[WARN] Falha ao registrar heartbeat: {ex}");
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetimeCts.Token);
        }
        catch (TaskCanceledException)
        {
            break;
        }
    }
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    lifetimeCts.Cancel();
});

app.MapGet("/health", (HttpContext ctx) =>
{
    Log.Information(
        "HEALTH REQ Method={Method} Path={Path} UserAgent={UserAgent} RemoteIp={RemoteIp}",
        ctx.Request.Method,
        ctx.Request.Path,
        ctx.Request.Headers.UserAgent.ToString(),
        ctx.Connection.RemoteIpAddress?.ToString() ?? ""
    );

    Console.WriteLine(
        $"[HEALTH REQ] Method={ctx.Request.Method} Path={ctx.Request.Path} UserAgent={ctx.Request.Headers.UserAgent} RemoteIp={ctx.Connection.RemoteIpAddress}"
    );

    return new
    {
        ok = true,
        service = "mcpserver",
        mode = "http",
        utc = DateTime.UtcNow,
        machine = Environment.MachineName,
        pid = Environment.ProcessId
    };
});

app.UseCors("mcp");
app.UseRouting();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        Log.Information(
            "MCP REQ Method={Method} Path={Path} QueryString={QueryString} Accept={Accept} ContentType={ContentType} UserAgent={UserAgent}",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.Request.QueryString.ToString(),
            ctx.Request.Headers.Accept.ToString(),
            ctx.Request.ContentType ?? "",
            ctx.Request.Headers.UserAgent.ToString()
        );

        Console.WriteLine(
            $"[MCP REQ] Method={ctx.Request.Method} Path={ctx.Request.Path} QueryString={ctx.Request.QueryString} Accept={ctx.Request.Headers.Accept} ContentType={ctx.Request.ContentType ?? ""} UserAgent={ctx.Request.Headers.UserAgent}"
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

        Console.WriteLine(
            $"[MCP RESP] Method={ctx.Request.Method} Path={ctx.Request.Path} StatusCode={ctx.Response.StatusCode} ResponseContentType={ctx.Response.ContentType ?? ""}"
        );
    }
});

app.MapMcp("/mcp");

try
{
    Log.Information("Iniciando MCP HTTP em http://0.0.0.0:8080/mcp");
    Console.WriteLine("[INFO] Iniciando MCP HTTP em http://0.0.0.0:8080/mcp");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCP HTTP finalizou com erro");
    Console.Error.WriteLine($"[FATAL] MCP HTTP finalizou com erro: {ex}");
    throw;
}
finally
{
    Log.CloseAndFlush();
}