namespace Chatterbox.Rag;

public sealed record RagChunk(
    string DocumentId,
    string ChunkId,
    string SourceFileName,
    int ChunkIndex,
    int PageNumber,
    string Text,
    float[] Embedding,
    string ContentHash,
    DateTimeOffset CreatedAtUtc
);
