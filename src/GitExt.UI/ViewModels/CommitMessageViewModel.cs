using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// "Mesaj ▾" menüsündeki tek bir geçmiş girdisi (P05-T13).
/// </summary>
public sealed class CommitMessageHistoryItem
{
    /// <summary>Menüde gösterilen etiketin üst sınırı.</summary>
    /// <remarks>
    /// GitExtensions'ta da 72 (<c>maxLabelLength</c>). Menü öğesi tek satır olmak zorunda;
    /// çok satırlı bir mesaj menüyü ekranın dışına taşırırdı.
    /// </remarks>
    public const int LabelLimit = 72;

    public CommitMessageHistoryItem(string message, ICommand applyCommand)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(applyCommand);

        Message = message;
        ApplyCommand = applyCommand;

        int newline = message.IndexOf('\n', StringComparison.Ordinal);
        string firstLine = (newline < 0 ? message : message[..newline]).TrimEnd('\r');

        Label = firstLine.Length > LabelLimit
            ? string.Concat(firstLine.AsSpan(0, LabelLimit - 1), "…")
            : firstLine;
    }

    /// <summary>Mesajın tamamı — seçilince kutuya bu giriyor.</summary>
    public string Message { get; }

    /// <summary>Menüde görünen tek satırlık etiket.</summary>
    public string Label { get; }

    public ICommand ApplyCommand { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Commit mesajı kutusunun durumu (P05-T12) ve yardımcıları (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// Konu satırı ≤ <b>50</b>, gövde satırları ≤ <b>72</b> — git topluluğunun yerleşik geleneği
/// (<c>git log --oneline</c> ve e-posta yamaları bu genişliklere göre biçimlenmiş).
/// </para>
/// <para>
/// <b>GitExtensions'ta karşılığı ölçüldü:</b> orada da sınırlar var
/// (<c>CommitValidationMaxCntCharsFirstLine</c>, <c>…PerLine</c>,
/// <c>CommitValidationSecondLineMustBeEmpty</c>) ama <b>varsayılanları 0 / kapalı</b> ve
/// denetim commit <i>anında</i> bir onay diyaloğu olarak çıkıyor. Burada tersi seçildi:
/// sınır <b>yazarken</b> görünüyor, hiçbir şeyi engellemiyor. Mesaj yazılıp bitirildikten
/// sonra "şunu düzelt" demek, kullanıcıyı zaten yaptığı işi geri almaya zorlamak olurdu.
/// </para>
/// <para>
/// ⚠️ <b>Hiçbir sınır commit'i ENGELLEMİYOR.</b> Uzun konu satırı bir tercih olabilir;
/// uygulamanın kullanıcıyı kendi deposunda kısıtlaması doğru değil.
/// </para>
/// <para>
/// 🔑 <b>P05-T13'ün değişmez kuralı: kullanıcının yazdığı metnin üzerine hiçbir kaynak
/// yazmaz.</b> Taslak, şablon, <c>MERGE_MSG</c> ve <c>--amend</c> mesajı yalnızca kutu
/// <b>boşken</b> yükleniyor. Geçmişten bir mesaj seçmek tek istisna — orada kullanıcı
/// değiştirmeyi kendisi istiyor (GitExtensions'ın <c>ReplaceMessage</c>'ı da böyle).
/// </para>
/// </remarks>
public sealed partial class CommitMessageViewModel : ViewModelBase
{
    /// <summary>Konu satırı için önerilen üst sınır.</summary>
    public const int SubjectLimit = 50;

    /// <summary>Gövde satırları için önerilen üst sınır.</summary>
    public const int BodyLimit = 72;

    /// <summary>Menüde gösterilecek en fazla geçmiş mesaj.</summary>
    /// <remarks>GitExtensions'ın varsayılanı da 6 (<c>CommitDialogNumberOfPreviousMessages</c>).</remarks>
    public const int HistoryCount = 6;

    private readonly ICommitMessageReader? _reader;
    private readonly ICommitMessageStore? _store;

    private string? _workingDirectory;

    /// <summary>Yükleme sırasında metin değişimi taslak kaydını tetiklemesin.</summary>
    private bool _loading;

    private CancellationTokenSource? _draftSave;

    public CommitMessageViewModel(
        ICommitMessageReader? reader = null,
        ICommitMessageStore? store = null)
    {
        _reader = reader;
        _store = store;

        ApplyHistoryCommand = new RelayCommand<CommitMessageHistoryItem>(item =>
        {
            if (item is not null)
            {
                Text = item.Message;
            }
        });
    }

    /// <summary>Kılavuz çizgilerinin sütunları.</summary>
    public static IReadOnlyList<int> GuideColumns { get; } = [SubjectLimit, BodyLimit];

    /// <summary>XAML bağlaması için örnek üzerinden erişim.</summary>
    /// <remarks>
    /// Avalonia bağlaması <c>static</c> üyeyi <c>{Binding}</c> ile göremiyor; kılavuz
    /// sütunlarını sabit bir dizi olarak XAML'e yazmak ise sınırların iki yerde durması
    /// demek olurdu.
    /// </remarks>
    public IReadOnlyList<int> GuideColumnsForBinding => GuideColumns;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(SubjectLength));
        OnPropertyChanged(nameof(SubjectCounter));
        OnPropertyChanged(nameof(IsSubjectTooLong));
        OnPropertyChanged(nameof(HasNonEmptySecondLine));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));

        ScheduleDraftSave();
    }

    /// <summary>Mesaj boş mu? (yalnızca boşluk da boş sayılır)</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>İlk satır — commit'in konusu.</summary>
    private string Subject
    {
        get
        {
            int newline = Text.IndexOf('\n', StringComparison.Ordinal);

            return (newline < 0 ? Text : Text[..newline]).TrimEnd('\r');
        }
    }

    public int SubjectLength => Subject.Length;

    /// <summary>Sayaç metni; kullanıcı sınırı yazarken görüyor.</summary>
    public string SubjectCounter => $"{SubjectLength} / {SubjectLimit}";

    public bool IsSubjectTooLong => SubjectLength > SubjectLimit;

    /// <summary>
    /// İkinci satır dolu mu? (konu ile gövde arasında boş satır olmalı)
    /// </summary>
    /// <remarks>
    /// git bu ayrımı <b>anlamlı</b> sayıyor: <c>%s</c> ilk satırı, <c>%b</c> boş satırdan
    /// sonrasını veriyor. İkinci satır doluysa gövde konuya yapışıyor ve <c>git log</c>
    /// çıktısı bozuk görünüyor.
    /// </remarks>
    public bool HasNonEmptySecondLine
    {
        get
        {
            string[] lines = Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            return lines.Length > 2 && lines[1].Trim().Length > 0;
        }
    }

    /// <summary>Kullanıcıya gösterilecek biçim önerisi; sorun yoksa boş.</summary>
    public string Hint => this switch
    {
        { HasNonEmptySecondLine: true } => "Leave a blank line between the subject and the body.",
        { IsSubjectTooLong: true } => $"The subject line exceeds {SubjectLimit} characters.",
        _ => string.Empty,
    };

    public bool HasHint => Hint.Length > 0;

    // ---- Geçmiş (P05-T13) ----

    /// <summary>"Mesaj ▾" menüsünün içeriği.</summary>
    public ObservableCollection<CommitMessageHistoryItem> RecentMessages { get; } = [];

    /// <summary>
    /// Yalnızca kullanıcının kendi commit'lerinin mesajları listelensin mi?
    /// </summary>
    /// <remarks>
    /// GitExtensions'taki <c>ShowOnlyMyMessages</c>'ın karşılığı. Ortak bir depoda son altı
    /// commit'in tamamı başkalarının olabilir ve menü işe yaramaz hâle gelir.
    /// </remarks>
    [ObservableProperty]
    public partial bool OnlyMyMessages { get; set; }

    partial void OnOnlyMyMessagesChanged(bool value) => _ = LoadRecentAsync();

    /// <summary>
    /// Geçmiş mesajları okur.
    /// </summary>
    /// <remarks>
    /// Menü <b>açılırken</b> çağrılıyor, depo açılırken değil: her depo açılışında bir
    /// <c>git log</c> daha çalıştırmak, kullanıcının hiç açmayacağı bir menü için ödenen
    /// bedel olurdu.
    /// </remarks>
    public async Task LoadRecentAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        IReadOnlyList<string> messages;

        try
        {
            messages = await _reader
                .ReadRecentAsync(directory, HistoryCount, OnlyMyMessages, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Core.Git.GitException)
        {
            // Geçmiş okunamadıysa menü boş kalır; commit ekranı çalışmaya devam etmeli.
            return;
        }

        RecentMessages.Clear();

        foreach (string message in messages)
        {
            RecentMessages.Add(new CommitMessageHistoryItem(message, ApplyHistoryCommand));
        }
    }

    /// <summary>Geçmişten seçilen mesajı kutuya koyar.</summary>
    /// <remarks>
    /// Bu <b>tek</b> yerde var olan metnin üzerine yazılıyor — kullanıcı menüden seçerek
    /// tam olarak bunu istedi (GitExtensions'ın <c>ReplaceMessage</c>'ı da böyle).
    /// </remarks>
    public IRelayCommand<CommitMessageHistoryItem> ApplyHistoryCommand { get; }

    // ---- Şablon (P05-T13) ----

    /// <summary><c>commit.template</c> ile yapılandırılmış şablon; yoksa <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial CommitTemplate? Template { get; private set; }

    partial void OnTemplateChanged(CommitTemplate? value)
    {
        OnPropertyChanged(nameof(HasTemplate));
        OnPropertyChanged(nameof(CanApplyTemplate));
        OnPropertyChanged(nameof(TemplateLabel));
    }

    /// <summary>Depoda bir şablon yapılandırılmış mı? (dosya bulunamamış olabilir)</summary>
    public bool HasTemplate => Template is not null;

    /// <summary>Şablon gerçekten uygulanabilir mi?</summary>
    public bool CanApplyTemplate => Template is { IsMissing: false };

    /// <summary>
    /// Menüde gösterilecek şablon satırı.
    /// </summary>
    /// <remarks>
    /// Bulunamayan şablon <b>gizlenmiyor</b>: git'in kendisi bu durumda commit'i
    /// <c>fatal: could not read</c> ile reddediyor (ölçüldü), yani kullanıcının
    /// yapılandırması gerçekten bozuk. Boş bir menü göstermek sorunu saklamak olurdu.
    /// </remarks>
    public string TemplateLabel => Template switch
    {
        null => "commit.template is not set",
        { IsMissing: true } t => $"Template not found: {t.Path}",
        { Path: var path } => Path.GetFileName(path),
    };

    /// <summary>
    /// Şablonu kutuya yükler.
    /// </summary>
    /// <remarks>
    /// 🔴 Yorum satırları <b>temizlenerek</b> yükleniyor (<see cref="CommitMessageText"/>):
    /// git'in editör yolu onları commit'e sokmuyor, bizim <c>--cleanup=whitespace</c>
    /// yolumuz sokardı. Kutuda görünen ne ise commit'lenen odur.
    /// </remarks>
    public async Task ApplyTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        if (Template is not { Text: { } text })
        {
            return;
        }

        string commentCharacter = await _reader
            .ReadCommentCharacterAsync(directory, cancellationToken)
            .ConfigureAwait(true);

        SetLoadedText(CommitMessageText.PrepareForEditing(text, commentCharacter));
    }

    // ---- Taslak ve yükleme (P05-T13) ----

    /// <summary>
    /// Taslak diske yazılmadan önce beklenen süre.
    /// </summary>
    /// <remarks>
    /// Her tuş vuruşunda dosyaya yazmak yerine sonuncusundan sonra bir kez yazılıyor.
    /// Testlerde sıfırlanabilsin diye ayarlanabilir.
    /// </remarks>
    public TimeSpan DraftSaveDelay { get; set; } = TimeSpan.FromMilliseconds(750);

    /// <summary>Kutuya yüklenen metnin kaynağı; kullanıcı yazdıysa <see cref="CommitMessageSource.None"/>.</summary>
    [ObservableProperty]
    public partial CommitMessageSource Source { get; private set; }

    /// <summary>
    /// Depoyu bağlar ve yüklenecek bir mesaj varsa kutuya koyar.
    /// </summary>
    public async Task OpenAsync(string? workingDirectory, CancellationToken cancellationToken = default)
    {
        _workingDirectory = workingDirectory;

        RecentMessages.Clear();
        Template = null;
        Source = CommitMessageSource.None;

        if (workingDirectory is not { Length: > 0 } directory)
        {
            SetLoadedText(string.Empty);
            return;
        }

        if (_reader is not null)
        {
            try
            {
                Template = await _reader.ReadTemplateAsync(directory, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Core.Git.GitException)
            {
                Template = null;
            }
        }

        if (_store is null)
        {
            return;
        }

        PendingCommitMessage pending = await _store.ReadAsync(directory, cancellationToken)
            .ConfigureAwait(true);

        // Kullanıcının yazmakta olduğu metnin üzerine yazılmıyor. Ekran yeniden açıldığında
        // kutu zaten boş olur; dolu olduğu tek durum ekranın açık kalmasıdır.
        if (pending.HasText && IsEmpty)
        {
            SetLoadedText(pending.Text);
            Source = pending.Source;
        }
    }

    /// <summary>
    /// <c>HEAD</c>'in mesajını yükler (<c>--amend</c> işaretlenince).
    /// </summary>
    /// <remarks>
    /// Yalnızca kutu boşken. GitExtensions'ta da koşul bu: kullanıcı yeni bir mesaj yazmaya
    /// başladıysa amend kutusunu işaretlemesi onu silmek anlamına gelmemeli.
    /// </remarks>
    public async Task LoadHeadMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || !IsEmpty || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        string? message;

        try
        {
            message = await _reader.ReadHeadMessageAsync(directory, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Core.Git.GitException)
        {
            return;
        }

        if (message is { Length: > 0 })
        {
            SetLoadedText(message);
        }
    }

    /// <summary>
    /// Bekleyen taslak kaydını hemen diske yazar.
    /// </summary>
    /// <remarks>
    /// Pencere kapanırken çağrılıyor: gecikmeli kayıt henüz çalışmamış olabilir ve
    /// kullanıcının son yazdığı satır kaybolurdu.
    /// </remarks>
    public async Task FlushDraftAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingSave();

        if (_store is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        await _store.SaveDraftAsync(directory, Text, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Başarılı commit sonrası: kutu ve taslak temizlenir.
    /// </summary>
    /// <remarks>
    /// Taslağı silmek <b>şart</b>: commit'lenen mesaj diskte kalsaydı ekran bir daha
    /// açıldığında az önce commit'lenmiş metin geri gelir ve ikinci bir commit'e davet
    /// ederdi.
    /// </remarks>
    public async Task OnCommittedAsync(CancellationToken cancellationToken = default)
    {
        Clear();

        if (_store is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        await _store.ClearDraftAsync(directory, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Mesajı temizler.</summary>
    public void Clear()
    {
        CancelPendingSave();

        _loading = true;

        try
        {
            Text = string.Empty;
            Source = CommitMessageSource.None;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Dışarıdan gelen metni kutuya koyar; taslak kaydını tetiklemez.</summary>
    private void SetLoadedText(string text)
    {
        _loading = true;

        try
        {
            Text = text;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Taslak kaydını erteler.
    /// </summary>
    /// <remarks>
    /// ⚠️ İptal ile yeni jetonun atanması arasında <c>await</c> <b>yok</b> — P04-T08'de
    /// ölçülmüştü: arada bir <c>await</c> olduğunda art arda gelen çağrılar birbirini
    /// iptal edemiyor ve her biri ayrı bir iş başlatıyordu.
    /// </remarks>
    private void ScheduleDraftSave()
    {
        if (_loading || _store is null || _workingDirectory is not { Length: > 0 })
        {
            return;
        }

        CancelPendingSave();

        _draftSave = new CancellationTokenSource();

        _ = SaveDraftLaterAsync(Text, _draftSave.Token);
    }

    private async Task SaveDraftLaterAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            if (DraftSaveDelay > TimeSpan.Zero)
            {
                await Task.Delay(DraftSaveDelay, cancellationToken).ConfigureAwait(true);
            }

            if (_store is not null && _workingDirectory is { Length: > 0 } directory)
            {
                await _store.SaveDraftAsync(directory, text, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Kullanıcı yazmaya devam etti; bir sonraki kayıt zaten planlandı.
        }
    }

    private void CancelPendingSave()
    {
        _draftSave?.Cancel();
        _draftSave?.Dispose();
        _draftSave = null;
    }
}
