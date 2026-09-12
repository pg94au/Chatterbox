using NUnit.Framework;

namespace Chatterbox.Backend.Tests;

internal static class TextWriterExtensions
{
    internal static void Info(this TextWriter textWriter, string message)
    {
        textWriter.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
    }
}
