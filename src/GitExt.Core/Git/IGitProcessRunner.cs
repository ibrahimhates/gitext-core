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
}
