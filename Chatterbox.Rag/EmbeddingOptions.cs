namespace Chatterbox.Rag;

public sealed class EmbeddingOptions
{
    public const string SectionName = "RagEmbeddings";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}
