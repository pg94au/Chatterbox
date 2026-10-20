namespace Chatterbox.Rag;

public interface IEmbeddingGenerator
{
    /// <summary>
    /// Generates an embedding vector for the provided text.
    /// </summary>
    /// <param name="text">Text to vectorize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
