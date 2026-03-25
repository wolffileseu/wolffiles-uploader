using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WolffilesUploader.ViewModels;

namespace WolffilesUploader.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        ViewModel.Refresh();
    }
}
