using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WolffilesUploader.Services;
using WolffilesUploader.ViewModels;
using WolffilesUploader.Views;

namespace WolffilesUploader;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddHttpClient<WolffilesApiService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<UploadHistoryService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<UploadQueueViewModel>();
        services.AddTransient<HistoryViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Apply saved language override BEFORE XAML resources are loaded by MainWindow.
        try
        {
            var langPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WolffilesUploader", "language.txt");
            if (File.Exists(langPath))
            {
                var tag = File.ReadAllText(langPath).Trim();
                if (!string.IsNullOrEmpty(tag))
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
            }
        }
        catch { /* silently fall back to system default */ }

        // Resolve .resw via an explicit ResourceContext — x:Uid alone does not
        // honor PrimaryLanguageOverride in unpackaged WinUI 3 builds.
        LocalizationService.Initialize();

        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Auto-login if token is saved
        var auth = Services.GetRequiredService<AuthService>();
        var api = Services.GetRequiredService<WolffilesApiService>();

        if (auth.HasSavedToken && auth.SavedToken != null)
        {
            api.SetToken(auth.SavedToken);
            MainWindow.NavigateToMain();
        }
        else
        {
            MainWindow.NavigateToLogin();
        }
    }
}
