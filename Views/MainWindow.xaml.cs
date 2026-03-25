using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace WolffilesUploader.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();
    }

    private void SetupWindow()
    {
        Title = "Wolffiles Uploader";
        var appWindow = GetAppWindow();
        appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 720));
        appWindow.TitleBar.ExtendsContentIntoTitleBar = false;

        // Center on screen
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest);
        var x = (area.WorkArea.Width - 1280) / 2;
        var y = (area.WorkArea.Height - 720) / 2;
        appWindow.Move(new Windows.Graphics.PointInt32(x, y));

        // Icon setzen
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
            appWindow.SetIcon(iconPath);
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var wndId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(wndId);
    }

    public void NavigateToLogin() => RootFrame.Navigate(typeof(LoginPage));
    public void NavigateToMain() => RootFrame.Navigate(typeof(ShellPage));
}
