namespace Chatterbox.Rag;

public sealed class ChunkingOptions
{
    public const string SectionName = "RagChunking";

    public int MaxCharactersPerChunk { get; set; } = 1000;

    public int OverlapCharacters { get; set; } = 150;
}
