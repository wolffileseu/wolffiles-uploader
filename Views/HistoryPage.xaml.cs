using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WolffilesUploader.Services;
using WolffilesUploader.ViewModels;

namespace WolffilesUploader.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        InitializeComponent();
        ApplyLocalization();
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        ViewModel.Refresh();
    }

    private void ApplyLocalization()
    {
        HistoryTitleText.Text = LocalizationService.GetString("History_Title.Text");
        HistoryClearButton.Content = LocalizationService.GetString("History_ClearButton.Content");
        HistoryTotalUploadsLabel.Text = LocalizationService.GetString("History_TotalUploadsLabel.Text");
        HistoryUploadedLabel.Text = LocalizationService.GetString("History_UploadedLabel.Text");
        HistorySuccessfulLabel.Text = LocalizationService.GetString("History_SuccessfulLabel.Text");
        HistoryEmptyText.Text = LocalizationService.GetString("History_Empty.Text");
    }
}
