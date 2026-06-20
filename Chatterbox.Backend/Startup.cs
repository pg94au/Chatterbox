using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Chatterbox.Backend;

[LambdaStartup]
public class Startup
{
    public HostApplicationBuilder ConfigureHostBuilder()
    {
        var hostBuilder = new HostApplicationBuilder();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSerilog(Log.Logger, dispose: true);

        hostBuilder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        hostBuilder.Services.AddSingleton(sp => new DynamoDBContext(
            sp.GetRequiredService<IAmazonDynamoDB>(),
            new DynamoDBContextConfig()));
        hostBuilder.Services.AddSingleton(sp => new ConnectionsStore(
            sp.GetRequiredService<IAmazonDynamoDB>(),
            sp.GetRequiredService<DynamoDBContext>(),
            Environment.GetEnvironmentVariable("CONNECTIONS_TABLE")
            ?? throw new InvalidOperationException("CONNECTIONS_TABLE not configured")));

        return hostBuilder;
    }
}
