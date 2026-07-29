namespace GitExt.Core.Git;

/// <summary>
/// <c>git</c> süreçlerini çalıştıran tek kapı.
/// </summary>
/// <remarks>
/// <b>ADR-0002 kuralı:</b> <c>Process.Start</c> uygulamanın başka hiçbir yerinde çağrılmaz.
/// Deterministik ortam, günlükleme, zaman aşımı ve iptal davranışının tek yerde toplanması
/// buna bağlıdır.
/// </remarks>
public interface IGitProcessRunner
{
    /// <summary>
    /// Komutu çalıştırır ve tamamlanmasını bekler.
    /// </summary>
    /// <remarks>
    /// Çıkış kodu ne olursa olsun <see cref="GitResult"/> döner; hata fırlatmaz.
    /// Başarısızlığı istisnaya çevirmek için <see cref="GitProcessRunnerExtensions.RunCheckedAsync"/>
    /// kullanılır.
    /// </remarks>
    /// <exception cref="OperationCanceledException">İptal edildiğinde veya zaman aşımında.</exception>
    Task<GitResult> RunAsync(GitCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Komutu çalıştırır ve stdout'u NUL ayraçlı parçalar hâlinde, <b>süreç bitmeden</b> üretir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 500 bin commit'lik bir depoda <c>git log</c>'un tamamlanmasını beklemek, arayüzün ilk
    /// ekranı çizmesini saniyelerce geciktirir. Bu metot ilk kayıtları anında verir (P02-T04).
    /// </para>
    /// <para>
    /// Boş parçalar <b>korunur</b>: sabit alanlı kayıtlarda boş bir alan atılırsa sonraki tüm
    /// alanlar kayar ve veri sessizce yanlış olur.
    /// </para>
    /// <para>
    /// Çıkış kodu sıfır değilse, akışın <b>sonunda</b> <see cref="GitException"/> fırlatılır —
    /// o ana kadar üretilmiş parçalar geçerlidir.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<string> StreamNulSeparatedAsync(
        GitCommand command,
        CancellationToken cancellationToken = default);
}
