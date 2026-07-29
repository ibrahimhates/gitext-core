namespace GitExt.Graph;

/// <summary>
/// Commit DAG'ını dikey şeritlere yerleştirir (P03-T03, P03-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Tek geçişli ileri tarama.</b> Commit'ler <c>--topo-order</c> ile geldiği için her çocuk
/// ebeveyninden önce işlenir (ADR-0007). Motor durumu tutar: bir satır üretildikten sonra
/// bir daha dokunulmaz, bu yüzden yeni commit'ler eklemek önceki satırların şeritlerini
/// <b>değiştirmez</b> — görsel kararlılık böyle sağlanıyor.
/// </para>
/// <para>
/// <b>Çekirdek fikir:</b> bir commit işlenirken, ebeveynleri için şerit <i>rezerve edilir</i>.
/// Rezerve edilen şerit, o ebeveyne ulaşılana kadar dolu kalır. Böylece uzun mesafeli bir kenar
/// geçtiği satırlarda kendi şeridini işgal eder ve başka hiçbir şey oraya yerleşemez —
/// çakışma yapısal olarak imkânsız hale gelir.
/// </para>
/// <para>
/// <b>Düz şeritler:</b> bir commit'in ilk ebeveyni <i>aynı şeritte</i> devam eder. Dolayısıyla
/// bir dalın ana zinciri tek sütunda kalır ve gözle takip edilebilir (ADR-0007).
/// </para>
/// </remarks>
public sealed class GraphLayoutEngine
{
    /// <summary>
    /// Şerit rezervasyonu: bu şerit hangi commit'i bekliyor ve hangi renge sahip.
    /// </summary>
    private readonly record struct LaneSlot(string Target, int ColorIndex);

    private readonly List<LaneSlot?> _lanes = [];
    private int _nextColor;

    /// <summary>Şu ana kadar kullanılmış en geniş şerit sayısı.</summary>
    public int MaxLaneCount { get; private set; }

    /// <summary>İşlenmiş satır sayısı.</summary>
    public int RowCount { get; private set; }

    /// <summary>
    /// Bir commit dizisini yerleştirir.
    /// </summary>
    /// <remarks>
    /// Motoru sıfırlamaz; art arda çağrılabilir. Sonsuz kaydırmada bir sonraki sayfa
    /// bu şekilde eklenir (P03-T06).
    /// </remarks>
    public IReadOnlyList<GraphRow> Add(IEnumerable<DagCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);

        List<GraphRow> rows = [];

        foreach (DagCommit commit in commits)
        {
            rows.Add(Add(commit));
        }

