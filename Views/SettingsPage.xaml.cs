using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using WolffilesUploader.Services;

namespace WolffilesUploader.Views;

public sealed partial class SettingsPage : Page
{
    private readonly AuthService _auth = App.Services.GetRequiredService<AuthService>();
    private readonly WolffilesApiService _api = App.Services.GetRequiredService<WolffilesApiService>();
    private readonly UpdateCheckerService _updater = new();
    private string? _latestReleaseUrl;
    private bool _languageLoaded;

    public SettingsPage()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadUserInfo();
        LoadLanguage();
        LoadVersion();
    }

    private void ApplyLocalization()
    {
        SettingsTitle.Text = LocalizationService.GetString("Settings_Title.Text");
        SettingsAccountHeader.Text = LocalizationService.GetString("Settings_AccountHeader.Text");
        SettingsLogoutButton.Content = LocalizationService.GetString("Settings_LogoutButton.Content");
        SettingsLanguageAppLabel.Text = LocalizationService.GetString("Settings_LanguageAppLabel.Text");
        SettingsLanguageHint.Text = LocalizationService.GetString("Settings_LanguageHint.Text");
        SettingsVersionUpdateHeader.Text = LocalizationService.GetString("Settings_VersionUpdateHeader.Text");
        SettingsCheckUpdateButton.Content = LocalizationService.GetString("Settings_CheckUpdateButton.Content");
        UpdateBtn.Content = LocalizationService.GetString("Settings_DownloadButton.Content");
        UpToDateText.Text = LocalizationService.GetString("Settings_UpToDateText.Text");
        SettingsLinksHeader.Text = LocalizationService.GetString("Settings_LinksHeader.Text");
        SettingsLinkOpenSite.Content = LocalizationService.GetString("Settings_LinkOpenSite.Content");
        SettingsLinkBugReport.Content = LocalizationService.GetString("Settings_LinkBugReport.Content");
        SettingsLinkGitHub.Content = LocalizationService.GetString("Settings_LinkGitHub.Content");
    }

    private void LoadUserInfo()
    {
        var name = _auth.SavedUserName ?? "User";
        UserNameText.Text = name;
        AvatarText.Text = name.Length > 0 ? name[0].ToString().ToUpper() : "W";
        UserRoleText.Text = (_auth.SavedUserRole ?? "user").ToUpper();
    }

    private void LoadLanguage()
    {
        var saved = ApplicationLanguage;
        LanguageCombo.SelectedIndex = saved switch
        {
            "de-DE" => 0,
            "en-US" => 1,
            "fr-FR" => 2,
            _ => 1 // default English
        };
        _languageLoaded = true;
    }

    private void LoadVersion()
    {
        VersionText.Text = $"v{_updater.CurrentVersion}";
    }

    private string ApplicationLanguage
    {
        get
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WolffilesUploader", "language.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "en-US";
            }
            catch { return "en-US"; }
        }
        set
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WolffilesUploader");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "language.txt"), value);
            }
            catch { }
        }
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageLoaded) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;

        ApplicationLanguage = tag;

        var dialog = new ContentDialog
        {
            Title = LocalizationService.GetString("Settings_LanguageRestartTitle"),
            Content = LocalizationService.GetString("Settings_LanguageRestartMessage"),
            PrimaryButtonText = LocalizationService.GetString("Settings_RestartNow"),
            CloseButtonText = LocalizationService.GetString("Settings_Later"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(exe);
        }
        catch { /* ignore — Exit still runs */ }

        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) btn.IsEnabled = false;

        var result = await _updater.CheckAsync();

        if (result.IsUpToDate && !result.CheckFailed)
        {
            UpToDateText.Visibility = Visibility.Visible;
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
        else if (!result.IsUpToDate && result.LatestVersion != null)
        {
            _latestReleaseUrl = result.AppInstallerUrl ?? result.ReleasesUrl;
            UpdateText.Text = $"🆕 Update verfügbar: v{result.LatestVersion}";
            UpdateBanner.Visibility = Visibility.Visible;
            UpToDateText.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpToDateText.Text = "⚠ Update-Check fehlgeschlagen";
            UpToDateText.Visibility = Visibility.Visible;
        }

        if (sender is Button btn2) btn2.IsEnabled = true;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_latestReleaseUrl != null)
            await Launcher.LaunchUriAsync(new Uri(_latestReleaseUrl));
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await _api.LogoutAsync();
        _auth.ClearSession();
        Frame.Navigate(typeof(LoginPage));
    }
}
