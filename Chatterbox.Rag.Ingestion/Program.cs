using Amazon;
using Amazon.DynamoDBv2;
using Chatterbox.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig;

var exitCode = await RunAsync(args).ConfigureAwait(false);
Environment.ExitCode = exitCode;

return;

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
    {
        Console.Error.WriteLine("Usage: Chatterbox.Rag.Ingestion <pdf-folder-path>");
        return 1;
    }

    var pdfFolderPath = args[0];
    if (!Directory.Exists(pdfFolderPath))
    {
        Console.Error.WriteLine($"Folder does not exist: {pdfFolderPath}");
        return 1;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddEnvironmentVariables();
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "HH:mm:ss ";
    });

    builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
    {
        var serviceUrl = builder.Configuration["AWS_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            return new AmazonDynamoDBClient();
        }

        return new AmazonDynamoDBClient(new AmazonDynamoDBConfig
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = "ca-central-1",
            RegionEndpoint = RegionEndpoint.CACentral1
        });
    });

    builder.Services.AddOptions<ChunkingOptions>()
        .Bind(builder.Configuration.GetSection(ChunkingOptions.SectionName));
    builder.Services.AddOptions<RagOptions>()
        .Bind(builder.Configuration.GetSection(RagOptions.SectionName));
    builder.Services.AddOptions<EmbeddingOptions>()
        .Bind(builder.Configuration.GetSection(EmbeddingOptions.SectionName));

    builder.Services.PostConfigure<RagOptions>(options =>
    {
        if (string.IsNullOrWhiteSpace(options.TableName))
        {
            options.TableName = builder.Configuration["RAG_TABLE"] ?? string.Empty;
        }
    });

    builder.Services.PostConfigure<EmbeddingOptions>(options =>
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            options.BaseUrl = builder.Configuration["EMBEDDINGS_BASE_URL"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = builder.Configuration["EMBEDDINGS_API_KEY"] ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            options.Model = builder.Configuration["EMBEDDINGS_MODEL"] ?? string.Empty;
        }
    });

    builder.Services.AddSingleton<TextChunker>();
    builder.Services.AddSingleton<IRagRepository, DynamoDbRagRepository>();
    builder.Services.AddSingleton<IEmbeddingGenerator, SemanticKernelEmbeddingGenerator>();

    var embeddingOptions = new EmbeddingOptions();
    builder.Configuration.GetSection(EmbeddingOptions.SectionName).Bind(embeddingOptions);

    var embeddingBaseUrl = string.IsNullOrWhiteSpace(embeddingOptions.BaseUrl)
        ? builder.Configuration["EMBEDDINGS_BASE_URL"] ?? string.Empty
        : embeddingOptions.BaseUrl;
    var embeddingApiKey = string.IsNullOrWhiteSpace(embeddingOptions.ApiKey)
        ? builder.Configuration["EMBEDDINGS_API_KEY"] ?? string.Empty
        : embeddingOptions.ApiKey;
    var embeddingModel = string.IsNullOrWhiteSpace(embeddingOptions.Model)
        ? builder.Configuration["EMBEDDINGS_MODEL"] ?? string.Empty
        : embeddingOptions.Model;

    if (string.IsNullOrWhiteSpace(embeddingBaseUrl)
        || string.IsNullOrWhiteSpace(embeddingApiKey)
        || string.IsNullOrWhiteSpace(embeddingModel))
    {
        Console.Error.WriteLine("Embedding configuration missing. Set EMBEDDINGS_BASE_URL, EMBEDDINGS_API_KEY, and EMBEDDINGS_MODEL.");
        return 1;
    }

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ingestion");
    var chunker = host.Services.GetRequiredService<TextChunker>();
    var embeddings = host.Services.GetRequiredService<IEmbeddingGenerator>();
    var repository = host.Services.GetRequiredService<IRagRepository>();
    var ragOptions = host.Services.GetRequiredService<IOptions<RagOptions>>().Value;

    if (string.IsNullOrWhiteSpace(ragOptions.TableName))
    {
        logger.LogError("RAG table configuration missing. Set Rag:TableName or RAG_TABLE.");
        return 1;
    }

    var files = Directory.GetFiles(pdfFolderPath, "*.pdf", SearchOption.TopDirectoryOnly);
    if (files.Length == 0)
    {
        logger.LogWarning("No PDF files found in {FolderPath}", pdfFolderPath);
        return 0;
    }

    foreach (var filePath in files)
    {
        cts.Token.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(filePath);
        logger.LogInformation("Processing {FileName}", fileName);

        var documentId = Path.GetFileNameWithoutExtension(filePath);
        var contentHash = ComputeSha256(filePath);
        var chunks = new List<RagChunk>();

        var pageTexts = ExtractPageText(filePath);
        var chunkIndexOffset = 0;

        foreach (var page in pageTexts)
        {
            var textChunks = chunker.Chunk(page.Text, page.PageNumber);
            foreach (var chunk in textChunks)
            {
                var chunkId = $"{documentId}-{chunkIndexOffset + chunk.ChunkIndex:D6}";
                var embedding = await embeddings.GenerateEmbeddingAsync(chunk.Text, cts.Token).ConfigureAwait(false);

                if (embedding.Length == 0)
                {
                    logger.LogWarning("Skipping chunk with empty embedding for {FileName} page {PageNumber} chunk {ChunkIndex}", fileName, page.PageNumber, chunk.ChunkIndex);
                    continue;
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
            logger.LogWarning("No chunks were generated for {FileName}", fileName);
            continue;
        }

        await repository.UpsertChunksAsync(chunks, cts.Token).ConfigureAwait(false);
        logger.LogInformation("Persisted {Count} chunks for {FileName}", chunks.Count, fileName);
    }

    return 0;
}

static IReadOnlyList<(int PageNumber, string Text)> ExtractPageText(string filePath)
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

static string ComputeSha256(string filePath)
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
