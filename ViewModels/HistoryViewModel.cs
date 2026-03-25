using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.System;
using WolffilesUploader.Models;
using WolffilesUploader.Services;

namespace WolffilesUploader.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly UploadHistoryService _history;

    public HistoryViewModel(UploadHistoryService history)
    {
        _history = history;
    }

    public ObservableCollection<HistoryItem> Items { get; } = [];
    [ObservableProperty] private UploadStats _stats = new();
    public bool IsEmpty => Items.Count == 0;

    public void Refresh()
    {
        Items.Clear();
        foreach (var item in _history.GetAll())
            Items.Add(item);
        Stats = _history.GetStats();
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task OpenUrlAsync(HistoryItem item)
    {
        if (!string.IsNullOrEmpty(item.Url))
            await Launcher.LaunchUriAsync(new Uri(item.Url));
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _history.ClearAsync();
        Refresh();
    }
}
