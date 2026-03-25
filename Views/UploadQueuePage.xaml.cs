using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WolffilesUploader.Models;
using WolffilesUploader.ViewModels;
using WinRT.Interop;

namespace WolffilesUploader.Views;

public sealed partial class UploadQueuePage : Page
{
    public UploadQueueViewModel ViewModel { get; }

    public UploadQueuePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<UploadQueueViewModel>();
        Loaded += async (s, e) => await ViewModel.InitAsync();
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<Windows.Storage.StorageFile>().Select(f => f.Path);
            ViewModel.AddFilesCommand.Execute(paths);
        }
    }

    private async void BrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        // Kategorien laden falls noch nicht passiert
        if (ViewModel.Categories.Count == 0)
            await ViewModel.InitAsync();

        // DEBUG
        var dlg = new ContentDialog
        {
            Title = "Debug Categories",
            Content = $"Categories geladen: {ViewModel.Categories.Count}\nErste: {ViewModel.Categories.FirstOrDefault()?.DisplayName ?? "keine"}",
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add(".pk3");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".rar");
        picker.FileTypeFilter.Add(".7z");
        picker.FileTypeFilter.Add(".map");
        picker.FileTypeFilter.Add(".cfg");
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files?.Count > 0)
            ViewModel.AddFilesCommand.Execute(files.Select(f => f.Path));
    }

    // Category ComboBox handler - called from XAML via Tag
    private void CategoryCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not UploadItem item) return;
        if (item.AvailableCategories.Count == 0) return;

        combo.ItemsSource = item.AvailableCategories;
        combo.SelectedItem = item.AvailableCategories.FirstOrDefault(c => c.Id == item.CategoryId);
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not UploadItem item) return;

        if (combo.SelectedItem is CategoryOption cat && !cat.IsParent)
        {
            item.CategoryId = cat.Id;
            item.SelectedCategory = cat;
        }
        else if (combo.SelectedItem is CategoryOption parent && parent.IsParent)
        {
            // Don't allow selecting a parent header - revert
            combo.SelectedItem = item.SelectedCategory;
        }
    }
}