        return rows;
    }

    /// <summary>
    /// Tek bir commit'i yerleştirir ve satırını üretir.
    /// </summary>
    public GraphRow Add(DagCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // 1. Bu commit için önceden rezerve edilmiş şeritleri bul.
        //    Birden fazla varsa, bu commit birden fazla çocuğun ortak ebeveyni demektir
        //    (dallanma noktası); hepsi bu satırda birleşir.
        List<int> reserved = FindReservedLanes(commit.Id);

        int lane;
        int color;

        if (reserved.Count == 0)
        {
            // Hiçbir çocuğu işlenmemiş: bu bir dal ucu (tip). Yeni şerit aç.
            lane = AllocateLane();
            color = NextColor();
        }
        else
        {
            // En soldaki rezervasyonu kullan — dal solda kalsın, yeni dallar sağa açılsın.
            lane = reserved[0];
            color = _lanes[lane]!.Value.ColorIndex;

            // Diğer rezervasyonlar bu satırda son buluyor; şeritleri serbest bırak.
            for (int i = 1; i < reserved.Count; i++)
            {
                _lanes[reserved[i]] = null;
            }
        }

        // 2. Ebeveynler için şerit rezerve et.
        //    İlk ebeveyn AYNI şeritte devam eder — "düz şerit" kuralı budur.
        List<GraphEdge> edges = [];

        if (commit.Parents.Count == 0)
        {
            // Kök commit: zincir burada bitiyor, şerit boşalıyor.
            _lanes[lane] = null;
        }
        else
        {
            _lanes[lane] = new LaneSlot(commit.Parents[0], color);

            edges.Add(new GraphEdge
            {
                FromLane = lane,
                ToLane = lane,
                Target = commit.Parents[0],
                ColorIndex = color,
            });

            // Ek ebeveynler (merge): her biri yeni bir şerit alır.
            for (int i = 1; i < commit.Parents.Count; i++)
            {
                string parent = commit.Parents[i];

                // Bu ebeveyn zaten bir şerit bekliyorsa (başka bir çocuğu onu rezerve etmiş)
                // yeni şerit açma — mevcut rezervasyona bağlan. Aksi halde aynı commit için
                // iki şerit oluşur ve grafik gereksiz genişler.
                int existing = FindReservedLanes(parent).FirstOrDefault(-1);

                if (existing >= 0)
                {
                    edges.Add(new GraphEdge
                    {
                        FromLane = lane,
                        ToLane = existing,
                        Target = parent,
                        ColorIndex = _lanes[existing]!.Value.ColorIndex,
                    });

                    continue;
                }

                int mergeLane = AllocateLane();
                int mergeColor = NextColor();
                _lanes[mergeLane] = new LaneSlot(parent, mergeColor);

                edges.Add(new GraphEdge
                {
                    FromLane = lane,
                    ToLane = mergeLane,
                    Target = parent,
                    ColorIndex = mergeColor,
                });
            }
        }

        // 3. Bu satırdan geçen ama bu commit'le ilgisi olmayan şeritler.
        //    Çizim katmanı bunları düğümsüz düz çizgi olarak çizecek.
        for (int i = 0; i < _lanes.Count; i++)
        {
            if (i == lane || _lanes[i] is not { } slot)
            {
                continue;
            }

            // Bu satırda zaten bir kenarla ele alınmışsa tekrar ekleme.
            if (edges.Any(e => e.ToLane == i))
            {
                continue;
            }

            edges.Add(new GraphEdge
            {
                FromLane = i,
                ToLane = i,
                Target = slot.Target,
                ColorIndex = slot.ColorIndex,
                IsPassThrough = true,
            });
        }

        TrimTrailingFreeLanes();

        int laneCount = Math.Max(_lanes.Count, lane + 1);
        MaxLaneCount = Math.Max(MaxLaneCount, laneCount);
        RowCount++;

        return new GraphRow
        {
            Commit = commit,
            Lane = lane,
            ColorIndex = color,
            Edges = edges,
            LaneCount = laneCount,
        };
    }

    private List<int> FindReservedLanes(string commitId)
    {
        List<int> result = [];

        for (int i = 0; i < _lanes.Count; i++)
        {
            if (_lanes[i] is { } slot && string.Equals(slot.Target, commitId, StringComparison.Ordinal))
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>
    /// En soldaki boş şeridi döndürür; yoksa yeni şerit ekler.
    /// </summary>
    /// <remarks>
    /// Soldan doldurmak grafiği dar tutar. Serbest kalan şeritler yeniden kullanılır —
    /// bu, "düz şerit" kuralını bozmaz çünkü şerit ancak gerçekten boşaldıktan sonra
    /// başkasına verilir.
    /// </remarks>
    private int AllocateLane()
    {
        for (int i = 0; i < _lanes.Count; i++)
        {
            if (_lanes[i] is null)
            {
                return i;
            }
        }

        _lanes.Add(null);
        return _lanes.Count - 1;
    }

    /// <summary>
    /// Sondaki boş şeritleri atarak grafiğin gereksiz geniş görünmesini engeller.
    /// </summary>
    private void TrimTrailingFreeLanes()
    {
        int last = _lanes.Count - 1;

        while (last >= 0 && _lanes[last] is null)
        {
            _lanes.RemoveAt(last);
            last--;
        }
    }

    /// <summary>
    /// Şu an kullanımda olmayan en küçük renk indeksini seçer.
    /// </summary>
    /// <remarks>
    /// Amaç: aynı anda görünen şeritlerin renkleri farklı olsun. Palet boyutu ve gerçek
    /// renkler tema katmanının işi (Faz 08); burada yalnızca ayırt edilebilirlik garanti ediliyor.
    /// </remarks>
    private int NextColor()
    {
        HashSet<int> inUse = [];

        foreach (LaneSlot? slot in _lanes)
        {
            if (slot is { } value)
            {
                inUse.Add(value.ColorIndex);
            }
        }

        for (int candidate = 0; candidate < inUse.Count + 1; candidate++)
        {
            if (!inUse.Contains(candidate))
            {
                return candidate;
            }
        }

        return _nextColor++;
    }
}
