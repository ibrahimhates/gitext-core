using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Uzak izleme dallarının ve etiketlerin anlık görüntüsü ve farkı (P06-T06).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden ayrı bir sınıf?</b> İki çağıranı var (<see cref="FetchWriter"/> ve
/// <see cref="PullWriter"/>) ve ikisi de aynı soruyu soruyor: <i>"bu ağ işleminden sonra
/// hangi ref'ler değişti?"</i>. Kopyalanmış iki uygulama, birinin sessizce farklı
/// davranması demekti — P06-T04'ün dersi (ve P06-T05'te <c>RefReader</c>'da gerçekten
/// başımıza gelen şey).
/// </para>
/// <para>
/// 🔴 <c>%(symref)</c> alanı şart: <c>refs/remotes/origin/HEAD</c> <b>sembolik</b> ve
/// <c>origin/main</c>'i izliyor; <c>%(objectname)</c> onu çözdüğü için main her
/// güncellendiğinde ikinci bir "değişiklik" olarak görünüyordu (ölçüldü).
/// </para>
/// </remarks>
internal static class RefSnapshot
{
    private const string Format = "%(refname)%00%(objectname)%00%(symref)";

    /// <summary>Uzak izleme dalları ve etiketler: ref adı → commit.</summary>
    internal static async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                $"--format={Format}",
                "refs/remotes",
                "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, string> refs = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            // Üçüncü alan doluysa ref semboliktir; takma ad olduğu için atlanıyor.
            if (fields.Length == 3 && fields[2].Length == 0)
            {
                refs[fields[0]] = fields[1];
            }
        }

        return refs;
    }

    /// <summary>İki anlık görüntü arasındaki farkı ref adına göre sıralı verir.</summary>
    internal static IReadOnlyList<RefChange> Diff(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        List<RefChange> changes = [];

        foreach ((string refName, string newId) in after)
        {
            if (!before.TryGetValue(refName, out string? oldId))
            {
                changes.Add(new RefChange(refName, null, newId, RefChangeKind.Created));
            }
            else if (!string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                changes.Add(new RefChange(refName, oldId, newId, RefChangeKind.Updated));
            }
        }

        foreach ((string refName, string oldId) in before)
        {
            if (!after.ContainsKey(refName))
            {
                changes.Add(new RefChange(refName, oldId, null, RefChangeKind.Deleted));
            }
        }

        return [.. changes.OrderBy(change => change.RefName, StringComparer.Ordinal)];
    }
}
