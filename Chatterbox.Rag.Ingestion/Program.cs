using Amazon;
using Amazon.DynamoDBv2;
using Chatterbox.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatterbox.Rag.Ingestion;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var exitCode = await RunAsync(args).ConfigureAwait(false);
        Environment.ExitCode = exitCode;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: Chatterbox.Rag.Ingestion <pdf-folder-path>");
            return 1;
        }

        var pdfFolderPath = args[0];

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddEnvironmentVariables();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var serviceUrl = builder.Configuration["AWS_SERVICE_URL"];
            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                return new AmazonDynamoDBClient();
            }

            var config = new AmazonDynamoDBConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "us-east-1",
                UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            };

            return new AmazonDynamoDBClient(config);
        });

        builder.Services.AddOptions<ChunkingOptions>()
            .Bind(builder.Configuration.GetSection(ChunkingOptions.SectionName));
        builder.Services.AddOptions<RagOptions>()
            .Bind(builder.Configuration.GetSection(RagOptions.SectionName));
        builder.Services.AddOptions<EmbeddingOptions>()
            .Bind(builder.Configuration.GetSection(EmbeddingOptions.SectionName));

        builder.Services.PostConfigure<RagOptions>(options =>
        {
            var ragTableOverride = builder.Configuration["RAG_TABLE"];
            if (!string.IsNullOrWhiteSpace(ragTableOverride))
            {
                options.TableName = ragTableOverride;
            }

            var ragEmbeddingDimensionsOverride = builder.Configuration["RAG_EMBEDDING_DIMENSIONS"];
            if (int.TryParse(ragEmbeddingDimensionsOverride, out var ragEmbeddingDimensions) && ragEmbeddingDimensions > 0)
            {
                options.EmbeddingDimensions = ragEmbeddingDimensions;
            }
        });

        builder.Services.PostConfigure<EmbeddingOptions>(options =>
        {
            var embeddingsBaseUrlOverride = builder.Configuration["EMBEDDINGS_BASE_URL"];
            if (!string.IsNullOrWhiteSpace(embeddingsBaseUrlOverride))
            {
                options.BaseUrl = embeddingsBaseUrlOverride;
            }

            var embeddingsApiKeyOverride = builder.Configuration["EMBEDDINGS_API_KEY"];
            if (!string.IsNullOrWhiteSpace(embeddingsApiKeyOverride))
            {
                options.ApiKey = embeddingsApiKeyOverride;
            }

            var embeddingsModelOverride = builder.Configuration["EMBEDDINGS_MODEL"];
            if (!string.IsNullOrWhiteSpace(embeddingsModelOverride))
            {
                options.Model = embeddingsModelOverride;
            }
        });

        builder.Services.AddSingleton<TextChunker>();
        builder.Services.AddSingleton<IRagRepository, DynamoDbRagRepository>();
        builder.Services.AddEmbeddingGeneration();
        builder.Services.AddSingleton<PdfIngestionRunner>();

        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IngestionStartup");
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var ragOptions = host.Services.GetRequiredService<IOptions<RagOptions>>().Value;
        var embeddingOptions = host.Services.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        var awsServiceUrl = configuration["AWS_SERVICE_URL"];
        var dynamoDbClient = host.Services.GetRequiredService<IAmazonDynamoDB>();
        var clientConfig = (dynamoDbClient as AmazonDynamoDBClient)?.Config as AmazonDynamoDBConfig;

        logger.LogInformation(
            "Resolved configuration. AWS_SERVICE_URL={AwsServiceUrl}; RAG table={RagTable}; Embeddings base URL={EmbeddingsBaseUrl}; Embeddings model={EmbeddingsModel}; Dynamo ServiceURL={DynamoServiceUrl}; Dynamo RegionEndpoint={DynamoRegion}",
            string.IsNullOrWhiteSpace(awsServiceUrl) ? "<default AWS SDK resolution>" : awsServiceUrl,
            string.IsNullOrWhiteSpace(ragOptions.TableName) ? "<empty>" : ragOptions.TableName,
            string.IsNullOrWhiteSpace(embeddingOptions.BaseUrl) ? "<empty>" : embeddingOptions.BaseUrl,
            string.IsNullOrWhiteSpace(embeddingOptions.Model) ? "<empty>" : embeddingOptions.Model,
            string.IsNullOrWhiteSpace(clientConfig?.ServiceURL) ? "<not set>" : clientConfig.ServiceURL,
            clientConfig?.RegionEndpoint?.SystemName ?? "<not set>");

        if (string.IsNullOrWhiteSpace(embeddingOptions.BaseUrl)
            || string.IsNullOrWhiteSpace(embeddingOptions.ApiKey)
            || string.IsNullOrWhiteSpace(embeddingOptions.Model))
        {
            Console.Error.WriteLine("Embedding configuration missing. Set RagEmbeddings in appsettings.json or EMBEDDINGS_BASE_URL, EMBEDDINGS_API_KEY, and EMBEDDINGS_MODEL.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(ragOptions.TableName))
        {
            try
            {
                await dynamoDbClient.DescribeTableAsync(ragOptions.TableName, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to verify RAG table {RagTable} using configured DynamoDB client.", ragOptions.TableName);
                return 1;
            }
        }

        var runner = host.Services.GetRequiredService<PdfIngestionRunner>();
        return await runner.RunAsync(pdfFolderPath, cts.Token).ConfigureAwait(false);
    }
}
