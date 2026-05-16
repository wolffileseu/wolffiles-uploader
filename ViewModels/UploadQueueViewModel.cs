using System.Collections.Concurrent;
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

    // One CTS per active upload so the per-item ✕ button cancels only that item.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _itemCts = new();

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
        var finished = Queue.Where(q => q.Status is UploadStatus.Done or UploadStatus.Error or UploadStatus.Cancelled).ToList();
        foreach (var item in finished) Queue.Remove(item);
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasItems));
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAllAsync()
    {
        IsUploading = true;
        UploadAllCommand.NotifyCanExecuteChanged();

        var pending = Queue.Where(q => q.Status == UploadStatus.Pending).ToList();
        StatusMessage = $"Uploading {pending.Count} Datei(en)...";

        foreach (var item in pending)
        {
            await UploadOneAsync(item);
        }

        IsUploading = false;
        StatusMessage = "";
        UploadAllCommand.NotifyCanExecuteChanged();
    }

    private async Task UploadOneAsync(UploadItem item)
    {
        item.Status = UploadStatus.Uploading;
        item.Progress = 0;
        item.ErrorMessage = "";
        item.StatusMessage = "";

        var cts = new CancellationTokenSource();
        _itemCts[item.Id] = cts;

        var startTime = DateTime.Now;
        var progress = new Progress<MultipartUploadProgress>(p =>
        {
            item.Progress = p.Percent;
            item.StatusMessage = p.Phase switch
            {
                MultipartUploadPhase.Hashing    => Localization.GetString("Upload_Status_Hashing"),
                MultipartUploadPhase.Uploading  => Localization.GetString("Upload_Status_Uploading"),
                MultipartUploadPhase.Processing => Localization.GetString("Upload_Status_Processing"),
                MultipartUploadPhase.Finalizing => Localization.GetString("Upload_Status_Finalizing"),
                MultipartUploadPhase.Done       => Localization.GetString("Upload_Status_Done"),
                _                                => item.StatusMessage
            };

            // Speed is only meaningful while bytes are flowing to S3.
            if (p.Phase == MultipartUploadPhase.Uploading)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed > 0.5)
                {
                    var bytesPerSec = p.BytesDone / elapsed;
                    item.SpeedDisplay = bytesPerSec > 1_048_576
                        ? $"{bytesPerSec / 1_048_576:F1} MB/s"
                        : $"{bytesPerSec / 1024:F0} KB/s";
                }
            }
            else
            {
                item.SpeedDisplay = "";
            }
        });

        try
        {
            var result = await _api.UploadFileAsync(item, progress, cts.Token);

            if (result.Success)
            {
                item.Status = UploadStatus.Done;
                item.Progress = 100;
                item.StatusMessage = Localization.GetString("Upload_Status_Done");
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
            else if (result.IsDuplicate)
            {
                item.Status = UploadStatus.Error;
                item.ErrorMessage = string.Format(
                    Localization.GetString("Upload_Error_Duplicate"),
                    result.DuplicateTitle ?? "?");
                item.StatusMessage = "";

                await _history.AddAsync(new HistoryItem
                {
                    FileName = item.FileName,
                    Title = item.Title,
                    FileSizeBytes = item.FileSizeBytes,
                    Success = false,
                    UploadedAt = DateTime.Now
                });
            }
            else
            {
                item.Status = UploadStatus.Error;
                item.ErrorMessage = result.Error ?? "Unbekannter Fehler";
                item.StatusMessage = "";

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
            // User pressed cancel — MultipartUploadService already aborted the S3 session.
            item.Status = UploadStatus.Cancelled;
            item.Progress = 0;
            item.SpeedDisplay = "";
            item.StatusMessage = Localization.GetString("Upload_Status_Cancelled");
        }
        catch (Exception ex)
        {
            item.Status = UploadStatus.Error;
            item.ErrorMessage = ex.Message;
            item.StatusMessage = "";
        }
        finally
        {
            _itemCts.TryRemove(item.Id, out _);
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void CancelItem(UploadItem item)
    {
        if (_itemCts.TryGetValue(item.Id, out var cts))
            cts.Cancel();
    }

    [RelayCommand]
    private void CancelUpload()
    {
        foreach (var cts in _itemCts.Values)
            cts.Cancel();
    }

    private bool CanUpload() => !IsUploading && Queue.Any(q => q.Status == UploadStatus.Pending);

    partial void OnIsUploadingChanged(bool value) => UploadAllCommand.NotifyCanExecuteChanged();
}
