using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Annotations;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
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

        var secureKeyOverrides = ResolveApiKeyConfigurationOverrides(hostBuilder.Configuration);
        if (secureKeyOverrides.Count > 0)
        {
            hostBuilder.Configuration.AddInMemoryCollection(secureKeyOverrides);
        }

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

        var configuration = hostBuilder.Configuration;
        var hasDeepSeekConfig = !string.IsNullOrWhiteSpace(configuration["DEEPSEEK_BASE_URL"])
            && !string.IsNullOrWhiteSpace(configuration["DEEPSEEK_API_KEY"])
            && !string.IsNullOrWhiteSpace(configuration["DEEPSEEK_MODEL"]);
        var hasEmbeddingConfig = !string.IsNullOrWhiteSpace(configuration["EMBEDDINGS_BASE_URL"])
            && !string.IsNullOrWhiteSpace(configuration["EMBEDDINGS_API_KEY"])
            && !string.IsNullOrWhiteSpace(configuration["EMBEDDINGS_MODEL"]);

        if (hasDeepSeekConfig && hasEmbeddingConfig)
        {
            hostBuilder.Services.AddEmbeddingGeneration();
            hostBuilder.Services.AddSingleton<IModelClient, DeepSeekClient>();
        }
        else
        {
            hostBuilder.Services.AddSingleton<IEmbeddingGenerator, NullEmbeddingGenerator>();
            hostBuilder.Services.AddSingleton<IModelClient, NullModelClient>();
        }

        hostBuilder.Services.AddSingleton<IRagRepository, DynamoDbRagRepository>();
        hostBuilder.Services.AddSingleton<RagRetrievalService>();

        return hostBuilder;
    }

    private static Dictionary<string, string?> ResolveApiKeyConfigurationOverrides(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var stageName = configuration["STAGE_NAME"];

        if (string.IsNullOrWhiteSpace(stageName))
        {
            return overrides;
        }

        var normalizedStageName = stageName.Trim().ToLowerInvariant();
        AddSecureParameterOverride(overrides, $"/chatterbox/{normalizedStageName}/openai/api-key", "EMBEDDINGS_API_KEY");

        var hasDeepSeekEnabled = !string.IsNullOrWhiteSpace(configuration["DEEPSEEK_BASE_URL"])
            || !string.IsNullOrWhiteSpace(configuration["DEEPSEEK_MODEL"]);

        if (hasDeepSeekEnabled)
        {
            AddSecureParameterOverride(overrides, $"/chatterbox/{normalizedStageName}/deepseek/api-key", "DEEPSEEK_API_KEY");
        }

        return overrides;
    }

    private static void AddSecureParameterOverride(
        IDictionary<string, string?> overrides,
        string parameterName,
        string targetConfigurationKey)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetConfigurationKey);

        var parameterValue = GetSecureStringParameterValue(parameterName);
        if (string.IsNullOrWhiteSpace(parameterValue))
        {
            throw new InvalidOperationException($"SSM parameter '{parameterName}' did not return a value.");
        }

        overrides[targetConfigurationKey] = parameterValue;
    }

    private static string GetSecureStringParameterValue(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        using var ssmClient = new AmazonSimpleSystemsManagementClient();
        var response = ssmClient.GetParameterAsync(new GetParameterRequest
        {
            Name = parameterName,
            WithDecryption = true
        }).GetAwaiter().GetResult();

        return response.Parameter?.Value
            ?? throw new InvalidOperationException($"SSM parameter '{parameterName}' was not found.");
    }
}
