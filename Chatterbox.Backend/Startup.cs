using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chatterbox.Backend;

[LambdaStartup]
public class Startup
{
    public HostApplicationBuilder ConfigureHostBuilder()
    {
        var hostBuilder = new HostApplicationBuilder();

        hostBuilder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        hostBuilder.Services.AddSingleton(sp => new DynamoDBContext(
            sp.GetRequiredService<IAmazonDynamoDB>(),
            new DynamoDBContextConfig()));

        return hostBuilder;
    }
}
