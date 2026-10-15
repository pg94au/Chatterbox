namespace Chatterbox.Rag;

public sealed record RagSearchResult(
    string DocumentId,
    string ChunkId,
    string SourceFileName,
    int ChunkIndex,
    int PageNumber,
    string Text,
    float Score
);
