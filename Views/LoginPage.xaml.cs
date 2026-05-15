using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WolffilesUploader.Services;
using WolffilesUploader.ViewModels;

namespace WolffilesUploader.Views;

public sealed partial class LoginPage : Page
{
    private static bool _updateAlreadyChecked;

    public LoginViewModel ViewModel { get; }

    public LoginPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<LoginViewModel>();
        ViewModel.LoginSucceeded += OnLoginSucceeded;

        VersionText.Text = $"uploader v{new UpdateCheckerService().CurrentVersion} · wolffiles.eu";
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        Frame.Navigate(typeof(ShellPage));
        _ = CheckAndPromptForUpdateAsync();
    }

    private static async Task CheckAndPromptForUpdateAsync()
    {
        if (_updateAlreadyChecked) return;
        _updateAlreadyChecked = true;

        var updater = new UpdateCheckerService();
        var result = await updater.CheckAsync();
        if (result.IsUpToDate || result.LatestVersion == null) return;

        var xamlRoot = App.MainWindow?.Content?.XamlRoot;
        if (xamlRoot == null) return;

        var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        var messageTemplate = loader.GetString("Update_Available_Message");
        var headline = string.Format(messageTemplate, result.LatestVersion, updater.CurrentVersion);

        var contentPanel = new StackPanel { Spacing = 8 };
        contentPanel.Children.Add(new TextBlock
        {
            Text = headline,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
        {
            contentPanel.Children.Add(new ScrollViewer
            {
                MaxHeight = 240,
                HorizontalScrollMode = ScrollMode.Disabled,
                Content = new TextBlock
                {
                    Text = result.ReleaseNotes,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.85
                }
            });
        }

        var dialog = new ContentDialog
        {
            Title = loader.GetString("Update_Available_Title"),
            Content = contentPanel,
            PrimaryButtonText = loader.GetString("Update_Download_Button"),
            CloseButtonText = loader.GetString("Update_Later_Button"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var dlgResult = await dialog.ShowAsync();
        if (dlgResult != ContentDialogResult.Primary) return;

        var url = result.ReleasesUrl ?? result.AppInstallerUrl;
        if (!string.IsNullOrEmpty(url))
            await Launcher.LaunchUriAsync(new Uri(url));
    }

    private void TextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            PasswordBox.Focus(FocusState.Keyboard);
    }

    private void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.LoginCommand.CanExecute(null))
            ViewModel.LoginCommand.Execute(null);
    }
}
