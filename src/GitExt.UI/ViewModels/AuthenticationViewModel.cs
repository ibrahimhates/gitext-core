using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Kimlik doğrulama başarısızlığında gösterilen ekran (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// 🔑 <b>Ekran ne olduğunu söylüyor, "hata" demiyor.</b> Ölçümde git aynı satırı
/// (<c>Could not read from remote repository.</c>) hem eksik SSH anahtarında hem
/// çözülemeyen sunucu adında yazıyor; ham metni göstermek kullanıcıyı adresini
/// kurcalamaya iterdi.
/// </para>
/// <para>
/// 🔒 <b>Gizli değer hiçbir yere yazılmıyor:</b> ne komut önizlemesine, ne komut
/// günlüğüne, ne diske. <c>credential.helper</c> ayarı yokken git de bir şey kaydetmiyor
/// (ölçüldü) — yani girilen token yalnızca o çağrı boyunca yaşıyor. Kaydetmek isteyen
/// kullanıcıya <b>helper önerisi</b> veriliyor, parolasını bizim saklamamız değil.
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

    /// <summary>Ne olduğunun açıklaması.</summary>
    public string Explanation => Diagnosis.Explanation;

    /// <summary>Uzak depo adresi (parola maskeli).</summary>
    public string? Url => Diagnosis.Url;

    public bool HasUrl => !string.IsNullOrEmpty(Url);

    /// <summary>Çalıştırılabilir öneriler.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Kimlik bilgisi alanları gösterilsin mi?</summary>
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

    /// <summary>Parola ya da kişisel erişim token'ı.</summary>
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

    /// <summary>Kullanıcının verdiği kimlik; iptal edildiyse <see langword="null"/>.</summary>
    public GitCredentials? Result { get; private set; }

    /// <summary>Ekran kapanmalı mı?</summary>
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

/// <summary>Kimlik doğrulama ekranını gösteren taraf (P06-T09).</summary>
public interface IAuthenticationPrompt
{
    /// <summary>
    /// Ekranı modal gösterir; kullanıcı kimlik verdiyse onu, vermediyse
    /// <see langword="null"/> döndürür.
    /// </summary>
    Task<GitCredentials?> ShowAsync(AuthenticationViewModel model);
}
