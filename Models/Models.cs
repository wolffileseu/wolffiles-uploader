using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WolffilesUploader.Models;

public class UserInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "user";
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<Category> Children { get; set; } = [];
}

// Flat item for ComboBox display
public class CategoryOption
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsParent { get; set; }
}

public enum UploadStatus
{
    Pending,
    Uploading,
    Done,
    Error
}

public partial class UploadItem : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public long FileSizeBytes { get; set; }
    public string FileSizeDisplay => FileSizeBytes > 1_048_576
        ? $"{FileSizeBytes / 1_048_576.0:F1} MB"
        : $"{FileSizeBytes / 1024.0:F1} KB";
    public string FileExtension => Path.GetExtension(FilePath).TrimStart('.').ToUpper();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private int _categoryId;
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private UploadStatus _status = UploadStatus.Pending;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _speedDisplay = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string? _screenshotPath;

    public string? ResultUrl { get; set; }
    public DateTime? UploadedAt { get; set; }

    // Set when item is added to queue so DataTemplate can bind to it
    public List<CategoryOption> AvailableCategories { get; set; } = [];

    private CategoryOption? _selectedCategory;
    public CategoryOption? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value) && value != null)
                CategoryId = value.Id;
        }
    }
}

public class HistoryItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string FileSizeDisplay => FileSizeBytes > 1_048_576
        ? $"{FileSizeBytes / 1_048_576.0:F1} MB"
        : $"{FileSizeBytes / 1024.0:F1} KB";
    public bool Success { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? Url { get; set; }
    public string DateDisplay => UploadedAt.ToString("dd.MM.yyyy HH:mm");
}

public class UploadStats
{
    public int TotalUploads { get; set; }
    public long TotalBytesUploaded { get; set; }
    public int SuccessCount { get; set; }
    public string TotalSizeDisplay => TotalBytesUploaded > 1_073_741_824
        ? $"{TotalBytesUploaded / 1_073_741_824.0:F1} GB"
        : $"{TotalBytesUploaded / 1_048_576.0:F0} MB";
}

// Helper base class (CommunityToolkit.Mvvm style)
// REMOVED - use CommunityToolkit.Mvvm.ComponentModel.ObservableObject directly
