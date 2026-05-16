# Wolffiles Uploader — Multipart Upload Integration (>100 MB Cloudflare bypass)

## Background

The Cloudflare Free Plan blocks any HTTP request body larger than 100 MB. Uploading files larger than 100 MB via `/api/v1/files` (classic multipart/form-data) fails with the error `'<' is an invalid start of a value` because Cloudflare returns an HTML challenge page instead of letting the request through to Laravel.

The server already has a working multipart upload pipeline (used by the web frontend) that streams chunks directly from the client to Hetzner Object Storage (S3-compatible at `https://fsn1.your-objectstorage.com`), bypassing Cloudflare entirely. We need to wire the desktop app into this pipeline.

## Server endpoints (already live)

All four endpoints are Sanctum-authenticated (Bearer token) and live under `/api/v1/upload-api/*`:

### `POST /api/v1/upload-api/init`
Request:
```json
{
  "filename": "WolfSE_patch-1007.zip",
  "size": 110100480,
  "content_type": "application/zip",
  "target": "files",
  "file_hash": "<sha256-hex-or-null>"
}
```
Response:
```json
{
  "uploadId": "2~UUJ_hccWGg8VZu6q_CZVpcHcsj-15Iz",
  "key": "files/2026/05/dd4715ca-069f-429c-884e-78545bffd5b1.pk3"
}
```

### `POST /api/v1/upload-api/sign`
Request:
```json
{
  "uploadId": "2~UUJ_...",
  "key": "files/2026/05/...",
  "partNumber": 1
}
```
Response:
```json
{
  "url": "https://fsn1.your-objectstorage.com/wolffiles/files/2026/05/...?uploadId=2~UUJ_...&partNumber=1&X-Amz-Algorithm=..."
}
```

### `POST /api/v1/upload-api/complete`
Request:
```json
{
  "uploadId": "2~UUJ_...",
  "key": "files/2026/05/...",
  "parts": [
    { "PartNumber": 1, "ETag": "abc123..." },
    { "PartNumber": 2, "ETag": "def456..." }
  ]
}
```
Response:
```json
{
  "success": true,
  "key": "files/2026/05/...",
  "url": "https://...",
  "target": "files",
  "filename": "WolfSE_patch-1007.zip",
  "size": 110100480,
  "file_hash": "abc123...",
  "content_type": "application/zip"
}
```

### `POST /api/v1/upload-api/abort`
Request:
```json
{
  "uploadId": "2~UUJ_...",
  "key": "files/2026/05/..."
}
```
Response:
```json
{ "success": true }
```

## Final POST to `/api/v1/files`

After `complete` succeeds, the metadata POST to `/api/v1/files` uses **`file_s3_key`** instead of the `file` field. **A server-side patch is needed first** to teach `FileUploadApiController::store()` the multipart branch — see Task 1 below.

Fields when using the multipart path:
- `file_s3_key` (required, string, max:500) — the S3 key returned by complete
- `file_filename` (required, string, max:255) — the original filename
- `file_size` (required, integer)
- `file_hash` (optional, string, exactly 64 chars sha256 hex) — server does duplicate check if provided
- `file_content_type` (optional, string, max:200)
- All existing metadata: `title`, `description`, `category_id`, `game`, `version`, `original_author`, `tags[]`, `screenshots[]`

The web frontend's `FileController::store()` already has this pattern (lines 191-236 of `app/Http/Controllers/Frontend/FileController.php`) and calls `FileUploadService::uploadFromS3(s3Key, originalFilename, fileSize, fileHash, contentType, data, userId, screenshots)`. We mirror that into the API controller.

---

## Tasks

### Task 1 — Server: patch `app/Http/Controllers/Api/FileUploadApiController.php`

Add a multipart branch in `store()` that mirrors the web `FileController::store()` logic. Pattern:

