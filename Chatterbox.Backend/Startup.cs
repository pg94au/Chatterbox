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
            .MinimumLevel.Verbose() // TODO: Allow this to be configured
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSerilog(Log.Logger, dispose: true);

        hostBuilder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        hostBuilder.Services.AddSingleton<IDynamoDBContext>(sp =>
            new DynamoDBContextBuilder()
                .WithDynamoDBClient(sp.GetRequiredService<IAmazonDynamoDB>)
                .Build()
        );
        hostBuilder.Services.AddSingleton<ConnectionsStore>();

        return hostBuilder;
    }
}