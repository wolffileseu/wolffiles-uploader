using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WolffilesUploader.Services;

namespace WolffilesUploader.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly WolffilesApiService _api;
    private readonly AuthService _auth;

    public LoginViewModel(WolffilesApiService api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        RememberMe = true;
    }

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _rememberMe = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public event EventHandler? LoginSucceeded;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = "";
        HasError = false;
        IsLoading = true;

        try
        {
            var result = await _api.LoginAsync(Email, Password);
            if (result.Success && result.Token != null)
            {
                if (RememberMe && result.User != null)
                    _auth.SaveSession(result.Token, result.User.Name, result.User.Role);

                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.Error ?? "Login fehlgeschlagen";
                HasError = true;
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Keine Verbindung zu wolffiles.eu";
            HasError = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLogin() => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password) && !IsLoading;

    partial void OnEmailChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();
}