```php
public function store(Request $request)
{
    $isMultipart = $request->filled('file_s3_key');

    $rules = [
        'title' => 'required|string|max:255',
        'description' => 'nullable|string|max:10000',
        'category_id' => 'required|exists:categories,id',
        'game' => 'nullable|string|max:50',
        'version' => 'nullable|string|max:50',
        'original_author' => 'nullable|string|max:255',
        'screenshots.*' => 'nullable|image|max:10240',
        'tags' => 'nullable|array|max:20',
        'tags.*' => 'string|max:50',
    ];

    if ($isMultipart) {
        $rules['file_s3_key'] = 'required|string|max:500';
        $rules['file_filename'] = 'required|string|max:255';
        $rules['file_size'] = 'required|integer|min:1';
        $rules['file_hash'] = 'nullable|string|size:64';
        $rules['file_content_type'] = 'nullable|string|max:200';
    } else {
        $rules['file'] = 'required|file|max:' . (config('app.max_upload_size', 500) * 1024);
    }

    $request->validate($rules);

    if ($isMultipart) {
        $hash = $request->input('file_hash') ?: '';

        // Duplicate check (skipped if hash empty, e.g. for very large files)
        if ($hash) {
            $duplicate = \App\Services\FileValidationService::findDuplicate($hash);
            if ($duplicate) {
                return response()->json([
                    'error' => 'duplicate',
                    'message' => 'A file with the same hash already exists',
                    'existing' => [
                        'id' => $duplicate->id,
                        'title' => $duplicate->title,
                        'slug' => $duplicate->slug,
                    ],
                ], 409);
            }
        }

        $file = app(\App\Services\FileUploadService::class)->uploadFromS3(
            s3Key: $request->input('file_s3_key'),
            originalFilename: $request->input('file_filename'),
            fileSize: (int) $request->input('file_size'),
            fileHash: $hash,
            contentType: $request->input('file_content_type'),
            data: $request->only(['title', 'description', 'category_id', 'game', 'version', 'original_author']),
            userId: $request->user()->id,
            screenshots: $request->file('screenshots', [])
        );

        // Apply tags (same logic as existing classic path)
        if ($request->has('tags')) {
            $this->applyTags($file, $request->input('tags'));
        }

        // Auto-approve + dispatch jobs (same as classic path)
        \App\Services\AutoApproveService::processUpload($file);
        \App\Jobs\AnalyzeUploadedFile::dispatch($file);
        \App\Jobs\ScanFileForViruses::dispatch($file);

        return response()->json([
            'success' => true,
            'file' => [
                'id' => $file->id,
                'slug' => $file->slug,
                'title' => $file->title,
                'status' => $file->status,
            ],
        ], 201);
    }

    // Classic upload path — keep existing logic intact
    // ...
}
```

**Important:** read the existing `store()` method first and adapt the multipart branch to use the same tag-handling helper method and the same job-dispatch pattern. Don't duplicate logic; if there's a private `applyTags()` or `dispatchPipeline()` method already, call that.

After the patch:
- `chown wolffiles.eu_lkiogmaiktl:psacln` the file
- `php -l` for syntax check
- `artisan optimize:clear`

### Task 2 — C# side: new `Services/MultipartUploadService.cs`

Create a new service that handles the full multipart flow. Use `HttpClient` injected from DI (same `IHttpClientFactory` setup as `WolffilesApiService`). The bearer token is on the named client `"wolffiles-api"`.

```csharp
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
        var api = _httpFactory.CreateClient("wolffiles-api");
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
            // Abort on any failure — never await ct in cleanup
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
                // For lower memory: use FileStream with Seek + LimitStream wrapper instead
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

                // PUT directly to S3 — use a separate HttpClient WITHOUT the bearer token
                using var s3Client = _httpFactory.CreateClient(); // un-named = no auth header
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
    Processing
}

public record MultipartUploadProgress(MultipartUploadPhase Phase, long BytesDone, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? (double)BytesDone / TotalBytes * 100.0 : 0.0;
}
```

Register the service in DI (the `App.xaml.cs` or wherever services are wired up):

```csharp
services.AddSingleton<MultipartUploadService>();
```

### Task 3 — Update `WolffilesApiService.UploadFileAsync` to always use multipart

