using Amazon;
using Amazon.DynamoDBv2;
using Chatterbox.Rag;
using Chatterbox.Rag.Ingestion;
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

            return new AmazonDynamoDBClient(new AmazonDynamoDBConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "ca-central-1",
                RegionEndpoint = RegionEndpoint.CACentral1
            });
        });

        builder.Services.AddOptions<ChunkingOptions>()
            .Bind(builder.Configuration.GetSection(ChunkingOptions.SectionName));
        builder.Services.AddOptions<RagOptions>()
            .Bind(builder.Configuration.GetSection(RagOptions.SectionName));
        builder.Services.AddOptions<EmbeddingOptions>()
            .Bind(builder.Configuration.GetSection(EmbeddingOptions.SectionName));

        builder.Services.PostConfigure<RagOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.TableName))
            {
                options.TableName = builder.Configuration["RAG_TABLE"] ?? string.Empty;
            }
        });

        builder.Services.PostConfigure<EmbeddingOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                options.BaseUrl = builder.Configuration["EMBEDDINGS_BASE_URL"] ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = builder.Configuration["EMBEDDINGS_API_KEY"] ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                options.Model = builder.Configuration["EMBEDDINGS_MODEL"] ?? string.Empty;
            }
        });

        builder.Services.AddSingleton<TextChunker>();
        builder.Services.AddSingleton<IRagRepository, DynamoDbRagRepository>();
        builder.Services.AddSingleton<IEmbeddingGenerator, SemanticKernelEmbeddingGenerator>();
        builder.Services.AddSingleton<PdfIngestionRunner>();

        using var host = builder.Build();

        var embeddingOptions = host.Services.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        if (string.IsNullOrWhiteSpace(embeddingOptions.BaseUrl)
            || string.IsNullOrWhiteSpace(embeddingOptions.ApiKey)
            || string.IsNullOrWhiteSpace(embeddingOptions.Model))
        {
            Console.Error.WriteLine("Embedding configuration missing. Set RagEmbeddings in appsettings.json or EMBEDDINGS_BASE_URL, EMBEDDINGS_API_KEY, and EMBEDDINGS_MODEL.");
            return 1;
        }

        var runner = host.Services.GetRequiredService<PdfIngestionRunner>();
        return await runner.RunAsync(pdfFolderPath, cts.Token).ConfigureAwait(false);
    }
}
