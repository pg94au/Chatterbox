using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace Chatterbox.Backend;

[LambdaStartup]
public class Startup
{
    public HostApplicationBuilder ConfigureHostBuilder()
    {
        var hostBuilder = new HostApplicationBuilder();

        hostBuilder.Configuration.AddEnvironmentVariables();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose() // TODO: Allow this to be configured
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSerilog(Log.Logger, dispose: true);

        hostBuilder.Services.AddSingleton<IOptions<DeepSeekOptions>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();

            return Options.Create(new DeepSeekOptions
            {
                BaseUrl = configuration["DEEPSEEK_BASE_URL"] ?? string.Empty,
                ApiKey = configuration["DEEPSEEK_API_KEY"] ?? string.Empty,
                Model = configuration["DEEPSEEK_MODEL"] ?? string.Empty
            });
        });

        hostBuilder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        hostBuilder.Services.AddSingleton<IDynamoDBContext>(sp =>
            new DynamoDBContextBuilder()
                .WithDynamoDBClient(sp.GetRequiredService<IAmazonDynamoDB>)
                .Build()
        );
        hostBuilder.Services.AddSingleton<ConnectionsStore>();
        hostBuilder.Services.AddSingleton<DeepSeekClient>();

        return hostBuilder;
    }
}
