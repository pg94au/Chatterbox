namespace Chatterbox.Rag;

public sealed record TextChunk(
    int ChunkIndex,
    int PageNumber,
    string Text
);
