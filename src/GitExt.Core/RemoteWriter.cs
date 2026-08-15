using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Uzak depo ekleme seçenekleri (P06-T05).
/// </summary>
public sealed record RemoteAddOptions
{
    /// <summary>Eklenecek remote'un adı.</summary>
    public required string Name { get; init; }

    /// <summary>Fetch URL'si.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// Ekledikten hemen sonra <c>fetch</c> yapılsın mı? (<c>git remote add -f</c>)
    /// </summary>
    /// <remarks>
    /// Varsayılan <b>kapalı</b>: bu bayrak komutu <b>ağa çıkarıyor</b> ve kimlik doğrulama
    /// isteyebiliyor. "Uzak depo ekle" düğmesinin kilitlenmesi beklenmedik olurdu
    /// (ağ işlemlerinin ilerleme/iptali P06-T10).
    /// </remarks>
    public bool FetchAfterAdd { get; init; }
}

/// <summary>
/// Bir uzak deponun silinmeden <b>önce</b> okunmuş hâli ve kurtarma yolu (P06-T05).
/// </summary>
/// <remarks>
/// 🔴 <b>Neden var?</b> ÖLÇÜLDÜ: <c>git remote remove</c> geri alınamaz bir kayıp
/// olabiliyor — yalnızca uzak izleme dalında duran bir commit için
/// <c>refs/remotes/*</c> <b>ve reflog'ları</b> siliniyor, commit "unreachable" oluyor ve
/// <c>gc --prune=now</c> sonrası <b>nesne kayboldu</b>. Ayrıca <c>branch.*.remote</c>,
/// <c>branch.*.merge</c>, <c>branch.*.pushRemote</c> ve <c>remote.pushDefault</c> sessizce
/// siliniyor.
/// <para>
/// ⚠️ Dal silmeden (P06-T03) <b>farkı</b>: oradaki kurtarma komutu nesneleri geri getiriyordu,
/// burada getirmiyor — kurtarma <c>fetch</c> gerektiriyor, yani <b>uzak depo hâlâ
/// erişilebilir olmalı</b>. Kullanıcıya gösterilen metin bunu söylemek zorunda.
/// </para>
/// </remarks>
public sealed record RemoteRemovalPlan
{
    /// <summary>Silinecek remote'un silme öncesi hâli.</summary>
    public required GitRemote Remote { get; init; }

    /// <summary>
    /// Upstream'i bu remote'a bakan yerel dallar: (dal, upstream kısa adı).
    /// </summary>
    public IReadOnlyList<(string Branch, string Upstream)> AffectedBranches { get; init; } = [];

    /// <summary>Bu remote'a işaret eden uzak izleme dallarının kısa adları.</summary>
    public IReadOnlyList<string> TrackingBranches { get; init; } = [];

    /// <summary><c>remote.pushDefault</c> bu remote'u gösteriyorsa <see langword="true"/>.</summary>
    public bool IsPushDefault { get; init; }

    /// <summary>
    /// Kullanıcının <b>olduğu gibi çalıştırabileceği</b> kurtarma komutları.
    /// </summary>
    /// <remarks>
    /// P05-T15 kuralı: geri alınamaz bir işlemde kullanıcı ekranda çalıştırılabilir bir
    /// kurtarma yolu görüyorsa ayrı bir "emin misiniz" diyaloğu yerine onay kutusu yeterli.
    /// </remarks>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>
/// Yeniden adlandırmanın sonucu (P06-T05).
/// </summary>
/// <param name="OldName">Yeniden adlandırmadan önceki ad.</param>
/// <param name="NewName">Yeni ad.</param>
/// <param name="Warnings">
/// git'in <b>çıkış kodu 0</b> ile birlikte verdiği uyarılar. Boş olmayabilir!
/// </param>
public sealed record RemoteRenameResult(string OldName, string NewName, IReadOnlyList<string> Warnings);

/// <summary>URL'nin hangi yönü.</summary>
public enum RemoteUrlKind
{
    /// <summary><c>remote.&lt;ad&gt;.url</c></summary>
    Fetch,

