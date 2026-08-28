using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;

namespace InstaRAG.ImportProducts;

/// <summary>
/// Admin CLI tool: Import product data from CSV/JSON → GCS → Vertex AI RAG Corpus.
///
/// Usage:
///   dotnet run -- --input ../../sample_data/products.csv [--create-corpus] [--chunk-size 512] [--chunk-overlap 100]
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("  InstaRAG Product Importer v1.0.0");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine();

        // ─── Parse Arguments ────────────────────────────────────────────
        var inputPath = GetArgValue(args, "--input");
        var createCorpus = args.Contains("--create-corpus");
        var chunkSize = int.TryParse(GetArgValue(args, "--chunk-size"), out var cs) ? cs : 512;
        var chunkOverlap = int.TryParse(GetArgValue(args, "--chunk-overlap"), out var co) ? co : 100;

        if (string.IsNullOrEmpty(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: --input <path> is required.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- --input <csv-or-json-path> [--create-corpus] [--chunk-size 512] [--chunk-overlap 100]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --input ../../sample_data/products.csv --create-corpus");
            Console.WriteLine("  dotnet run -- --input products.json");
            return 1;
        }

        // ─── Load Configuration ─────────────────────────────────────────
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("../../src/InstaRAG.Api/appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var projectId = config["GoogleCloud:ProjectId"] ?? Environment.GetEnvironmentVariable("GCP_PROJECT_ID");
        var location = config["GoogleCloud:Location"] ?? "asia-south1";
        var bucketName = config["GoogleCloud:GcsBucketName"] ?? Environment.GetEnvironmentVariable("GCS_BUCKET_NAME");
        var corpusDisplayName = config["GoogleCloud:RagCorpusDisplayName"] ?? "my-product-catalog";
        var serviceAccountJson = config["GoogleCloud:ServiceAccountJson"] ?? Environment.GetEnvironmentVariable("SERVICE_ACCOUNT_JSON");

        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(bucketName) || string.IsNullOrEmpty(serviceAccountJson))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: GoogleCloud:ProjectId, GcsBucketName, and ServiceAccountJson must be configured.");
            Console.ResetColor();
            Console.WriteLine("Set them in appsettings.json or via environment variables.");
            return 1;
        }

        // ─── Step 1: Read Product Data ──────────────────────────────────
        Console.WriteLine($"📂 Reading products from: {inputPath}");
        var products = ReadProducts(inputPath);
        Console.WriteLine($"   Found {products.Count} products.\n");

        // ─── Step 2: Convert to Documents ───────────────────────────────
        Console.WriteLine("📝 Converting products to text documents...");
        var documents = new Dictionary<string, string>();
        foreach (var product in products)
        {
            var fileName = $"{SanitizeFileName(product.ProductName)}.txt";
            documents[fileName] = product.ToDocumentText();
        }
        Console.WriteLine($"   Created {documents.Count} documents.\n");

        // ─── Step 3: Upload to GCS ──────────────────────────────────────
        Console.WriteLine($"☁️  Uploading to GCS bucket: gs://{bucketName}/products/");
        var gcsUris = await UploadToGcsAsync(bucketName, documents, serviceAccountJson, projectId);
        Console.WriteLine($"   Uploaded {gcsUris.Count} files.\n");

        // ─── Step 4: Create RAG Corpus (optional) ───────────────────────
        string? corpusResourceName = config["GoogleCloud:RagCorpusResourceName"];

        if (createCorpus)
        {
            Console.WriteLine($"🏗️  Creating RAG Corpus: \"{corpusDisplayName}\"...");
            corpusResourceName = await CreateRagCorpusAsync(projectId, location, corpusDisplayName, serviceAccountJson);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ✅ Corpus created: {corpusResourceName}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("   ⚠️  Save this resource name in your appsettings.json → GoogleCloud:RagCorpusResourceName");
            Console.WriteLine();
        }

        if (string.IsNullOrEmpty(corpusResourceName))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  No RagCorpusResourceName configured. Use --create-corpus to create one,");
            Console.WriteLine("   or set GoogleCloud:RagCorpusResourceName in appsettings.json.");
            Console.ResetColor();
            return 1;
        }

        // ─── Step 5: Import Files into RAG Corpus ───────────────────────
        Console.WriteLine($"📥 Importing files into RAG corpus...");
        Console.WriteLine($"   Chunk size: {chunkSize}, Overlap: {chunkOverlap}");
        await ImportFilesToCorpusAsync(projectId, location, corpusResourceName, gcsUris, chunkSize, chunkOverlap, serviceAccountJson);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   ✅ Import triggered successfully!\n");
        Console.ResetColor();

        // ─── Summary ────────────────────────────────────────────────────
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  IMPORT SUMMARY");
        Console.ResetColor();
        Console.WriteLine($"  Products imported:    {products.Count}");
        Console.WriteLine($"  Documents uploaded:   {documents.Count}");
        Console.WriteLine($"  GCS Bucket:           gs://{bucketName}/products/");
        Console.WriteLine($"  RAG Corpus:           {corpusResourceName}");
        Console.WriteLine($"  Chunk size / Overlap: {chunkSize} / {chunkOverlap}");
        Console.WriteLine("═══════════════════════════════════════════════════");

        return 0;
    }

    // ─── Product Reading ────────────────────────────────────────────────

    static List<ProductRecord> ReadProducts(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ReadCsv(path),
            ".json" => ReadJson(path),
            _ => throw new NotSupportedException($"Unsupported file format: {extension}. Use .csv or .json")
        };
    }

    static List<ProductRecord> ReadCsv(string path)
    {
        using var reader = new StreamReader(path);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => args.Header.Replace("_", "").ToLowerInvariant()
        };
        using var csv = new CsvReader(reader, csvConfig);
        return csv.GetRecords<ProductRecord>().ToList();
    }

    static List<ProductRecord> ReadJson(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ProductRecord>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<ProductRecord>();
    }

    // ─── GCS Upload ─────────────────────────────────────────────────────

    static async Task<List<string>> UploadToGcsAsync(string bucketName, Dictionary<string, string> documents, string serviceAccountJson, string projectId)
    {
        var credential = GoogleCredential.FromJson(serviceAccountJson.Replace("\\n", "\n"));
        var storageClient = await StorageClient.CreateAsync(credential);
        
        try {
            await storageClient.GetBucketAsync(bucketName);
        } catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) {
            Console.WriteLine($"   Bucket {bucketName} not found. Creating it...");
            await storageClient.CreateBucketAsync(projectId, bucketName);
        }

        var uris = new List<string>();

        foreach (var (fileName, content) in documents)
        {
            var objectName = $"products/{fileName}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await storageClient.UploadObjectAsync(bucketName, objectName, "text/plain", stream);
            var uri = $"gs://{bucketName}/{objectName}";
            uris.Add(uri);
            Console.WriteLine($"     ↳ {uri}");
        }

        return uris;
    }

    // ─── Vertex AI RAG Corpus ───────────────────────────────────────────

    static async Task<string> CreateRagCorpusAsync(string projectId, string location, string displayName, string serviceAccountJson)
    {
        var credential = GoogleCredential.FromJson(serviceAccountJson.Replace("\\n", "\n"))
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{projectId}/locations/{location}/ragCorpora";

        var body = new
        {
            display_name = displayName,
            rag_vector_db_config = new
            {
                rag_managed_db = new { }
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to create corpus. Status: {response.StatusCode}, Body: {responseBody}");
        }

        // The response is a Long Running Operation — extract the corpus name from metadata
        var operation = JsonSerializer.Deserialize<JsonElement>(responseBody);

        // Poll the operation until complete
        if (operation.TryGetProperty("name", out var opName))
        {
            Console.WriteLine($"   Operation: {opName.GetString()}");
            Console.Write("   Waiting for corpus creation");

            var opUrl = $"https://{location}-aiplatform.googleapis.com/v1/{opName.GetString()}";
            for (int i = 0; i < 60; i++) // Wait up to 5 minutes
            {
                await Task.Delay(5000);
                Console.Write(".");

                var opResponse = await httpClient.GetAsync(opUrl);
                var opBody = await opResponse.Content.ReadAsStringAsync();
                var opResult = JsonSerializer.Deserialize<JsonElement>(opBody);

                if (opResult.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    Console.WriteLine(" Done!");
                    if (opResult.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("name", out var name))
                    {
                        return name.GetString()!;
                    }
                    // Fallback: try to find corpus name in metadata
                    if (opResult.TryGetProperty("metadata", out var meta) &&
                        meta.TryGetProperty("resource", out var resource))
                    {
                        return resource.GetString()!;
                    }
                }
            }
            Console.WriteLine();
            throw new TimeoutException("Corpus creation timed out after 5 minutes.");
        }

        // If response directly has the name (unlikely for LRO)
        if (operation.TryGetProperty("name", out var directName))
        {
            return directName.GetString()!;
        }

        throw new Exception($"Could not extract corpus name from response: {responseBody}");
    }

    static async Task ImportFilesToCorpusAsync(
        string projectId, string location, string corpusResourceName,
        List<string> gcsUris, int chunkSize, int chunkOverlap, string serviceAccountJson)
    {
        var credential = GoogleCredential.FromJson(serviceAccountJson.Replace("\\n", "\n"))
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        
        var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var url = $"https://{location}-aiplatform.googleapis.com/v1/{corpusResourceName}:importRagFiles";

        var body = new
        {
            import_rag_files_config = new
            {
                gcs_source = new { uris = gcsUris },
                rag_file_transformation_config = new
                {
                    rag_file_chunking_config = new
                    {
                        chunk_size = chunkSize,
                        chunk_overlap = chunkOverlap
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to import files. Status: {response.StatusCode}, Body: {responseBody}");
        }

        Console.WriteLine($"   Import operation started: {responseBody[..Math.Min(200, responseBody.Length)]}...");
    }

    // ─── Utilities ──────────────────────────────────────────────────────

    static string? GetArgValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return (index >= 0 && index + 1 < args.Length) ? args[index + 1] : null;
    }

    static string SanitizeFileName(string name)
    {
        return string.Join("_", name.Split(Path.GetInvalidFileNameChars()))
            .Replace(" ", "_")
            .ToLowerInvariant();
    }
}

/// <summary>
/// Product record for CSV/JSON deserialization.
/// Mirrors the ProductData model but lives in the ImportProducts tool.
/// </summary>
public class ProductRecord
{
    public string ProductName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public string StockStatus { get; set; } = "In Stock";
    public string Specs { get; set; } = string.Empty;
    public string SizeChart { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ComplementaryProducts { get; set; } = string.Empty;

    public string ToDocumentText()
    {
        return $"""
            === PRODUCT INFORMATION ===
            Product Name: {ProductName}
            Category: {Category}
            Price: {Price:N2} {Currency}
            Stock Status: {StockStatus}

            Description:
            {Description}

            Specifications:
            {Specs}

            Size Chart:
            {SizeChart}

            Complementary Products:
            {ComplementaryProducts}
            === END PRODUCT ===
            """;
    }
}
