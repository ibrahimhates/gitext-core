using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// Çalıştırılacak tek bir <c>git</c> komutunun tanımı.
/// </summary>
/// <remarks>
/// <para>
/// Argümanlar <b>dizi olarak</b> tutulur ve asla tek bir komut satırı dizesine birleştirilmez
/// (ADR-0002). Kullanıcı verisi — dosya yolları, ref isimleri, commit mesajları — kabuk
/// yorumlamasına maruz kalmaz.
/// </para>
/// <para>
/// Commit mesajı gibi serbest metinler argüman yerine <see cref="StandardInput"/> ile
/// geçirilmelidir.
/// </para>
/// </remarks>
public sealed record GitCommand
{
    /// <summary>Komutun çalıştırılacağı dizin.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// <c>git</c>'e verilecek argümanlar. Her eleman tek bir argümandır; boşluk içerebilir.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// stdin üzerinden gönderilecek veri. <see langword="null"/> ise stdin hemen kapatılır.
    /// </summary>
    public ReadOnlyMemory<byte>? StandardInput { get; init; }

    /// <summary>
    /// Süreç bu süre içinde bitmezse öldürülür.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Komut depoyu değiştirmiyorsa <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Salt okunur çağrılarda <c>GIT_OPTIONAL_LOCKS=0</c> ayarlanır; bu, arka planda çalışan
    /// bir <c>git status</c>'ün index'i güncellemeye çalışıp kilit çakışması üretmesini önler.
    /// </remarks>
    public bool IsReadOnly { get; init; } = true;

    /// <summary>
    /// Sıfır olmayan çıkış kodunun hata sayılmadığı durumlar.
    /// </summary>
    /// <remarks>
    /// Bazı komutlar başarıyı sıfır dışı kodla bildirir; örneğin <c>git diff --quiet</c>
    /// fark varsa 1 döner. Bu kodlar burada beyan edilir.
    /// </remarks>
    public IReadOnlyCollection<int> SuccessExitCodes { get; init; } = [0];

    /// <summary>
    /// stdout için üst sınır; aşılırsa okuma durdurulur ve süreç sonlandırılır.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="null"/> ise sınır yok. Güvenlik valfi olarak var: <c>git diff</c>
    /// tek bir commit için <b>yüzlerce megabayt</b> yama üretebiliyor (ölçüldü: tamamı
    /// değişen 12,7 MB'lık bir dosya 23 MB yama veriyor) ve bunu belleğe almak uygulamayı
    /// öldürür.
    /// </para>
    /// <para>
    /// Sınır aşıldığında sonuç <see cref="GitResult.OutputTruncated"/> ile işaretlenir;
    /// çağıran kısmi çıktıyı <b>ayrıştırmamalı</b>, farklı bir strateji seçmelidir.
    /// </para>
    /// </remarks>
    public long? MaximumOutputBytes { get; init; }

    /// <summary>
    /// Kısa yol: çalışma dizini ve argümanlarla komut oluşturur.
    /// </summary>
    public static GitCommand Create(string workingDirectory, params string[] arguments) =>
        new() { WorkingDirectory = workingDirectory, Arguments = arguments };

    /// <summary>
    /// Komutu, günlüğe ve kullanıcıya gösterilecek okunabilir biçime çevirir.
    /// </summary>
    /// <remarks>
    /// Bu çıktı <b>yalnızca gösterim içindir</b>; asla bir kabuğa geri verilmez.
    /// Boşluk veya özel karakter içeren argümanlar tırnaklanır ki kullanıcı komutu
    /// terminaline kopyalayabilsin.
    /// </remarks>
    public string ToDisplayString()
    {
        StringBuilder builder = new("git");

        foreach (string argument in Arguments)
        {
            builder.Append(' ');
            builder.Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "''";
        }

        bool needsQuoting = argument.AsSpan().ContainsAny(" \t\n'\"\\$`|&;<>()*?[]{}#~!");
        if (!needsQuoting)
        {
            return argument;
        }

        // POSIX tek tırnak: içerideki tek tırnak '\'' dizisiyle kaçırılır.
        return $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}