    /// <summary><c>remote.&lt;ad&gt;.pushurl</c></summary>
    Push,
}

/// <summary>
/// Uzak depo yazma işlemleri (P06-T05).
/// </summary>
public interface IRemoteWriter
{
    /// <summary>Yeni bir uzak depo ekler.</summary>
    /// <exception cref="ArgumentException">Ad geçersiz.</exception>
    /// <exception cref="GitException">Ad zaten var ya da başka bir adla çakışıyor.</exception>
    Task AddAsync(
        string workingDirectory,
        RemoteAddOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Silmenin ne kaybettireceğini ve kurtarma yolunu <b>silmeden</b> hesaplar.
    /// </summary>
    Task<RemoteRemovalPlan> PrepareRemovalAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uzak depoyu siler ve silmeden <b>önce</b> hesaplanmış planı döndürür.
    /// </summary>
    /// <remarks>
    /// Plan bilerek dönüş değeri: bilgi silindikten sonra <b>okunamıyor</b>, çağıranın
    /// önce ayrı bir çağrı yapmayı hatırlamasına güvenilmez.
    /// </remarks>
    Task<RemoteRemovalPlan> RemoveAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Uzak depoyu yeniden adlandırır.</summary>
    /// <exception cref="ArgumentException">Yeni ad geçersiz.</exception>
    /// <exception cref="GitException">Ad zaten var veya remote bulunamadı.</exception>
    Task<RemoteRenameResult> RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir uzak deponun <b>tek</b> URL'sini değiştirir.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Remote'ta birden çok URL var. ÖLÇÜLDÜ: bu durumda <c>git remote set-url</c>
    /// <c>remote.&lt;ad&gt;.url has multiple values</c> diyip çıkış kodu 128 ile duruyor;
    /// hangisinin değiştirileceğini <b>kullanıcı</b> seçmeli.
    /// </exception>
    Task SetUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>Aynı yöne ikinci (üçüncü…) bir URL ekler.</summary>
    Task AddUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirtilen URL'yi kaldırır.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: git son fetch URL'sinin silinmesine izin vermiyor
    /// (<c>Will not delete all non-push URLs</c>, çıkış kodu 128).
    /// </remarks>
    Task RemoveUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git remote</c> yazma sarmalayıcısı (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Her komutta <c>--</c> ayracı var.</b> ÖLÇÜLDÜ: <c>-</c> ile başlayan bir ad ayraçsız
/// çağrıda <b>bayrak sanılıyor</b> (<c>error: unknown switch 'x'</c>, çıkış kodu 129) ve
/// <c>--</c> ile aynı ad kabul ediliyor. Kendi doğrulamamız böyle adları reddediyor ama
/// depoda <b>zaten var olan</b> bir remote da böyle adlanmış olabilir.
/// </para>
/// <para>
/// Yazmalar <c>config.lock</c> kullanıyor, <c>index.lock</c> değil; yine de
/// <see cref="IGitWriter"/> üzerinden gidiyorlar — yazma yolunun tek girişi orası (P05-T03)
/// ve kilit çakışmasında yeniden deneme oradan geliyor.
/// </para>
/// </remarks>
public sealed class RemoteWriter : IRemoteWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IRemoteReader _reader;

    public RemoteWriter(IGitWriter writer, IGitProcessRunner runner, IRemoteReader reader)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(reader);

        _writer = writer;
        _runner = runner;
        _reader = reader;
    }

    public async Task AddAsync(
        string workingDirectory,
        RemoteAddOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);

        List<string> arguments = ["remote", "add"];

        if (options.FetchAfterAdd)
        {
            arguments.Add("-f");
        }

        arguments.Add("--");
        arguments.Add(options.Name);
        arguments.Add(options.Url);

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteRemovalPlan> PrepareRemovalAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        GitRemote remote = await _reader.FindAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new GitException(
                GitFailureKind.RemoteNotFound,
                GitFailureClassifier.Describe(GitFailureKind.RemoteNotFound),
                "git remote remove",
                exitCode: 2,
                standardError: $"error: No such remote: '{name}'");

        IReadOnlyList<(string Branch, string Upstream)> affected =
            await ReadAffectedBranchesAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<string> tracking =
            await ReadTrackingBranchesAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false);

        string? pushDefault = await ReadConfigAsync(
                workingDirectory, "remote.pushDefault", cancellationToken)
            .ConfigureAwait(false);

