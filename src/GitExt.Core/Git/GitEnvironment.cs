using System.Diagnostics;

namespace GitExt.Core.Git;

/// <summary>
/// Her <c>git</c> çağrısını öngörülebilir hale getiren ortam ve yapılandırma ayarları (ADR-0002).
/// </summary>
/// <remarks>
/// Bu ayarlar olmadan <c>git</c>'in davranışı kullanıcının yereline, terminaline ve global
/// yapılandırmasına göre değişir; ayrıştırıcılar da buna bağlı olarak sessizce bozulur.
/// </remarks>
internal static class GitEnvironment
{
    /// <summary>
    /// Süreç ortamını hazırlar.
    /// </summary>
    internal static void Apply(ProcessStartInfo startInfo, bool isReadOnly)
    {
        // Yerelden bağımsız, İngilizce ve deterministik çıktı.
        // Aksi halde tarih biçimleri ve hata metinleri kullanıcının diline göre değişir.
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        // KRİTİK: Kimlik doğrulama isteyen bir komut, bu olmadan terminal beklerken
        // uygulamayı süresiz kilitler. Bunun yerine hata döner ve biz onu ele alırız.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        // Editör açmaya çalışan komutlar (commit, rebase -i, tag -a) takılmasın.
        // Bu değişkenlere ihtiyaç duyan komutlar onları bilinçli olarak kendisi ayarlar.
        startInfo.Environment["GIT_EDITOR"] = "false";
        startInfo.Environment["GIT_SEQUENCE_EDITOR"] = "false";

        // Sayfalayıcı (pager) alt süreçte anlamsızdır ve çıktıyı bozabilir.
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";

        // Grafik kimlik doğrulama araçlarının açılmasını engelle — arayüzü biz yönetiyoruz.
        startInfo.Environment["GIT_ASKPASS"] = string.Empty;
        startInfo.Environment["SSH_ASKPASS"] = string.Empty;

        // Salt okunur çağrılar index'e yazmaya çalışmasın; aksi halde eşzamanlı bir yazma
        // işlemiyle index.lock üzerinden çakışırlar.
        if (isReadOnly)
        {
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        }
    }

    /// <summary>
    /// Her komutun başına eklenen <c>-c</c> yapılandırma geçersiz kılmaları.
    /// </summary>
    /// <remarks>
    /// Argüman olarak verildikleri için kullanıcının <c>.gitconfig</c>'ini kalıcı olarak
    /// değiştirmezler; yalnızca o çağrı için geçerlidirler.
    /// </remarks>
    internal static IEnumerable<string> ConfigurationOverrides()
    {
        // ASCII olmayan dosya adlarını \303\266 gibi sekizlik kaçışlarla değil, olduğu gibi ver.
        // Ayrıştırıcıların bu kaçışları çözmek zorunda kalmaması için.
        yield return "-c";
        yield return "core.quotepath=false";

        // Tavsiye (advice) metinleri stderr'e uzun bloklar yazar. Bu mesajları kullanıcıya
        // kendi arayüzümüzde göstereceğiz; ham hâlleri stderr'i gürültüye boğuyor.
        yield return "-c";
        yield return "advice.detachedHead=false";

        // Commit mesajlarını her zaman UTF-8 olarak al.
        // git, nesnede saklanan kodlamadan (encoding satırı) bu ayara dönüştürür; ölçüldü:
        // ISO-8859-9 saklanmış bir mesaj ham nesnede 0xFC, log çıktısında 0xC3 0xBC geliyor.
        // Varsayılan zaten UTF-8 ama kullanıcı .gitconfig'inde değiştirebilir — o durumda
        // ayrıştırıcılarımız sessizce bozulurdu. Açıkça zorluyoruz.
        yield return "-c";
        yield return "i18n.logOutputEncoding=UTF-8";
    }
}
