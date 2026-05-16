using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WolffilesUploader.Services;

namespace WolffilesUploader.Views;

public sealed partial class ShellPage : Page
{
    private readonly AuthService _auth = App.Services.GetRequiredService<AuthService>();
    private readonly WolffilesApiService _api = App.Services.GetRequiredService<WolffilesApiService>();

    public ShellPage()
    {
        InitializeComponent();
        ApplyLocalization();
        SetUserInfo();
        ContentFrame.Navigate(typeof(UploadQueuePage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void ApplyLocalization()
    {
        NavQueueItem.Content = LocalizationService.GetString("Nav_Queue.Content");
        NavHistoryItem.Content = LocalizationService.GetString("Nav_History.Content");
        NavSettingsItem.Content = LocalizationService.GetString("Nav_Settings.Content");
        NavLogoutItem.Content = LocalizationService.GetString("Nav_Logout.Content");
    }

    private void SetUserInfo()
    {
        var name = _auth.SavedUserName ?? "User";
        UserNameText.Text = name;
        AvatarInitial.Text = name.Length > 0 ? name[0].ToString().ToUpper() : "W";
        UserRoleText.Text = (_auth.SavedUserRole ?? "user").ToUpper();
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            switch (tag)
            {
                case "queue":
                    ContentFrame.Navigate(typeof(UploadQueuePage));
                    break;
                case "history":
                    ContentFrame.Navigate(typeof(HistoryPage));
                    break;
                case "settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
                case "logout":
                    await _api.LogoutAsync();
                    _auth.ClearSession();
                    Frame.Navigate(typeof(LoginPage));
                    break;
            }
        }
    }

    public void UpdateQueueBadge(int count)
    {
        QueueBadge.Value = count;
        QueueBadge.Visibility = count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
