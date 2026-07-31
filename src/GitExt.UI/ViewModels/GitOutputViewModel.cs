using GitExt.Core;
using GitExt.Core.Git;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Bir git komutunun <b>tam çıktısını</b> kullanıcıya gösteren bileşenin ViewModel'i (P05-T07).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0002'de <c>git</c> CLI'ının seçilme gerekçesi hook desteğiydi. Hook'un yalnızca
/// <b>çalışması</b> yetmez: kullanıcının kurduğu doğrulama bir şey söylüyorsa o söz
/// görünmelidir. Bugüne kadar arayüz yalnızca sınıflandırılmış özeti
/// (<c>"Git komutu başarısız oldu."</c>) gösteriyordu; hook'un asıl çıktısı
/// <see cref="GitException.StandardError"/> içinde kalıp <b>hiç gösterilmiyordu</b>.
/// </para>
/// <para>
/// Bileşen bilinçli olarak <b>bağımsız</b>: commit paneli (P05-T12), dosya işlemleri
/// (P05-T08) ve onay akışı (P05-T15) aynı görünümü kullanacak.
/// </para>
/// </remarks>
public sealed class GitOutputViewModel : ViewModelBase
{
    private GitOutputViewModel(string title, string summary)
    {
        Title = title;
        Summary = summary;
    }

    /// <summary>Pencere/bölüm başlığı.</summary>
    public string Title { get; private init; }

    /// <summary>Tek cümlelik özet — sınıflandırılmış hata açıklaması ya da durum.</summary>
    public string Summary { get; private init; }

    /// <summary>Çalıştırılan komut; kullanıcı terminaline kopyalayabilsin diye.</summary>
    public string CommandLine { get; private init; } = string.Empty;

    /// <summary><see cref="CommandLine"/> gösterilecek mi?</summary>
    public bool HasCommandLine => CommandLine.Length > 0;

    /// <summary>
    /// git'in çıkış kodu; başarı yolunda <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu <b>git'in</b> çıkış kodudur, hook'unki değil. Ölçüldü (P05-T07): çıkış 3 veren
    /// bir <c>pre-commit</c> hook'unda git yine <b>1</b> döndürüyor — hook'un kodu
    /// kaybolur. Arayüz bu sayıyı "hook 3 ile çıktı" diye sunamaz.
    /// </remarks>
    public int? ExitCode { get; private init; }

    /// <summary>Çıkış kodu satırı gösterilecek mi?</summary>
    public bool HasExitCode => ExitCode is not null;

    /// <summary>Çıkış kodunun gösterim metni.</summary>
    public string ExitCodeText => ExitCode is { } code ? $"Çıkış kodu: {code}" : string.Empty;

    /// <summary>
    /// Komutun tam çıktısı — gösterime hazırlanmış (ANSI kodları silinmiş, <c>\r</c> uygulanmış).
    /// </summary>
    public string Output { get; private init; } = string.Empty;

    /// <summary>Gösterilecek çıktı var mı?</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>Çıktı kırpıldıysa kaç satırın atıldığını anlatan not.</summary>
    public string TruncationNotice { get; private init; } = string.Empty;

    /// <summary>Kırpma notu gösterilecek mi?</summary>
    public bool HasTruncationNotice => TruncationNotice.Length > 0;

    /// <summary>
    /// Hook mesajı değiştirdiyse commit'e giren son mesaj; değiştirmediyse boş.
    /// </summary>
    public string FinalMessage { get; private init; } = string.Empty;

    /// <summary>Son mesaj bölümü gösterilecek mi?</summary>
    public bool HasFinalMessage => FinalMessage.Length > 0;

    /// <summary>
    /// Başarısız bir git komutunu gösterime hazırlar.
    /// </summary>
    /// <param name="exception">Yakalanan hata.</param>
    /// <param name="title">Başlık; verilmezse genel bir başlık kullanılır.</param>
    public static GitOutputViewModel ForFailure(GitException exception, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string output = GitOutputText.CleanForDisplay(exception.StandardError, out int dropped);

        return new GitOutputViewModel(title ?? "Git komutu başarısız oldu", exception.Message)
        {
            CommandLine = exception.CommandLine,
            ExitCode = exception.ExitCode,
            Output = output,
            TruncationNotice = Notice(dropped),
        };
    }

    /// <summary>
    /// Tamamlanmış bir commit'i gösterime hazırlar.
    /// </summary>
    /// <remarks>
    /// <b>Gösterilip gösterilmeyeceğine burası karar VERMEZ</b> — çağıran
    /// <see cref="CommitResult.NeedsReporting"/> ile karar verir. Ayrı bir pencerede boş
    /// içerik gürültüdür ama commit paneline (P05-T12) gömülü bir bölümde "hook çıktısı yok"
    /// gayet iyi bir cevaptır; kararı yüzeyi bilen taraf vermeli.
    /// </remarks>
    public static GitOutputViewModel ForCommit(CommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string output = GitOutputText.CleanForDisplay(result.Output, out int dropped);
        bool messageChanged = result.MessageChanged;

        string summary = messageChanged
            ? "Commit oluşturuldu. Hook'lar commit mesajını değiştirdi."
            : "Commit oluşturuldu.";

        return new GitOutputViewModel("Commit tamamlandı", summary)
        {
            Output = output,
            TruncationNotice = Notice(dropped),

            // Değişmediyse gösterilmiyor: kullanıcının zaten yazdığı metni geri okutmak
            // bilgi değil gürültüdür.
            FinalMessage = messageChanged ? result.Message : string.Empty,
        };
    }

    private static string Notice(int droppedLines) =>
        droppedLines > 0
            ? $"Çıktı çok uzun; ilk {droppedLines} satır gösterilmiyor (son "
              + $"{GitOutputText.MaximumDisplayLines} satır aşağıda)."
            : string.Empty;
}
