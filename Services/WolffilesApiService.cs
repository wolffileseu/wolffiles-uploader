using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolffilesUploader.Models;

namespace WolffilesUploader.Services;

public class WolffilesApiService
{
    private readonly HttpClient _http;
    private readonly MultipartUploadService _multipart;
    private const string BaseUrl = "https://wolffiles.eu/api/v1";
    private static string? _token; // static = immer verfügbar

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WolffilesApiService(HttpClient http, MultipartUploadService multipart)
    {
        _http = http;
        _multipart = multipart;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromSeconds(300);

        // Restore token if already set (e.g. from previous DI resolution)
        if (!string.IsNullOrEmpty(_token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        System.Diagnostics.Debug.WriteLine($"[API] SetToken: {token[..Math.Min(20, token.Length)]}...");
    }

    public void ClearToken()
    {
        _token = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);

    // Read-only static accessor so peer services (e.g. MultipartUploadService)
    // can attach the same bearer token to their own HttpClient instances.
    public static string? CurrentToken => _token;

    // ── AUTH ─────────────────────────────────────────────────────────────────

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        var payload = JsonSerializer.Serialize(new { email, password });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var resp = await _http.PostAsync($"{BaseUrl}/auth/login", content);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var err = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts);
            return new LoginResult { Success = false, Error = err?.Message ?? "Login fehlgeschlagen" };
        }

        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOpts);
        if (result?.Token != null)
        {
            SetToken(result.Token);
            return new LoginResult { Success = true, Token = result.Token, User = result.User };
        }

        return new LoginResult { Success = false, Error = "Kein Token erhalten" };
    }

    public async Task<LoginResult> RegisterAsync(string name, string email, string password, string passwordConfirmation)
    {
        var payload = JsonSerializer.Serialize(new
        {
            name,
            email,
            password,
            password_confirmation = passwordConfirmation
        });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var resp = await _http.PostAsync($"{BaseUrl}/auth/register", content);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var err = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts);
            return new LoginResult { Success = false, Error = err?.Message ?? "Registrierung fehlgeschlagen" };
        }

        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOpts);
        if (result?.Token != null)
        {
            SetToken(result.Token);
            return new LoginResult { Success = true, Token = result.Token, User = result.User };
        }

        return new LoginResult { Success = false, Error = "Kein Token erhalten" };
    }

    public async Task LogoutAsync()
    {
        if (!IsAuthenticated) return;
        await _http.PostAsync($"{BaseUrl}/auth/logout", null);
        ClearToken();
    }

    public async Task<UserInfo?> GetMeAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/auth/me");
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<DataWrapper<UserInfo>>(json, JsonOpts);
        return wrapper?.Data;
    }

    // ── CATEGORIES ───────────────────────────────────────────────────────────

    public async Task<List<CategoryOption>> GetCategoriesAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/categories");
        if (!resp.IsSuccessStatusCode) return [];
        var json = await resp.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<DataWrapper<List<Category>>>(json, JsonOpts);
        var categories = wrapper?.Data ?? [];

        // Flatten hierarchy: Parent → Children as "ET → Maps"
        var result = new List<CategoryOption>();
        foreach (var parent in categories)
        {
            if (parent.Children.Count == 0)
            {
                result.Add(new CategoryOption { Id = parent.Id, DisplayName = parent.Name });
            }
            else
            {
                // Add parent as header (not selectable, but shown)
                result.Add(new CategoryOption { Id = parent.Id, DisplayName = parent.Name, IsParent = true });
                foreach (var child in parent.Children)
                    result.Add(new CategoryOption { Id = child.Id, DisplayName = $"  {parent.Name} → {child.Name}" });
            }
        }
        return result;
    }

    // ── UPLOAD ───────────────────────────────────────────────────────────────

    public async Task<UploadResult> UploadFileAsync(
        UploadItem item,
        IProgress<MultipartUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Stream the file through the S3 multipart pipeline (bypasses Cloudflare 100 MB limit).
        var s3 = await _multipart.UploadAsync(item.FilePath, progress, cancellationToken);

        // 2. Signal Finalizing while we POST the metadata.
        progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Finalizing, s3.Size, s3.Size));

        using var form = new MultipartFormDataContent();

        // Multipart-uploaded file → reference by S3 key
        form.Add(new StringContent(s3.Key), "file_s3_key");
        form.Add(new StringContent(item.FileName), "file_filename");
        form.Add(new StringContent(s3.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)), "file_size");
        if (!string.IsNullOrEmpty(s3.Hash))
            form.Add(new StringContent(s3.Hash), "file_hash");
        if (!string.IsNullOrEmpty(s3.ContentType))
            form.Add(new StringContent(s3.ContentType), "file_content_type");

        // Metadata (same fields as the classic path)
        form.Add(new StringContent(item.Title), "title");
        form.Add(new StringContent(item.Description ?? ""), "description");
        form.Add(new StringContent(item.CategoryId.ToString()), "category_id");
        form.Add(new StringContent(item.Game ?? "auto"), "game");
        if (!string.IsNullOrEmpty(item.Version))
            form.Add(new StringContent(item.Version), "version");
        if (!string.IsNullOrEmpty(item.Author))
            form.Add(new StringContent(item.Author), "original_author");

        // Tags
        foreach (var tag in item.Tags)
            form.Add(new StringContent(tag), "tags[]");

        // Screenshots stay classic (small files, no need for multipart)
        foreach (var path in item.ScreenshotPaths)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var content = new ByteArrayContent(bytes);
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var mime = ext switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "png"           => "image/png",
                "webp"          => "image/webp",
                _               => "application/octet-stream"
            };
            content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(content, "screenshots[]", Path.GetFileName(path));
        }

        var resp = await _http.PostAsync($"{BaseUrl}/files", form, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var dup = JsonSerializer.Deserialize<DuplicateResponse>(json, JsonOpts);
            return new UploadResult
            {
                Success = false,
                IsDuplicate = true,
                DuplicateTitle = dup?.Existing?.Title,
                Error = dup?.Message ?? "Duplicate"
            };
        }

        if (!resp.IsSuccessStatusCode)
        {
            var err = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts);
            return new UploadResult { Success = false, Error = err?.Message ?? $"HTTP {(int)resp.StatusCode}" };
        }

        // Server response shape: { success, file: { id, slug, title, status } }
        var ok = JsonSerializer.Deserialize<StoreSuccessResponse>(json, JsonOpts);
        progress?.Report(new MultipartUploadProgress(MultipartUploadPhase.Done, s3.Size, s3.Size));
        return new UploadResult
        {
            Success = true,
            FileId = ok?.File?.Id ?? 0,
            Url = ok?.File?.Slug != null ? $"https://wolffiles.eu/files/{ok.File.Slug}" : ""
        };
    }

    // ── HISTORY ──────────────────────────────────────────────────────────────

    public async Task<List<HistoryItem>> GetHistoryAsync(int page = 1, int perPage = 50)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/files/my?page={page}&per_page={perPage}");
        if (!resp.IsSuccessStatusCode) return [];
        var json = await resp.Content.ReadAsStringAsync();
        var wrapper = JsonSerializer.Deserialize<PaginatedWrapper<HistoryItem>>(json, JsonOpts);
        return wrapper?.Data ?? [];
    }
}

// ── DTO classes ──────────────────────────────────────────────────────────────

public record LoginResult
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public UserInfo? User { get; init; }
    public string? Error { get; init; }
}

public record UploadResult
{
    public bool Success { get; init; }
    public int FileId { get; init; }
    public string? Url { get; init; }
    public string? Error { get; init; }
    public bool IsDuplicate { get; init; }
    public string? DuplicateTitle { get; init; }
}

file class LoginResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("user")] public UserInfo? User { get; set; }
}

file class StoreSuccessResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("file")] public StoreSuccessFile? File { get; set; }
}

file class StoreSuccessFile
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

file class DuplicateResponse
{
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("existing")] public DuplicateExisting? Existing { get; set; }
}

file class DuplicateExisting
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
}

file class ErrorResponse
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}

file class DataWrapper<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

file class PaginatedWrapper<T>
{
    [JsonPropertyName("data")] public List<T>? Data { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}
