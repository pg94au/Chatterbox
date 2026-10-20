using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig;

namespace Chatterbox.Rag.Ingestion;

public sealed class PdfIngestionRunner
{
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly IRagRepository _repository;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<PdfIngestionRunner> _logger;

    public PdfIngestionRunner(
        TextChunker chunker,
        IEmbeddingGenerator embeddings,
        IRagRepository repository,
        IOptions<RagOptions> ragOptions,
        ILogger<PdfIngestionRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ragOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _chunker = chunker;
        _embeddings = embeddings;
        _repository = repository;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ingests PDF files from a folder and persists chunk embeddings.
    /// </summary>
    /// <param name="pdfFolderPath">Folder containing PDF files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process exit code.</returns>
    public async Task<int> RunAsync(string pdfFolderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfFolderPath);

        if (!Directory.Exists(pdfFolderPath))
        {
            _logger.LogError("Folder does not exist: {FolderPath}", pdfFolderPath);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(_ragOptions.TableName))
        {
            _logger.LogError("RAG table configuration missing. Set Rag:TableName or RAG_TABLE.");
            return 1;
        }

        var files = Directory.GetFiles(pdfFolderPath, "*.pdf", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            _logger.LogWarning("No PDF files found in {FolderPath}", pdfFolderPath);
            return 0;
        }

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            _logger.LogInformation("Processing {FileName}", fileName);

            var documentId = Path.GetFileNameWithoutExtension(filePath);
            var contentHash = ComputeSha256(filePath);
            var chunks = new List<RagChunk>();

            var pageTexts = ExtractPageText(filePath);
            var chunkIndexOffset = 0;

            foreach (var page in pageTexts)
            {
                var textChunks = _chunker.Chunk(page.Text, page.PageNumber);
                foreach (var chunk in textChunks)
                {
                    var chunkId = $"{documentId}-{chunkIndexOffset + chunk.ChunkIndex:D6}";

                    float[] embedding;
                    try
                    {
                        embedding = await _embeddings.GenerateEmbeddingAsync(chunk.Text, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Embedding generation failed for {FileName} page {PageNumber} chunk {ChunkIndex}. Stopping ingestion.", fileName, page.PageNumber, chunk.ChunkIndex);
                        return 1;
                    }

                    if (embedding.Length == 0)
                    {
                        _logger.LogError("Embedding generation returned an empty vector for {FileName} page {PageNumber} chunk {ChunkIndex}. Stopping ingestion.", fileName, page.PageNumber, chunk.ChunkIndex);
                        return 1;
                    }

                    chunks.Add(new RagChunk(
                        DocumentId: documentId,
                        ChunkId: chunkId,
                        SourceFileName: fileName,
                        ChunkIndex: chunkIndexOffset + chunk.ChunkIndex,
                        PageNumber: page.PageNumber,
                        Text: chunk.Text,
                        Embedding: embedding,
                        ContentHash: contentHash,
                        CreatedAtUtc: DateTimeOffset.UtcNow));
                }

                chunkIndexOffset += textChunks.Count;
            }

            if (chunks.Count == 0)
            {
                _logger.LogWarning("No chunks were generated for {FileName}", fileName);
                continue;
            }

            await _repository.UpsertChunksAsync(chunks, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Persisted {Count} chunks for {FileName}", chunks.Count, fileName);
        }

        return 0;
    }

    private static IReadOnlyList<(int PageNumber, string Text)> ExtractPageText(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var pages = new List<(int PageNumber, string Text)>();

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add((page.Number, text));
            }
        }

        return pages;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}
