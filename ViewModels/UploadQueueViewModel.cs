using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolffilesUploader.Models;
using WolffilesUploader.Services;

namespace WolffilesUploader.ViewModels;

public partial class UploadQueueViewModel : ObservableObject
{
    private readonly WolffilesApiService _api;
    private readonly UploadHistoryService _history;
    private CancellationTokenSource? _cts;

    public UploadQueueViewModel(WolffilesApiService api, UploadHistoryService history)
    {
        _api = api;
        _history = history;
    }

    public ObservableCollection<UploadItem> Queue { get; } = [];
    public ObservableCollection<CategoryOption> Categories { get; } = [];

    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private bool _isLoadingCategories;
    [ObservableProperty] private string _statusMessage = "";

    public int QueueCount => Queue.Count;
    public bool HasItems => Queue.Count > 0;

    public async Task InitAsync()
    {
        IsLoadingCategories = true;
        try
        {
            var cats = await _api.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
            System.Diagnostics.Debug.WriteLine($"[InitAsync] Loaded {Categories.Count} categories");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InitAsync] ERROR: {ex.Message}");
        }
        finally { IsLoadingCategories = false; }
    }

    [RelayCommand]
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            if (Queue.Any(q => q.FilePath == path)) continue; // no dupes

            var info = new FileInfo(path);
            var firstCat = Categories.FirstOrDefault(c => !c.IsParent);
            var item = new UploadItem
            {
                FilePath = path,
                FileSizeBytes = info.Length,
                Title = Path.GetFileNameWithoutExtension(path),
                CategoryId = firstCat?.Id ?? 0,
                AvailableCategories = Categories.ToList(),
                SelectedCategory = firstCat
            };
            Queue.Add(item);
        }
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasItems));
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveItem(UploadItem item)
    {
        Queue.Remove(item);
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasItems));
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        var finished = Queue.Where(q => q.Status is UploadStatus.Done or UploadStatus.Error).ToList();
        foreach (var item in finished) Queue.Remove(item);
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasItems));
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAllAsync()
    {
        IsUploading = true;
        _cts = new CancellationTokenSource();
        UploadAllCommand.NotifyCanExecuteChanged();

        var pending = Queue.Where(q => q.Status == UploadStatus.Pending).ToList();
        StatusMessage = $"Uploading {pending.Count} Datei(en)...";

        foreach (var item in pending)
        {
            if (_cts.Token.IsCancellationRequested) break;

            item.Status = UploadStatus.Uploading;
            item.Progress = 0;

            var startTime = DateTime.Now;
            var progress = new Progress<double>(pct =>
            {
                item.Progress = pct;
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed > 0.5)
                {
                    var bytesPerSec = item.FileSizeBytes * (pct / 100.0) / elapsed;
                    item.SpeedDisplay = bytesPerSec > 1_048_576
                        ? $"{bytesPerSec / 1_048_576:F1} MB/s"
                        : $"{bytesPerSec / 1024:F0} KB/s";
                }
            });

            try
            {
                var result = await _api.UploadFileAsync(item, progress, _cts.Token);

                if (result.Success)
                {
                    item.Status = UploadStatus.Done;
                    item.Progress = 100;
                    item.ResultUrl = result.Url;
                    item.UploadedAt = DateTime.Now;

                    await _history.AddAsync(new HistoryItem
                    {
                        FileName = item.FileName,
                        Title = item.Title,
                        Category = Categories.FirstOrDefault(c => c.Id == item.CategoryId)?.DisplayName ?? "",
                        FileSizeBytes = item.FileSizeBytes,
                        Success = true,
                        UploadedAt = DateTime.Now,
                        Url = result.Url
                    });
                }
                else
                {
                    item.Status = UploadStatus.Error;
                    item.ErrorMessage = result.Error ?? "Unbekannter Fehler";

                    await _history.AddAsync(new HistoryItem
                    {
                        FileName = item.FileName,
                        Title = item.Title,
                        FileSizeBytes = item.FileSizeBytes,
                        Success = false,
                        UploadedAt = DateTime.Now
                    });
                }
            }
            catch (OperationCanceledException)
            {
                item.Status = UploadStatus.Pending;
                item.Progress = 0;
                break;
            }
            catch (Exception ex)
            {
                item.Status = UploadStatus.Error;
                item.ErrorMessage = ex.Message;
            }
        }

        IsUploading = false;
        StatusMessage = "";
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CancelUpload() => _cts?.Cancel();

    private bool CanUpload() => !IsUploading && Queue.Any(q => q.Status == UploadStatus.Pending);

    partial void OnIsUploadingChanged(bool value) => UploadAllCommand.NotifyCanExecuteChanged();
}
