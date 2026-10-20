namespace Chatterbox.Rag;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public string TableName { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 1536;

    public int TopK { get; set; } = 5;
}
