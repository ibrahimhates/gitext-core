using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The screen shown when authentication fails (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// 🔑 <b>The screen says what happened; it does not say "error".</b> In the measurement git wrote the
/// same line (<c>Could not read from remote repository.</c>) both for a missing SSH key and for a host
/// name that could not be resolved; showing the raw text would push the user into fiddling with their
/// address.
/// </para>
/// <para>
/// 🔒 <b>The secret is written nowhere:</b> not into the command preview, not into the command log,
/// not to disk. With no <c>credential.helper</c> configured git saves nothing either (measured) — so
/// the token entered lives only for the duration of that call. A user who wants it saved is given a
/// <b>helper suggestion</b>, rather than us storing their password.
/// </para>
/// </remarks>
public sealed class AuthenticationViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _secret = string.Empty;

    public AuthenticationViewModel(AuthenticationDiagnosis diagnosis)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);

        Diagnosis = diagnosis;

        foreach (string suggestion in diagnosis.Suggestions)
        {
            Suggestions.Add(suggestion);
        }

        SubmitCommand = new RelayCommand(Submit, () => CanSubmit);
    }

    public AuthenticationDiagnosis Diagnosis { get; }

    /// <summary>The explanation of what happened.</summary>
    public string Explanation => Diagnosis.Explanation;

    /// <summary>Uzak depo adresi (parola maskeli).</summary>
    public string? Url => Diagnosis.Url;

    public bool HasUrl => !string.IsNullOrEmpty(Url);

    /// <summary>Runnable suggestions.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Should the credential fields be shown?</summary>
    public bool CanEnterCredentials => Diagnosis.CanRetryWithCredentials;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                RaiseCanSubmit();
            }
        }
    }

    /// <summary>The password or personal access token.</summary>
    public string Secret
    {
        get => _secret;
        set
        {
            if (SetProperty(ref _secret, value))
            {
                RaiseCanSubmit();
            }
        }
    }

    public bool CanSubmit => CanEnterCredentials
        && Username.Length > 0
        && Secret.Length > 0;

    public IRelayCommand SubmitCommand { get; }

    /// <summary>The credential the user supplied; <see langword="null"/> when cancelled.</summary>
    public GitCredentials? Result { get; private set; }

    /// <summary>Should the screen close?</summary>
    public event EventHandler? Completed;

    private void Submit()
    {
        if (!CanSubmit)
        {
            return;
        }

        Result = new GitCredentials(Username, Secret);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCanSubmit()
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>The side that shows the authentication screen (P06-T09).</summary>
public interface IAuthenticationPrompt
{
    /// <summary>
    /// Shows the screen modally; returns the credential when the user supplied one, and
    /// <see langword="null"/> otherwise.
    /// </summary>
    Task<GitCredentials?> ShowAsync(AuthenticationViewModel model);
}
