using Microsoft.Extensions.Options;

namespace Chatterbox.Rag;

public sealed class TextChunker
{
    private readonly ChunkingOptions _options;

    public TextChunker(IOptions<ChunkingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        if (_options.MaxCharactersPerChunk <= 0)
        {
            throw new ArgumentException("MaxCharactersPerChunk must be greater than zero.", nameof(options));
        }

        if (_options.OverlapCharacters < 0 || _options.OverlapCharacters >= _options.MaxCharactersPerChunk)
        {
            throw new ArgumentException("OverlapCharacters must be non-negative and less than MaxCharactersPerChunk.", nameof(options));
        }
    }

    /// <summary>
    /// Splits a text payload into ordered chunks with overlap.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="pageNumber">The source page number.</param>
    /// <returns>Ordered chunks suitable for embedding.</returns>
    public IReadOnlyList<TextChunk> Chunk(string text, int pageNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var normalizedText = NormalizeWhitespace(text);
        if (normalizedText.Length == 0)
        {
            return [];
        }

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;
        var start = 0;

        while (start < normalizedText.Length)
        {
            var maxEndExclusive = Math.Min(start + _options.MaxCharactersPerChunk, normalizedText.Length);
            var endExclusive = FindChunkEnd(normalizedText, start, maxEndExclusive);
            var chunkText = normalizedText[start..endExclusive].Trim();

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new TextChunk(chunkIndex, pageNumber, chunkText));
                chunkIndex++;
            }

            if (endExclusive >= normalizedText.Length)
            {
                break;
            }

            var nextStart = endExclusive - _options.OverlapCharacters;
            start = Math.Max(nextStart, start + 1);
        }

        return chunks;
    }

    private static int FindChunkEnd(string text, int start, int maxEndExclusive)
    {
        if (maxEndExclusive >= text.Length)
        {
            return text.Length;
        }

        for (var i = maxEndExclusive; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
            {
                return i;
            }
        }

        return maxEndExclusive;
    }

    private static string NormalizeWhitespace(string input)
    {
        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', lines).Trim();
    }
}
