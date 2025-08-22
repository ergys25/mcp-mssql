using Akka.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSSQL.MCP.Configuration;
using MSSQL.MCP.Database;
using MSSQL.MCP.Actors;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseKestrel()
            .UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://+:8585");
    })
    .ConfigureAppConfiguration((context, builder) =>
    {
        builder.AddEnvironmentVariables();
        // Map MSSQL_CONNECTION_STRING to Database:ConnectionString
        builder.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Database:ConnectionString", 
                Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
        ]);
    })
    .ConfigureServices((context, services) =>
{
    // Configure logging to stderr for MCP protocol compatibility
    services.AddLogging(builder =>
    {
        builder.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = Microsoft.Extensions.Logging.LogLevel.Trace;
        });
    });

    // Configure Database options with validation
    services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
    services.AddOptionsWithValidateOnStart<DatabaseOptions>()
        .BindConfiguration("Database");

    // Register SQL Connection Factory
    services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

    // Add web services for SSE
    services.AddControllers();
    services.AddCors(options =>
    {
        options.AddDefaultPolicy(builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
    });

    // Add MCP Server with SSE transport
    var transport = Environment.GetEnvironmentVariable("MCP_TRANSPORT");
    if (transport == "sse")
    {
        services.AddMcpServer()
            .WithHttpServerTransport("/")
            .WithToolsFromAssembly();
    }
    else
    {
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
    }

    // Add Akka.NET
    services.AddAkka("MSSQLMcpActorSystem", (builder, sp) =>
    {
        builder
            .ConfigureLoggers(configBuilder =>
            {
                configBuilder.ClearLoggers();
                configBuilder.AddLoggerFactory();
            })
            .WithActors((system, registry, resolver) =>
            {
                // Database validation actor - tests actual connection
                var dbValidationActorProps = resolver.Props<DatabaseValidationActor>();
                var dbValidationActor = system.ActorOf(dbValidationActorProps, "database-validation");
                
                // We would normally register this actor in the registry, but since it dies immediately after validation,
                // there's not much point in keeping it around.
            });
    });
});

var app = hostBuilder.Build();

// Configure the HTTP request pipeline
if (app is WebApplication webApp)
{
    webApp.UseCors();
    webApp.UseRouting();
    
    webApp.MapControllers();
    webApp.MapGet("/health", () => "OK");
}

await app.RunAsync();