The current implementation sends the file as a `file` field in multipart/form-data. **Always** use the new multipart pipeline (matching the web frontend's approach where multipart is universal, not just for large files).

Replace the existing implementation with:

1. Call `MultipartUploadService.UploadAsync(filePath, progress, ct)` — get back `UploadResult`
2. Build a `MultipartFormDataContent` with:
   - `file_s3_key` = `result.Key`
   - `file_filename` = `Path.GetFileName(filePath)`
   - `file_size` = `result.Size.ToString()`
   - `file_hash` = `result.Hash`
   - `file_content_type` = `result.ContentType`
   - All existing metadata fields (`title`, `description`, `category_id`, `game`, `version`, `original_author`, `tags[]`, `screenshots[]`)
3. POST to `/api/v1/files` — same endpoint as before
4. Handle 409 (duplicate) by parsing the response and showing a clear error to the user

The screenshots **stay classic** (small files, no need for multipart): just attach them as `screenshots[]` to the same form-data POST.

Inject `MultipartUploadService` into `WolffilesApiService` via constructor.

### Task 4 — Progress through `UploadItem`

`UploadItem` already has a `Progress` property (double, 0-100). Add a new `StatusMessage` string property if not present (for showing "Hashing...", "Uploading...", "Processing...").

In the upload loop:

```csharp
var progress = new Progress<MultipartUploadProgress>(p =>
{
    uploadItem.Progress = p.Percent;
    uploadItem.StatusMessage = p.Phase switch
    {
        MultipartUploadPhase.Hashing => Localization.GetString("Upload_Status_Hashing"),
        MultipartUploadPhase.Uploading => Localization.GetString("Upload_Status_Uploading"),
        MultipartUploadPhase.Processing => Localization.GetString("Upload_Status_Processing"),
        _ => ""
    };
});

var result = await _multipart.UploadAsync(filePath, progress, ct);
// Then post metadata
uploadItem.StatusMessage = Localization.GetString("Upload_Status_Finalizing");
await PostMetadataAsync(result, uploadItem, ct);
uploadItem.Progress = 100;
uploadItem.StatusMessage = Localization.GetString("Upload_Status_Done");
```

### Task 5 — Add localization keys to `Services/Localization.cs`

Add four new keys per language (DE master, EN/FR translated):

| Key | DE | EN | FR |
|---|---|---|---|
| `Upload_Status_Hashing` | `Datei wird analysiert...` | `Computing hash...` | `Calcul de l'empreinte...` |
| `Upload_Status_Uploading` | `Wird hochgeladen...` | `Uploading...` | `Téléversement...` |
| `Upload_Status_Processing` | `Wird verarbeitet...` | `Processing...` | `Traitement...` |
| `Upload_Status_Finalizing` | `Wird abgeschlossen...` | `Finalizing...` | `Finalisation...` |
| `Upload_Status_Done` | `Fertig` | `Done` | `Terminé` |
| `Upload_Error_Duplicate` | `Diese Datei existiert bereits als '{0}'` | `This file already exists as '{0}'` | `Ce fichier existe déjà en tant que '{0}'` |

For `Upload_Error_Duplicate`, the `{0}` placeholder will be filled at runtime with the existing file's title from the 409 response.

### Task 6 — UI display of `StatusMessage` in `UploadQueuePage.xaml`

Inside each `UploadItem` card, find the existing progress display and add a `TextBlock` bound to `StatusMessage` underneath or next to the progress bar. Keep the existing percent display.

### Task 7 — Cancellation and abort

The upload loop should support cancellation:

- The `UploadQueueViewModel` should have a `CancellationTokenSource` per upload
- When the user clicks the per-item cancel button (✕), trigger `Cancel()`
- `MultipartUploadService.UploadAsync` already handles `OperationCanceledException` and calls `abort` in its catch — make sure to **not** swallow the `OperationCanceledException` in the upper layer; just mark the item as "Cancelled" in the UI

### Task 8 — Build and verify

```powershell
dotnet publish WolffilesUploader.csproj `
  -c Release -r win-x64 -p:Platform=x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:WindowsPackageType=None `
  -o publish\portable
```

Test plan:

1. Upload a small file (5-50 MB PK3) — should work, going through multipart. Server log shows `Multipart upload initiated` + `Multipart upload completed`.
2. Upload the `WolfSE_patch-1007.zip` (101 MB) — the file that previously failed with the Cloudflare error — should now succeed.
3. Upload a 500 MB file — succeeds with realistic progress bar (each 100 MB chunk takes ~30-60 seconds depending on bandwidth).
4. Upload while watching `tail -f storage/logs/laravel.log` on the server: should see `[FileUploadApi] incoming upload` + `Multipart upload initiated/completed`.
5. Cancel an upload mid-way — server log should show `Multipart upload aborted`, S3 should have no orphan multipart.
6. Re-upload the exact same file — should get a 409 with duplicate detection (because `file_hash` is now always computed and sent).

Pause for my test.

## Heads-up for testing

- The 109 MB file `WolfSE_patch-1007.zip` is the canonical "Cloudflare-blocked" test case. After this work, it should upload cleanly.
- Server-side, the `MultipartUploadController` enforces a 5h TTL on the upload session and validates user ownership on every sign/complete/abort. If a test upload spans more than 5h (unlikely but possible on slow connections), the session expires and the upload fails.
- The classic `/api/v1/files` POST path is preserved for backward compatibility with v1.1.0 app users. Don't remove the `else` branch in `FileUploadApiController::store()`.
