using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WolffilesUploader.Models;
using WolffilesUploader.Services;
using WolffilesUploader.ViewModels;
using WinRT.Interop;

namespace WolffilesUploader.Views;

public sealed partial class UploadQueuePage : Page
{
    public UploadQueueViewModel ViewModel { get; }

    public UploadQueuePage()
    {
        InitializeComponent();
        ApplyLocalization();
        ViewModel = App.Services.GetRequiredService<UploadQueueViewModel>();
        Loaded += async (s, e) => await ViewModel.InitAsync();
    }

    private void ApplyLocalization()
    {
        QueueTitle.Text = LocalizationService.GetString("Queue_Title.Text");
        QueueClearDoneButton.Content = LocalizationService.GetString("Queue_ClearDoneButton.Content");
        QueueUploadAllText.Text = LocalizationService.GetString("Queue_UploadAllText.Text");
        QueueDropHint.Text = LocalizationService.GetString("Queue_DropHint.Text");
        QueueDropSub.Text = LocalizationService.GetString("Queue_DropSub.Text");
        QueueBrowseButton.Content = LocalizationService.GetString("Queue_BrowseButton.Content");
    }

    // Localizes elements inside the per-item DataTemplate, where x:Name is
    // not reachable from page code-behind. The element's Tag carries the
    // resource key (e.g. "Field_TitleLabel.Text"); the property assigned
    // is inferred from the element type.
    private void Localize_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
            ApplyTagLocalization(fe);
    }

    private static void ApplyTagLocalization(FrameworkElement fe)
    {
        if (fe.Tag is not string key || key.Length == 0) return;
        var value = LocalizationService.GetString(key);
        switch (fe)
        {
            case TextBlock tb: tb.Text = value; break;
            case TextBox txt: txt.PlaceholderText = value; break;
            case ComboBox combo: combo.PlaceholderText = value; break;
            case Button btn: btn.Content = value; break;
        }
    }

    // Field_GameAuto's Tag is reserved for SelectedValuePath="Tag" ("auto"),
    // so it can't carry the .resw key — set Content directly.
    private void GameAutoItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBoxItem cbi)
            cbi.Content = LocalizationService.GetString("Field_GameAuto.Content");
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
        if (ViewModel.Categories.Count == 0)
            await ViewModel.InitAsync();

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

    private async void ChooseScreenshots_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not UploadItem item) return;
        if (!item.CanAddMoreScreenshots) return;

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files == null) return;

        foreach (var f in files)
        {
            if (item.ScreenshotPaths.Count >= UploadItem.MaxScreenshots) break;
            if (!item.ScreenshotPaths.Contains(f.Path))
                item.ScreenshotPaths.Add(f.Path);
        }
    }

    private void RemoveScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string path) return;
        var item = FindUploadItem(btn);
        item?.ScreenshotPaths.Remove(path);
    }

    // Category ComboBox handler - called from XAML via Tag
    private void CategoryCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;

        ApplyTagLocalization(combo);

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

    private static UploadItem? FindUploadItem(DependencyObject? start)
    {
        var current = start;
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is UploadItem ui)
                return ui;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void TagPill_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        if (btn.Tag is not string value) return;
        var item = FindUploadItem(btn);
        if (item == null) return;
        btn.IsChecked = item.Tags.Contains(value);
    }

    private void TagPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn) return;
        if (btn.Tag is not string value) return;
        var item = FindUploadItem(btn);
        if (item == null) return;

        if (btn.IsChecked == true)
        {
            if (!item.Tags.Contains(value)) item.Tags.Add(value);
        }
        else
        {
            item.Tags.Remove(value);
        }
    }

    private void AddCustomTag_Click(object sender, RoutedEventArgs e)
    {
        var item = FindUploadItem(sender as DependencyObject);
        if (item == null) return;

        var value = item.CustomTagInput?.Trim() ?? "";
        if (value.Length == 0) return;
        if (item.Tags.Contains(value)) { item.CustomTagInput = ""; return; }

        item.Tags.Add(value);
        if (!item.CustomTags.Contains(value)) item.CustomTags.Add(value);
        item.CustomTagInput = "";
    }

    private void RemoveCustomTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string value) return;
        var item = FindUploadItem(btn);
        if (item == null) return;

        item.Tags.Remove(value);
        item.CustomTags.Remove(value);
    }
}
