using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolffilesUploader.Services;

namespace WolffilesUploader.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly WolffilesApiService _api;
    private readonly AuthService _auth;

    public RegisterViewModel(WolffilesApiService api, AuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _passwordConfirm = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    // Password strength: 0=empty, 1=weak, 2=medium, 3=strong
    [ObservableProperty] private int _passwordStrength;
    [ObservableProperty] private string _passwordStrengthText = "";

    public event EventHandler? RegisterSucceeded;

    partial void OnPasswordChanged(string value)
    {
        PasswordStrength = CalculateStrength(value);
        PasswordStrengthText = PasswordStrength switch
        {
            1 => "Schwach / Weak / Faible",
            2 => "Mittel / Medium / Moyen",
            3 => "Stark / Strong / Fort",
            _ => ""
        };
        RegisterCommand.NotifyCanExecuteChanged();
    }
    partial void OnUsernameChanged(string value) => RegisterCommand.NotifyCanExecuteChanged();
    partial void OnEmailChanged(string value) => RegisterCommand.NotifyCanExecuteChanged();
    partial void OnPasswordConfirmChanged(string value) => RegisterCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => RegisterCommand.NotifyCanExecuteChanged();

    private static int CalculateStrength(string pw)
    {
        if (pw.Length < 3) return 0;
        int score = 0;
        if (pw.Length >= 8) score++;
        if (pw.Any(char.IsUpper) && pw.Any(char.IsLower)) score++;
        if (pw.Any(char.IsDigit) || pw.Any(c => !char.IsLetterOrDigit(c))) score++;
        return score;
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        HasError = false;
        ErrorMessage = "";

        if (Password != PasswordConfirm)
        {
            ErrorMessage = "Passwörter stimmen nicht überein";
            HasError = true;
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _api.RegisterAsync(Username, Email, Password, PasswordConfirm);
            if (result.Success && result.Token != null)
            {
                if (result.User != null)
                    _auth.SaveSession(result.Token, result.User.Name, result.User.Role);
                RegisterSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.Error ?? "Registrierung fehlgeschlagen";
                HasError = true;
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Keine Verbindung zu wolffiles.eu";
            HasError = true;
        }
        finally { IsLoading = false; }
    }

    private bool CanRegister() =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Email) &&
        Password.Length >= 8 &&
        !string.IsNullOrWhiteSpace(PasswordConfirm) &&
        !IsLoading;
}
