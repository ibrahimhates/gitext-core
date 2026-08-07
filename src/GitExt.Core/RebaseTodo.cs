using System.Runtime.Versioning;
using System.Text;

namespace GitExt.Core;

/// <summary>
/// Interactive rebase todo listesindeki bir adımın eylemi (P07-T10).
/// </summary>
/// <remarks>
/// Adlar git'in kendi fiilleri; todo dosyasına birebir bu şekilde yazılıyorlar.
/// </remarks>
public enum RebaseAction
{
    /// <summary>Commit'i olduğu gibi uygula.</summary>
    Pick,

    /// <summary>Uygula ama mesajı değiştir.</summary>
    Reword,

    /// <summary>Uygula ve düzenlemek için dur.</summary>
    Edit,

    /// <summary>Bir öncekine kaynat, mesajları birleştir.</summary>
    Squash,

    /// <summary>Bir öncekine kaynat, <b>bu</b> commit'in mesajını at.</summary>
    Fixup,

    /// <summary>Commit'i tamamen çıkar.</summary>
    Drop,
}

/// <summary>
/// Interactive rebase todo listesindeki tek satır (P07-T10).
/// </summary>
public sealed record RebaseStep
{
    /// <summary>Commit'in tam SHA'sı.</summary>
    public required string ObjectId { get; init; }

    /// <summary>Commit konusu — yalnızca gösterim için.</summary>
    public string Subject { get; init; } = string.Empty;

    public RebaseAction Action { get; init; } = RebaseAction.Pick;

    /// <summary>
    /// <see cref="RebaseAction.Reword"/> için kullanıcının yazdığı yeni mesaj.
    /// </summary>
    public string? NewMessage { get; init; }

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;
}

