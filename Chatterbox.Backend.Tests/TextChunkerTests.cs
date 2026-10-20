using AwesomeAssertions;
using Chatterbox.Rag;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class TextChunkerTests
{
    [Test]
    public void Chunk_WhenTextExceedsLimit_SplitsWithConfiguredOverlap()
    {
        var options = Options.Create(new ChunkingOptions
        {
            MaxCharactersPerChunk = 20,
            OverlapCharacters = 5
        });

        var chunker = new TextChunker(options);

        var chunks = chunker.Chunk("one two three four five six seven eight nine", pageNumber: 2);

        chunks.Should().NotBeEmpty();
        chunks.Should().OnlyContain(chunk => chunk.PageNumber == 2);
        chunks.Should().OnlyContain(chunk => chunk.Text.Length <= 20);
        chunks.Count.Should().BeGreaterThan(1);
    }
}
