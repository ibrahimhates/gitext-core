# Uygulama temel çizgisi — 2026-08-10 (P09-T04)

Performans bütçesinin her maddesi ölçüldü. **Bundan sonraki her optimizasyon buna göre.**

| | |
|---|---|
| **Makine** | Linux x64 (CachyOS), .NET 10.0.10 |
| **Yapı** | self-contained, single-file, ReadyToRun, **trimsiz** |
| **Ekran** | X11/Wayland gerçek oturum (headless değil) |
| **Depolar** | `tools/test-repos/generate.sh` ile üretildi |

---

## Bütçe karşısında durum

| Metrik | Hedef | **Ölçülen** | Durum |
|---|---|---|---|
| Soğuk başlatma (ilk kare) | < 1.5 sn | **~420 ms** | ✅ 3.5× marj |
| Repo açma, 10k commit | < 1 sn | **99 ms** (ilk satır 38 ms) | ✅ 10× marj |
| Repo açma, 500k commit | < 5 sn | **3.41 sn** (ilk ekran 1.28 sn) | ✅ |
| Diff açma, 1k satır | < 200 ms | **3 ms** | ✅ |
| Durum tazeleme, 10k dosya | < 300 ms | **47 ms** | ✅ 6× marj |
| Bellek, 500k commit | < 600 MB | **460 MB** | ✅ |
| **Boşta bellek (RSS)** | **< 200 MB** | **255 MB** | 🔴 **AŞILIYOR** |
| Grafik kaydırma | 60 FPS | *ölçülmedi* | ⏳ P09-T09 |

> **Yedi maddenin altısı bütçenin altında, çoğu geniş marjla.** Sekizinci (kaydırma
> FPS'i) etkileşim gerektirdiği için otomatik ölçülemedi; teşhis paneli (P09-T03)
> kare süresini ve düşen kare sayısını gösteriyor, P09-T09'da oradan okunacak.

---

## 🔴 Boşta bellek bütçeyi aşıyor

Boş bir pencere açıkken RSS **255 MB**, hedef 200 MB. Ama yönetilen yığın yalnızca
**9 MB** — yani fark .NET çalışma zamanı ve Avalonia'nın kendi tahsisleri, uygulama
verisi değil. Commit önbelleğini küçültmek bu sayıyı düşürmez.

Gerçekçi seçenekler P09-T05 ve P09-T11'e ait: trimming (şu an **kırık**, aşağıya bak),
`TieredPGO`, `ServerGarbageCollector=false`. Hiçbiri işe yaramazsa **doğru cevap hedefi
revize etmek** — fazın kuralı bunu açıkça söylüyor: *"ulaşılamıyorsa neden ulaşılamadığını
belgelemek ve hedefi dürüstçe revize etmek."*

## 🔴 Trimming kırık (P09-T11'e devrediyor)

`PublishTrimmed=true` **derlenmiyor**:

```
IL2026: AppearanceService.cs(126) — ResourceInclude(Uri) 'RequiresUnreferencedCode'
```

P08'de eklenen renk körü paleti kaplaması (`AppearanceService.ApplyColorBlindOverlay`)
çalışma zamanında `ResourceInclude` kuruyor; trimmer bunun yükleyeceği kaynakları
göremiyor. Faz 01'de trimming **çalışıyordu** (36 MB, 0 IL uyarısı) — bu bir **regresyon**
ve P01-T20'de açık bırakılan NativeAOT sorusunu da doğrudan etkiliyor.

Trimsiz tek dosya binary: **132 MB**. Faz 01'deki trimli ölçüm 36 MB'tı.

---

## Ölçek davranışı

| Commit | İlk satır | Tamamı | Hız | Tutulan bellek |
|---:|---:|---:|---:|---:|
| 10.000 | 38 ms | 99 ms | 101k/sn | 9 MB |
| 250.000 | 699 ms | 2.03 sn | 123k/sn | 229 MB |
| 500.000 | 1.28 sn | 3.41 sn | 147k/sn | 460 MB |

**Süre doğrusaldan iyi:** girdi 50× büyürken süre 34× artıyor — hız aslında
*yükseliyor* (101k → 147k commit/sn), çünkü sabit başlangıç maliyeti büyük depolarda
amorti ediliyor.

**Bellek tam doğrusal ve asıl sınır burada:** satır başına sabit **~960 bayt**.
500k'da 460 MB, 1M commit'te ~920 MB olur — 600 MB'lık bütçeyi 1M'de aşar.
P09-T08 ve P09-T10'un hedefi bu sayı.

> ⚠️ Mikro-benchmark'lardaki (P09-T02) süperlineer büyüme burada **görünmüyor**.
> Orada 5× girdi 6.7× süre veriyordu, burada 50× girdi 34× süre. Fark, oradaki ölçümün
> yalnızca yerleşim algoritmasını, buradakinin ise git süreci + ayrıştırma + yerleşimin
> tamamını kapsaması. Yerleşim, gerçek hattın darboğazı **değil**.

---

## Depo seti

| Depo | Commit | Dal | Dosya | Not |
|---|---:|---:|---:|---|
| `small` | 501 | 1 | 1 | duman testi |
| `medium` | 2.221 | 11 | 2 | 10 gerçek merge |
| `wide` | 401 | **201** | 201 | her dal kökten çatallanıyor |
| `many-files` | 1 | 1 | 1.000 | çalışma dizini taraması |
| `large-10k` / `large-500k` | 10k / 500k | 1 | 1 | ölçek (`fast-import`) |

> 🔴 **`wide` deposu bu görevde yanlış çıktı ve düzeltildi.** 201 dal üretiyordu ama
> `git switch -c` başlangıç noktası verilmeden çağrıldığı için her dal bir öncekinin
> ucundan çıkıyordu: **tek bir zincir, üzerinde 201 etiket**. Maksimum şerit genişliği
> 1'di — yani "dal paneli stresi" için var olan depo, tam da test etmesi gereken şeyi
> hiç test etmiyordu. Kökten çatallanma eklendikten sonra şerit 2'ye çıktı.

> 🔴 **`large-deep` üreteci "250000" diyip 250 commit üretiyordu** — döngü her 1000
> yinelemede bir commit atıyordu. Ölçek testi olarak kullanılan depo, adının iki kat
> büyüklük mertebesi altındaydı. → `git fast-import`: 250k commit **5 saniyede**
> (commit döngüsüyle 250k süreç ≈ 20+ dakika sürerdi).

---

## Yeniden üretme

```
bash tools/test-repos/generate.sh ./test-repos
GEN_LARGE=1 GEN_LARGE_COUNT=500000 bash tools/test-repos/generate.sh ./test-repos

dotnet run --project src/GitExt.Desktop -c Release -- --bench test-repos/large-500k
dotnet run --project src/GitExt.Desktop -c Release -- --bench-startup
```
