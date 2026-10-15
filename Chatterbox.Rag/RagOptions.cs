namespace Chatterbox.Rag;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public string TableName { get; set; } = string.Empty;

    public int TopK { get; set; } = 5;
}
