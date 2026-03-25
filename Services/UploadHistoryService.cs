using System.Text.Json;
using WolffilesUploader.Models;

namespace WolffilesUploader.Services;

/// <summary>
/// Stores upload history locally as JSON (works unpackaged).
/// </summary>
public class UploadHistoryService
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WolffilesUploader", "upload_history.json");

    private List<HistoryItem> _cache = [];

    public UploadHistoryService() => Load();

    private void Load()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                _cache = JsonSerializer.Deserialize<List<HistoryItem>>(json) ?? [];
            }
        }
        catch { _cache = []; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(_cache,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public async Task AddAsync(HistoryItem item)
    {
        _cache.Insert(0, item);
        if (_cache.Count > 500) _cache = _cache.Take(500).ToList();
        await Task.Run(Save);
    }

    public IReadOnlyList<HistoryItem> GetAll() => _cache.AsReadOnly();

    public UploadStats GetStats() => new()
    {
        TotalUploads = _cache.Count,
        TotalBytesUploaded = _cache.Where(h => h.Success).Sum(h => h.FileSizeBytes),
        SuccessCount = _cache.Count(h => h.Success)
    };

    public async Task ClearAsync()
    {
        _cache.Clear();
        await Task.Run(Save);
    }
}
