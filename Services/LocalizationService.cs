namespace WolffilesUploader.Services;

/// <summary>
/// Reads language.txt and delegates string lookups to the in-process
/// <see cref="Localization"/> table. We dropped the .resw / PRI /
/// ResourceManager stack — it never resolved reliably in this unpackaged
/// WinUI 3 app.
/// </summary>
public static class LocalizationService
{
    public static string ActiveLanguage { get; private set; } = "en-US";

    public static void Initialize()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WolffilesUploader", "language.txt");
            if (File.Exists(path))
            {
                var lang = File.ReadAllText(path).Trim();
                if (Array.IndexOf(Localization.SupportedLanguages, lang) >= 0)
                    ActiveLanguage = lang;
            }
        }
        catch { /* default to en-US */ }
    }

    public static string GetString(string key) => Localization.GetString(key);
}
