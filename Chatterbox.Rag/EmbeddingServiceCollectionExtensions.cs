using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Chatterbox.Rag;

public static class EmbeddingServiceCollectionExtensions
{
    private const string EmbeddingsHttpClientName = "Chatterbox.Rag.Embeddings";

    /// <summary>
    /// Registers embedding generation dependencies using the OpenAI embedding client.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddEmbeddingGeneration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(EmbeddingsHttpClientName);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            var clientOptions = new OpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(EmbeddingsHttpClientName))
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Endpoint = new Uri(options.BaseUrl);
            }

            return new EmbeddingClient(options.Model, new ApiKeyCredential(options.ApiKey), clientOptions);
        });

        services.AddSingleton<IEmbeddingGenerator, OpenAIEmbeddingGenerator>();

        return services;
    }
}
