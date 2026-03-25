using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolffilesUploader.Models;

namespace WolffilesUploader.Services;

public class WolffilesApiService
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://wolffiles.eu/api/v1";
    private static string? _token; // static = immer verfügbar

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WolffilesApiService(HttpClient http)
    {
        _http = http;
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
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Debug: log token
        var authHeader = _http.DefaultRequestHeaders.Authorization;
        System.Diagnostics.Debug.WriteLine($"[Upload] Auth header: {authHeader?.Scheme} {authHeader?.Parameter?[..Math.Min(20, authHeader?.Parameter?.Length ?? 0)]}...");
        System.Diagnostics.Debug.WriteLine($"[Upload] IsAuthenticated: {IsAuthenticated}");
        using var form = new MultipartFormDataContent();

        // File
        var fileBytes = await File.ReadAllBytesAsync(item.FilePath, cancellationToken);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", item.FileName);

        // Metadata
        form.Add(new StringContent(item.Title), "title");
        form.Add(new StringContent(item.Description ?? ""), "description");
        form.Add(new StringContent(item.CategoryId.ToString()), "category_id");
        if (!string.IsNullOrEmpty(item.Version))
            form.Add(new StringContent(item.Version), "version");
        if (!string.IsNullOrEmpty(item.Author))
            form.Add(new StringContent(item.Author), "author");

        // Screenshot
        if (!string.IsNullOrEmpty(item.ScreenshotPath) && File.Exists(item.ScreenshotPath))
        {
            var imgBytes = await File.ReadAllBytesAsync(item.ScreenshotPath, cancellationToken);
            var imgContent = new ByteArrayContent(imgBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imgContent, "screenshot", Path.GetFileName(item.ScreenshotPath));
        }

        // Progress simulation (no real progress without wrapper, but shows activity)
        progress?.Report(10);
        var resp = await _http.PostAsync($"{BaseUrl}/files", form, cancellationToken);
        progress?.Report(100);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var err = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts);
            return new UploadResult { Success = false, Error = err?.Message ?? $"HTTP {(int)resp.StatusCode}" };
        }

        var result = JsonSerializer.Deserialize<DataWrapper<UploadFileResponse>>(json, JsonOpts);
        return new UploadResult
        {
            Success = true,
            FileId = result?.Data?.Id ?? 0,
            Url = result?.Data?.Url ?? ""
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
}

file class LoginResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("user")] public UserInfo? User { get; set; }
}

file class UploadFileResponse
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
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

// ── Progress helper ──────────────────────────────────────────────────────────

internal class ProgressableContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly IProgress<double>? _progress;
    private readonly long _totalBytes;

    public ProgressableContent(HttpContent inner, IProgress<double>? progress, long totalBytes)
    {
        _inner = inner;
        _progress = progress;
        _totalBytes = totalBytes;
        foreach (var h in inner.Headers)
            Headers.TryAddWithoutValidation(h.Key, h.Value);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var buffer = new byte[81920];
        long written = 0;
        using var src = await _inner.ReadAsStreamAsync();
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read));
            written += read;
            _progress?.Report(_totalBytes > 0 ? (double)written / _totalBytes * 100 : 0);
        }
    }

    // Return false so HttpClient uses chunked transfer instead of Content-Length
    protected override bool TryComputeLength(out long length) { length = -1; return false; }
}
