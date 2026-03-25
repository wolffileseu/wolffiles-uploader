using System.Text.Json;

namespace WolffilesUploader.Services;

/// <summary>
/// Persists the Sanctum token using a local JSON file (works unpackaged).
/// </summary>
public class AuthService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WolffilesUploader", "session.json");

    private SessionData _data = new();

    public AuthService() => Load();

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _data = JsonSerializer.Deserialize<SessionData>(json) ?? new();
            }
        }
        catch { _data = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_data));
        }
        catch { }
    }

    public string? SavedToken => _data.Token;
    public string? SavedUserName => _data.UserName;
    public string? SavedUserRole => _data.UserRole;
    public bool HasSavedToken => !string.IsNullOrEmpty(_data.Token);

    public void SaveSession(string token, string userName, string role = "user")
    {
        _data = new SessionData { Token = token, UserName = userName, UserRole = role };
        Save();
    }

    public void ClearSession()
    {
        _data = new SessionData();
        Save();
    }

    private class SessionData
    {
        public string? Token { get; set; }
        public string? UserName { get; set; }
        public string? UserRole { get; set; }
    }
}
