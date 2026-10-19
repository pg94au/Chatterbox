using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Annotations;
using Chatterbox.Rag;
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

        hostBuilder.Services.AddSingleton<IOptions<RagOptions>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var topK = int.TryParse(configuration["RAG_TOP_K"], out var parsedTopK)
                ? parsedTopK
                : 5;
            var embeddingDimensions = int.TryParse(configuration["RAG_EMBEDDING_DIMENSIONS"], out var parsedEmbeddingDimensions)
                ? parsedEmbeddingDimensions
                : 1536;

            return Options.Create(new RagOptions
            {
                TableName = configuration["RAG_TABLE"] ?? string.Empty,
                EmbeddingDimensions = embeddingDimensions,
                TopK = topK
            });
        });

        hostBuilder.Services.AddSingleton<IOptions<EmbeddingOptions>>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();

            return Options.Create(new EmbeddingOptions
            {
                BaseUrl = configuration["EMBEDDINGS_BASE_URL"] ?? string.Empty,
                ApiKey = configuration["EMBEDDINGS_API_KEY"] ?? string.Empty,
                Model = configuration["EMBEDDINGS_MODEL"] ?? string.Empty
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

        hostBuilder.Services.AddEmbeddingGeneration();
        hostBuilder.Services.AddSingleton<IRagRepository, DynamoDbRagRepository>();
        hostBuilder.Services.AddSingleton<RagRetrievalService>();

        return hostBuilder;
    }
}
