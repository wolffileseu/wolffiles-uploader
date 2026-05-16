using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace WolffilesUploader.Services;

public class MultipartUploadService
{
    private const long PART_SIZE = 100L * 1024 * 1024; // 100 MB
    private const int MAX_PARALLEL_PARTS = 3;
    private const int MAX_RETRIES_PER_PART = 3;
    private const string BaseUrl = "https://wolffiles.eu";

    private readonly IHttpClientFactory _httpFactory;

    public MultipartUploadService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public record CompletedPart(
        [property: JsonPropertyName("PartNumber")] int PartNumber,
        [property: JsonPropertyName("ETag")] string ETag);

    public record UploadResult(string Key, string Hash, long Size, string ContentType);

    private record InitResponse(
        [property: JsonPropertyName("uploadId")] string UploadId,
        [property: JsonPropertyName("key")] string Key);

    private record SignResponse(
        [property: JsonPropertyName("url")] string Url);

    public async Task<UploadResult> UploadAsync(
        string filePath,
        IProgress<MultipartUploadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        var filename = fileInfo.Name;
        var size = fileInfo.Length;
        var contentType = GuessMimeType(filename);

        // 1. SHA-256 hash
        progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Hashing, 0, size));
        var hash = await ComputeSha256Async(filePath, ct);

        // 2. Init
        progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Uploading, 0, size));
        using var api = CreateAuthedClient();
        var initBody = new { filename, size, content_type = contentType, target = "files", file_hash = hash };
        var initResponse = await PostJsonAsync<InitResponse>(api, "/api/v1/upload-api/init", initBody, ct);

        var totalParts = (int)Math.Ceiling((double)size / PART_SIZE);
        var completedParts = new ConcurrentBag<CompletedPart>();
        var uploadedBytes = 0L;

        try
        {
            using var semaphore = new SemaphoreSlim(MAX_PARALLEL_PARTS);
            var tasks = new List<Task>();

            for (int partNumber = 1; partNumber <= totalParts; partNumber++)
            {
                var pn = partNumber;
                await semaphore.WaitAsync(ct);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var offset = (long)(pn - 1) * PART_SIZE;
                        var partSize = Math.Min(PART_SIZE, size - offset);

                        var etag = await UploadPartWithRetryAsync(api, initResponse, pn, filePath, offset, partSize, ct);
                        completedParts.Add(new CompletedPart(pn, etag));

                        var newTotal = Interlocked.Add(ref uploadedBytes, partSize);
                        progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Uploading, newTotal, size));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            // 3. Complete
            progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Processing, size, size));
            var parts = completedParts.OrderBy(p => p.PartNumber).ToArray();
            var completeBody = new { uploadId = initResponse.UploadId, key = initResponse.Key, parts };
            await PostJsonAsync<object>(api, "/api/v1/upload-api/complete", completeBody, ct);

            return new UploadResult(initResponse.Key, hash, size, contentType);
        }
        catch
        {
            // Abort on any failure — never propagate ct in cleanup
            try
            {
                var abortBody = new { uploadId = initResponse.UploadId, key = initResponse.Key };
                await PostJsonAsync<object>(api, "/api/v1/upload-api/abort", abortBody, CancellationToken.None);
            }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }

    private async Task<string> UploadPartWithRetryAsync(
        HttpClient api,
        InitResponse init,
        int partNumber,
        string filePath,
        long offset,
        long partSize,
        CancellationToken ct)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= MAX_RETRIES_PER_PART; attempt++)
        {
            try
            {
                // Sign
                var signBody = new { uploadId = init.UploadId, key = init.Key, partNumber };
                var signResponse = await PostJsonAsync<SignResponse>(api, "/api/v1/upload-api/sign", signBody, ct);

                // Read the chunk into memory (100 MB is fine for modern systems)
                var buffer = new byte[partSize];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    var read = 0;
                    while (read < partSize)
                    {
                        var n = await fs.ReadAsync(buffer.AsMemory(read, (int)(partSize - read)), ct);
                        if (n == 0) throw new IOException("Unexpected end of file");
                        read += n;
                    }
                }

                // PUT directly to S3 — separate HttpClient WITHOUT the bearer token
                using var s3Client = _httpFactory.CreateClient();
                s3Client.Timeout = TimeSpan.FromMinutes(10);

                using var content = new ByteArrayContent(buffer);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var putResponse = await s3Client.PutAsync(signResponse.Url, content, ct);
                putResponse.EnsureSuccessStatusCode();

                var etag = putResponse.Headers.ETag?.Tag ?? throw new InvalidOperationException("No ETag in S3 response");
                return etag.Trim('"');
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < MAX_RETRIES_PER_PART)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }

        throw new Exception($"Part {partNumber} failed after {MAX_RETRIES_PER_PART} attempts", lastError);
    }

    private HttpClient CreateAuthedClient()
    {
        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = WolffilesApiService.CurrentToken;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string url, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(url, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"{(int)response.StatusCode} from {url}: {error}");
        }
        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new InvalidOperationException($"Empty response from {url}");
        return result;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GuessMimeType(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".pk3" or ".zip" => "application/zip",
        ".rar" => "application/x-rar-compressed",
        ".7z" => "application/x-7z-compressed",
        ".dm_84" or ".dm_85" or ".dm_86" or ".dm_87" or ".dm_88" => "application/octet-stream",
        ".cfg" or ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}

public enum MultipartUploadPhase
{
    Hashing,
    Uploading,
    Processing,
    Finalizing,
    Done
}

public record MultipartUploadProgress(MultipartUploadPhase Phase, long BytesDone, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? (double)BytesDone / TotalBytes * 100.0 : 0.0;
}