        return new RemoteRemovalPlan
        {
            Remote = remote,
            AffectedBranches = affected,
            TrackingBranches = tracking,
            IsPushDefault = string.Equals(pushDefault, name, StringComparison.Ordinal),
            RecoveryCommands = BuildRecoveryCommands(remote, affected, pushDefault),
        };
    }

    public async Task<RemoteRemovalPlan> RemoveAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        // 🔴 Plan silmeden ÖNCE hesaplanıyor. Silme sonrası config anahtarları ve
        // uzak izleme dalları YOK — geri okunacak hiçbir şey kalmıyor (ölçüldü).
        RemoteRemovalPlan plan =
            await PrepareRemovalAsync(workingDirectory, name, cancellationToken).ConfigureAwait(false);

        await _writer
            .RunAsync(workingDirectory, ["remote", "remove", "--", name], cancellationToken)
            .ConfigureAwait(false);

        return plan;
    }

    public async Task<RemoteRenameResult> RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ValidateName(newName, nameof(newName));

        GitResult result = await _writer
            .RunAsync(workingDirectory, ["remote", "rename", "--", oldName, newName], cancellationToken)
            .ConfigureAwait(false);

        // 🔴 ÖLÇÜLDÜ: varsayılan olmayan bir fetch refspec'i git GÜNCELLEMİYOR ama çıkış kodu
        // yine de 0. Uyarı yalnızca stderr'de duruyor:
        //   warning: Not updating non-default fetch refspec
        // Yalnızca çıkış koduna bakan bir arayüz "başarıyla yeniden adlandırıldı" der,
        // kullanıcının fetch yapılandırması ise eski ada bağlı kalırdı (P06-T02'deki
        // `switch --merge` tuzağının aynısı).
        return new RemoteRenameResult(oldName, newName, ExtractWarnings(result.StandardError));
    }

    public async Task SetUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        // ÖLÇÜLDÜ: birden çok URL varsa git düz `set-url`'ü reddediyor
        // ("has multiple values", çıkış kodu 128). Hatayı git'ten almak yerine burada
        // durduruyoruz ki arayüz kullanıcıya HANGİ URL sorusunu sorabilsin.
        GitRemote? remote = await _reader.FindAsync(workingDirectory, name, cancellationToken)
            .ConfigureAwait(false);

        if (remote is not null)
        {
            IReadOnlyList<string> existing =
                kind == RemoteUrlKind.Fetch ? remote.FetchUrls : remote.PushUrls;

            if (existing.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Remote '{name}' has {existing.Count} URLs configured; "
                    + "it cannot be updated in a single step without choosing which one to change.");
            }
        }

        await RunUrlCommandAsync(workingDirectory, name, kind, url, mode: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task AddUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default) =>
        RunUrlCommandAsync(workingDirectory, name, kind, url, "--add", cancellationToken);

    public Task RemoveUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default) =>
        RunUrlCommandAsync(workingDirectory, name, kind, url, "--delete", cancellationToken);

    private async Task RunUrlCommandAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        string? mode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        List<string> arguments = ["remote", "set-url"];

        if (mode is not null)
        {
            arguments.Add(mode);
        }

        if (kind == RemoteUrlKind.Push)
        {
            arguments.Add("--push");
        }

        arguments.Add("--");
        arguments.Add(name);
        arguments.Add(url);

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateName(string? name, string parameterName)
    {
        // Ad doğrulaması git'e bırakılmıyor: git'in cevabı çıkış kodu 128 ve serbest metin,
        // oysa arayüz kullanıcı YAZARKEN "neden geçersiz" diyebilmeli (P06-T01 kalıbı).
        if (RemoteName.Validate(name) is { } problem)
        {
            throw new ArgumentException(
                $"'{name}' is not a valid remote name ({RemoteName.Describe(problem)})",
                parameterName);
        }
    }

    /// <summary>
    /// Upstream'i bu remote'a bakan yerel dallar.
    /// </summary>
    /// <remarks>
    /// <c>branch.&lt;dal&gt;.remote</c> okunuyor; <c>for-each-ref</c>'in
    /// <c>%(upstream:short)</c> alanı silme sonrası <b>boş</b> döneceği için bu bilgi
    /// yalnızca şimdi toplanabiliyor.
    /// </remarks>
    private async Task<IReadOnlyList<(string Branch, string Upstream)>> ReadAffectedBranchesAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname:short)%00%(upstream:remotename)%00%(upstream:short)",
                "refs/heads"),
            cancellationToken).ConfigureAwait(false);

        List<(string, string)> affected = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            if (fields.Length == 3
                && string.Equals(fields[1], name, StringComparison.Ordinal)
                && fields[2].Length > 0)
            {
                affected.Add((fields[0], fields[2]));
            }
        }

        return affected;
    }

    private async Task<IReadOnlyList<string>> ReadTrackingBranchesAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname:short)",
                RemoteName.RemotesPrefix + name),
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. result.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')),
        ];
    }

    private async Task<string?> ReadConfigAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "--get", key],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Silinen yapılandırmayı geri kuran, <b>olduğu gibi çalıştırılabilir</b> komutlar.
    /// </summary>
    private static IReadOnlyList<string> BuildRecoveryCommands(
        GitRemote remote,
        IReadOnlyList<(string Branch, string Upstream)> affected,
        string? pushDefault)
    {
        List<string> commands = [];

        string first = remote.FetchUrls.Count > 0 ? remote.FetchUrls[0] : string.Empty;
        commands.Add($"git remote add {Quote(remote.Name)} {Quote(first)}");

        foreach (string url in remote.FetchUrls.Skip(1))
        {
            commands.Add($"git remote set-url --add {Quote(remote.Name)} {Quote(url)}");
        }

        foreach (string url in remote.PushUrls)
        {
            string flag = url == remote.PushUrls[0] ? "--push" : "--push --add";
            commands.Add($"git remote set-url {flag} {Quote(remote.Name)} {Quote(url)}");
        }

        // Varsayılan refspec'i `remote add` zaten kuruyor; yalnızca farklıysa yazılıyor.
        if (!remote.HasDefaultFetchRefspec)
        {
            foreach (string refspec in remote.FetchRefspecs)
            {
                commands.Add(
                    $"git config --add remote.{remote.Name}.fetch {Quote(refspec)}");
            }
        }

        if (remote.TagOption is { } tagOption)
        {
            commands.Add($"git config remote.{remote.Name}.tagopt {Quote(tagOption)}");
        }

        // ⚠️ Nesneler `remote add` ile GERİ GELMİYOR: uzak izleme dalları silindi ve
        // reflog'ları da gitti. Yeniden fetch şart — yani uzak depo erişilebilir olmalı.
        commands.Add($"git fetch {Quote(remote.Name)}");

        foreach ((string branch, string upstream) in affected)
        {
            commands.Add($"git branch --set-upstream-to={Quote(upstream)} {Quote(branch)}");
        }

        if (string.Equals(pushDefault, remote.Name, StringComparison.Ordinal))
        {
            commands.Add($"git config remote.pushDefault {Quote(remote.Name)}");
        }

        return commands;
    }

    /// <summary>
    /// Komut metnini kabuğa yapıştırılabilir hâle getirir.
    /// </summary>
    /// <remarks>
    /// Yalnızca <b>gösterim</b> içindir; kendi çağrılarımız argümanları dizi olarak geçiyor
    /// ve hiçbir kabuktan geçmiyor (ADR-0002).
    /// </remarks>
    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        bool safe = value.All(c =>
            char.IsLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or ':' or '@' or '+' or '~' or '*');

        return safe ? value : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static IReadOnlyList<string> ExtractWarnings(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return [];
        }

        List<string> warnings = [];
        StringBuilder current = new();

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.StartsWith("warning:", StringComparison.Ordinal))
            {
                Flush(warnings, current);
                current.Append(line["warning:".Length..].Trim());
            }
            else if (current.Length > 0 && line.Trim().Length > 0)
            {
                // git uyarıyı birden çok satıra yayabiliyor: "Not updating non-default fetch
                // refspec" satırından sonra refspec'in kendisi ve "Please update…" geliyor.
                current.Append(' ').Append(line.Trim());
            }
        }

        Flush(warnings, current);

        return warnings;

        static void Flush(List<string> target, StringBuilder buffer)
        {
            if (buffer.Length > 0)
            {
                target.Add(buffer.ToString());
                buffer.Clear();
            }
        }
    }
}
