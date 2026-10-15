namespace Chatterbox.Rag;

public interface IRagRepository
{
    /// <summary>
    /// Upserts RAG chunks and their embeddings.
    /// </summary>
    /// <param name="chunks">Chunks to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertChunksAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for chunks similar to the query embedding.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="topK">Maximum number of matches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of similar chunks.</returns>
    Task<IReadOnlyList<RagSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default);
}
