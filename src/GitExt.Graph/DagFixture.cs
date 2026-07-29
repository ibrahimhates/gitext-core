using System.Text;

namespace GitExt.Graph;

/// <summary>
/// Testler için metin tabanlı DAG tanımı (P03-T02).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim algoritmasını gerçek bir depo kurmadan, okunabilir senaryolarla test edebilmek için.
/// Gerçek <c>git</c> ile fixture kurmak (Faz 02'deki yaklaşım) burada uygun değil: bir şerit
/// çakışmasını üreten DAG'ı <c>git</c> komutlarıyla anlatmak, aynı şeyi dört satır metinle
/// anlatmaktan çok daha zor ve okunduğunda ne test edildiği anlaşılmıyor.
/// </para>
/// <para>
/// Biçim — her satır bir commit, <b>en yeniden en eskiye</b> (<c>git log</c> sırası):
/// </para>
/// <code>
/// D: B C     # D'nin ebeveynleri B ve C (merge)
/// C: A
/// B: A
/// A:         # kök commit, ebeveyni yok
/// </code>
/// <para>
/// Kurallar:
/// </para>
/// <list type="bullet">
///   <item><c>#</c> ile başlayan satırlar ve boş satırlar yok sayılır.</item>
///   <item>Satır sonundaki <c>#</c> yorumu da atılır.</item>
///   <item>Ebeveynler boşluk veya virgülle ayrılabilir.</item>
///   <item>Kimlikler serbest metindir; okunabilir olsun diye tek harf önerilir.</item>
/// </list>
/// </remarks>
public static class DagFixture
{
    /// <summary>
    /// Metin tanımını commit listesine çevirir.
    /// </summary>
    /// <exception cref="FormatException">
    /// Bir satır ayrıştırılamazsa, kimlik tekrarlanırsa, veya bir commit kendisinden
    /// <b>önce</b> tanımlanmamış bir ebeveyne işaret ederse.
    /// </exception>
    public static IReadOnlyList<DagCommit> Parse(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<DagCommit> commits = [];
        HashSet<string> seen = [];
        int lineNumber = 0;

        foreach (string rawLine in definition.Split('\n'))
        {
            lineNumber++;

            string line = StripComment(rawLine).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                throw new FormatException(
                    $"Satır {lineNumber}: ':' bekleniyordu. Biçim: 'kimlik: ebeveyn1 ebeveyn2'. Gelen: '{line}'");
            }

            string id = line[..colon].Trim();

            if (id.Length == 0)
            {
                throw new FormatException($"Satır {lineNumber}: commit kimliği boş olamaz.");
            }

            if (!seen.Add(id))
            {
                throw new FormatException($"Satır {lineNumber}: '{id}' kimliği birden fazla kez tanımlandı.");
            }

            string[] parents = line[(colon + 1)..]
                .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);

            commits.Add(new DagCommit(id, parents));
        }

        ValidateTopologicalOrder(commits);
        return commits;
    }

    /// <summary>
    /// Girdinin topolojik sırada olduğunu doğrular: her ebeveyn, çocuğundan <b>sonra</b> gelmeli.
    /// </summary>
    /// <remarks>
    /// ADR-0007'nin değişmezi bu. Algoritma tek geçişli ileri tarama yapıyor; bir ebeveyn
    /// çocuğundan önce gelirse kenar yukarı bakar ve grafik bozulur. Fixture'ın kendisi bu
    /// hatayı yapıyorsa test yanlış şeyi doğrular — bu yüzden burada yakalanıyor.
    /// <para>
    /// Tanımlanmamış ebeveynlere izin verilir: kısmi geçmiş (sayfalama sınırı) böyle görünür.
    /// </para>
    /// </remarks>
    private static void ValidateTopologicalOrder(IReadOnlyList<DagCommit> commits)
    {
        Dictionary<string, int> position = [];

        for (int i = 0; i < commits.Count; i++)
        {
            position[commits[i].Id] = i;
        }

        for (int i = 0; i < commits.Count; i++)
        {
            foreach (string parent in commits[i].Parents)
            {
                // Tanımlanmamış ebeveyn = geçmişin kesildiği yer, sorun değil.
                if (position.TryGetValue(parent, out int parentIndex) && parentIndex < i)
                {
                    throw new FormatException(
                        $"Topolojik sıra ihlali: '{parent}' ebeveyni, çocuğu '{commits[i].Id}' "
                        + $"commit'inden ÖNCE tanımlanmış (satır {parentIndex + 1} < {i + 1}). "
                        + "Girdi en yeniden en eskiye sıralı olmalı (ADR-0007).");
                }
            }
        }
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? line : line[..hash];
    }

    /// <summary>
    /// Yerleşim sonucunu, beklenen çıktıyla karşılaştırılabilecek metin tablosuna çevirir.
    /// </summary>
    /// <remarks>
    /// Testlerde beklenen değeri elle kurmak yerine okunabilir bir dize olarak yazabilmek için.
    /// Bir test kırıldığında fark doğrudan gözle görülür.
    /// </remarks>
    public static string Render(IReadOnlyList<GraphRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        StringBuilder builder = new();

        foreach (GraphRow row in rows)
        {
            builder.Append(row.Commit.Id).Append(": şerit=").Append(row.Lane);

            if (row.Edges.Count > 0)
            {
                builder.Append(" kenarlar=");
                builder.AppendJoin(
                    ' ',
                    row.Edges.Select(e => $"{e.FromLane}→{e.ToLane}({e.Target})"));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// Fixture'dan gelen tek bir commit — yalnızca kimlik ve ebeveynler.
/// </summary>
/// <remarks>
/// Yerleşim algoritması yazar, tarih veya mesaj bilmez; bunlara ihtiyaç duymadığı için
/// <see cref="Core.Model.CommitInfo"/> yerine bu daraltılmış tip kullanılıyor. Böylece
/// algoritma testleri tam bir commit kurmak zorunda kalmıyor.
/// </remarks>
public sealed record DagCommit(string Id, IReadOnlyList<string> Parents)
{
    public bool IsMerge => Parents.Count > 1;

    public bool IsRoot => Parents.Count == 0;

    public override string ToString() =>
        Parents.Count == 0 ? Id : $"{Id} → {string.Join(", ", Parents)}";
}