/// <summary>
/// Interactive rebase todo listesi (P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// git normalde bu listeyi bir editörde açar. Biz <c>GIT_SEQUENCE_EDITOR</c>'ı kendi
/// betiğimize yönlendirip listeyi <b>programatik</b> yazıyoruz.
/// </para>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — betiğe verilen dosya git'in kendi todo'suyla DOLU geliyor.</b>
/// İlk ölçümde betik <c>&gt;&gt;</c> ile eklediği için git 3 komut yerine <b>6</b> gördü,
/// commit'ler iki kez uygulandı ve çakıştı. Yazıcı dosyayı <b>kesmek</b> zorunda —
/// <see cref="RebaseTodoSession"/>'ın betiği bunu yapıyor ve test bunu sabitliyor.
/// </para>
/// </remarks>
public static class RebaseTodo
{
    /// <summary>Todo dosyasının içeriğini üretir.</summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>pick &lt;sha&gt;</c> yeterli — git satırın kalanını yok sayıyor, konu
    /// yazmak şart değil. Yine de yazılıyor: bir şey ters giderse
    /// <c>.git/rebase-merge/git-rebase-todo</c> dosyasına bakan <b>insan</b> ne olduğunu
    /// görebilmeli. Kısa ve tam SHA'nın ikisi de kabul ediliyor; tam SHA yazılıyor ki
    /// kısaltma çakışması hiç doğmasın.
    /// </remarks>
    public static string Render(IReadOnlyList<RebaseStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        StringBuilder builder = new();

        foreach (RebaseStep step in steps)
        {
            if (step.Action == RebaseAction.Drop)
            {
                // `drop` yazmak ile satırı hiç yazmamak aynı sonucu veriyor; `drop`
                // yazılıyor çünkü dosyaya bakan biri için niyet açık olmalı.
                builder.Append("drop ");
            }
            else
            {
                builder.Append(Verb(step.Action)).Append(' ');
            }

            builder.Append(step.ObjectId);

            if (step.Subject is { Length: > 0 } subject)
            {
                // Satır sonu todo'yu bozar; konu tek satıra indirgeniyor.
                builder.Append(" # ").Append(subject.ReplaceLineEndings(" "));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    internal static string Verb(RebaseAction action) => action switch
    {
        RebaseAction.Reword => "reword",
        RebaseAction.Edit => "edit",
        RebaseAction.Squash => "squash",
        RebaseAction.Fixup => "fixup",
        RebaseAction.Drop => "drop",
        _ => "pick",
    };

    /// <summary>
    /// Todo listesi git tarafından kabul edilir mi?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ÖLÇÜLDÜ — boş todo <c>error: nothing to do</c> ile rc=1 veriyor</b> ve rebase
    /// hiç başlamıyor (depo el değmemiş kalıyor — güvenli, ama kullanıcı "hiçbir şey
    /// olmadı" diye şaşırır). Her adımı <c>drop</c> yapmak da aynı kapıya çıkıyor.
    /// </para>
    /// <para>
    /// ⚠️ İlk adım <c>squash</c> ya da <c>fixup</c> olamaz: kaynatacak bir önceki commit
    /// yok. git bu durumda <c>cannot 'squash' without a previous commit</c> diyor.
    /// </para>
    /// </remarks>
    public static string? Validate(IReadOnlyList<RebaseStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<RebaseStep> kept = [.. steps.Where(step => step.Action != RebaseAction.Drop)];

        if (kept.Count == 0)
        {
            return "Tüm commit'ler çıkarılmış — geriye uygulanacak bir şey kalmıyor.";
        }

        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
        {
            return "İlk commit bir öncekine kaynatılamaz; kaynatılacak önceki commit yok.";
        }

        return null;
    }
}

/// <summary>
/// <c>GIT_SEQUENCE_EDITOR</c> (ve gerekiyorsa <c>GIT_EDITOR</c>) kuran geçici oturum
/// (P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// Deseni <see cref="AskPassSession"/>'dan alıyor: git'e bir <b>betik yolu</b> veriliyor,
/// asıl içerik betiğin içine gömülmüyor — betik onu <b>ortamdan</b> okunan bir dosyadan
/// kopyalıyor. Böylece todo metni komut satırında ya da betik gövdesinde görünmüyor ve
/// içindeki tırnak/satırsonu karakterleri kaçış sorunları doğurmuyor.
/// </para>
/// <para>
/// ÖLÇÜLDÜ — sequence editor <b>hata verirse</b> git rebase'i hiç başlatmıyor
/// (rc=1, <c>rebase-merge</c> dizini yok, depo el değmemiş). Yani betiğin başarısızlığı
/// yarım bir duruma yol açmıyor.
/// </para>
/// </remarks>
public sealed class RebaseTodoSession : IDisposable
{
    /// <summary>Todo içeriğinin okunacağı dosyanın yolu.</summary>
    internal const string TodoVariable = "GITEXT_REBASE_TODO";

    /// <summary>Yeni commit mesajının okunacağı dosyanın yolu.</summary>
    internal const string MessageVariable = "GITEXT_REBASE_MESSAGE";

    private readonly List<string> _paths = [];
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);
    private bool _disposed;

    private RebaseTodoSession()
    {
    }

    /// <summary>Komuta eklenecek ortam değişkenleri.</summary>
    public IReadOnlyDictionary<string, string> Environment => _environment;

    /// <summary>
    /// Todo listesini (ve isteğe bağlı yeni mesajı) yazan bir oturum kurar.
    /// </summary>
    /// <param name="todo">Todo dosyasının içeriği.</param>
    /// <param name="message">
    /// <c>reword</c>/<c>squash</c> için kullanılacak mesaj; <see langword="null"/> ise
    /// git'in hazırladığı mesaj değiştirilmeden kabul edilir.
    /// </param>
    public static RebaseTodoSession Create(string todo, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(todo);

        RebaseTodoSession session = new();

        session._environment["GIT_SEQUENCE_EDITOR"] = session.WriteScript("seq", TodoVariable);
        session._environment[TodoVariable] = session.WriteTemporary("todo", todo);

        if (message is not null)
        {
            session._environment["GIT_EDITOR"] = session.WriteScript("msg-editor", MessageVariable);
            session._environment[MessageVariable] = session.WriteTemporary("msg", message);
        }
        else
        {
            // Mesaj verilmediyse editörün hiç açılmaması gerekiyor; `true` her zaman
            // sessizce başarır ve git bunu "kullanıcı değiştirmedi" diye yorumlar.
            session._environment["GIT_EDITOR"] =
                OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true";
        }

        return session;
    }

    private string WriteTemporary(string kind, string content)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"gitext-rebase-{kind}-{Guid.NewGuid():N}");

        File.WriteAllText(path, content.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        _paths.Add(path);
        return path;
    }

    /// <summary>
    /// Verilen ortam değişkenindeki dosyayı git'in verdiği hedefin <b>üzerine</b> yazan
    /// betik.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>&gt;</c> (kes ve yaz) kullanılıyor, <c>&gt;&gt;</c> değil. Ölçümde eklemek,
    /// git'in kendi todo'sunun üstüne bizimkini koyduğu için commit'lerin iki kez
    /// uygulanmasına yol açmıştı.
    /// </remarks>
    private string WriteScript(string kind, string variable)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"gitext-rebase-{kind}-{Guid.NewGuid():N}{(OperatingSystem.IsWindows() ? ".cmd" : ".sh")}");

        string script = OperatingSystem.IsWindows()
            ? $"@echo off\r\ntype \"%{variable}%\" > %1\r\n"
            : $"#!/bin/sh\ncat \"${variable}\" > \"$1\"\n";

        File.WriteAllText(path, script, new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(path);
        }

        _paths.Add(path);
        return path;
    }

    [UnsupportedOSPlatform("windows")]
    private static void MakeExecutable(string path) => File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (string path in _paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Silinememesi işlevi bozmuyor.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
