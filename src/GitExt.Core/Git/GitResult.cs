using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// Tamamlanmış bir <c>git</c> çağrısının sonucu.
/// </summary>
/// <remarks>
/// stdout <b>ham bayt</b> olarak tutulur, <see cref="string"/> olarak değil: dosya adları geçerli
/// UTF-8 olmayabilir ve <c>git show</c> gibi komutlar binary içerik döndürebilir. Metin gerektiğinde
/// <see cref="GetStandardOutputText"/> kullanılır.
/// </remarks>
public sealed class GitResult
{
    public GitResult(
        GitCommand command,
        int exitCode,
        byte[] standardOutput,
        string standardError,
        TimeSpan duration)
    {
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
    }

    /// <summary>Çalıştırılan komut.</summary>
    public GitCommand Command { get; }

    /// <summary>Sürecin çıkış kodu.</summary>
    public int ExitCode { get; }

    /// <summary>Ham stdout içeriği.</summary>
    public byte[] StandardOutput { get; }

    /// <summary>stderr içeriği. Git ilerleme bilgisini de buraya yazar, hata olmasa bile dolu olabilir.</summary>
    public string StandardError { get; }

    /// <summary>Sürecin başlangıcından bitişine kadar geçen süre.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Çıkış kodu, komutun beyan ettiği başarı kodlarından biri mi?
    /// </summary>
    public bool IsSuccess => Command.SuccessExitCodes.Contains(ExitCode);

    /// <summary>
    /// stdout'u UTF-8 metin olarak döndürür.
    /// </summary>
    /// <remarks>
    /// Geçersiz baytlar U+FFFD ile değiştirilir — bozuk kodlamalı bir dosya adı yüzünden
    /// istisna fırlatmak, o dosyayı hiç göstermemekten daha kötüdür.
    /// </remarks>
    public string GetStandardOutputText() =>
        StandardOutput.Length == 0 ? string.Empty : _utf8Lenient.GetString(StandardOutput);

    /// <summary>
    /// stdout'u NUL (<c>\0</c>) ayracına göre böler; <b>boş parçaları atar</b>.
    /// </summary>
    /// <remarks>
    /// Yalnızca her parçanın dolu olduğu bilinen çıktılar için uygundur
    /// (<c>ls-files -z</c> gibi).
    /// <para>
    /// ⚠️ Sabit alanlı kayıtları ayrıştırmak için <b>kullanmayın</b>: boş bir alan
    /// (örneğin gövdesiz bir commit) atıldığında sonraki tüm alanlar kayar ve veri
    /// sessizce yanlış olur. O durumda <see cref="SplitStandardOutputAtNulPreservingEmpty"/>
    /// kullanılmalı.
    /// </para>
    /// </remarks>
    public string[] SplitStandardOutputAtNul()
    {
        string text = GetStandardOutputText();
        return text.Length == 0
            ? []
            : text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// stdout'u NUL ayracına göre böler; <b>boş parçaları korur</b>.
    /// </summary>
    /// <remarks>
    /// Sabit alanlı kayıtların hizasını korumak için gereklidir. Yalnızca akışın en sonundaki
    /// ayraçtan doğan boş parça atılır — <c>git log -z</c> son kaydın ardına da NUL koyar.
    /// </remarks>
    public string[] SplitStandardOutputAtNulPreservingEmpty()
    {
        string text = GetStandardOutputText();

        if (text.Length == 0)
        {
            return [];
        }

        // Sondaki ayraç yapay bir boş parça üretir; onu at, diğer boşları koru.
        if (text[^1] == '\0')
        {
            text = text[..^1];
        }

        return text.Split('\0');
    }

    private static readonly UTF8Encoding _utf8Lenient = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);
}
