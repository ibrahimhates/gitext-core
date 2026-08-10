# Publish kipleri karşılaştırması — 2026-08-10 (P09-T05, P09-T11)

P01-T20'de açık bırakılan **NativeAOT sorusu** burada ölçümle cevaplandı.

| Kip | Binary | Soğuk başlatma | RSS | **PSS** | Yönetilen |
|---|---:|---:|---:|---:|---:|
| Trimsiz + R2R | 132 MB | ~420 ms | 255 MB | — | 9 MB |
| **Trimli + R2R** | **59 MB** | **~390 ms** | 222 MB | **76 MB** | 8 MB |
| Trimli, R2R yok | 26 MB | ~1.315 ms | — | — | — |
| **NativeAOT** | **28 MB** | **~186 ms** | 185 MB | **54 MB** | 9 MB |

Her satır aynı makinede, gerçek X11/Wayland oturumunda, `--bench-startup` ile
(süreç başlangıcından ilk çizilen kareye) ölçüldü; başlatma değerleri 3 koşunun ortası.

---

## Okunan

**ReadyToRun başlatmayı 3.4× hızlandırıyor** ve bedeli 33 MB binary. Bütçe başlatmayı
kısıtlıyor, boyutu kısıtlamıyor — R2R kalıyor. R2R'siz trimli build 26 MB'a iniyor ama
1.3 saniyede açılıyor.

**NativeAOT her metrikte kazanıyor:** R2R'ye göre 2× hızlı başlatma, PSS'te 22 MB daha
az, binary yarısından küçük. Duman testlerinin tamamından geçti — headless teşhis, graph
benchmark (`wide` deposunda R2R ile **aynı** şerit sonucu), gerçek pencere ilk kare, ve
en riskli yol olan **renk körü palet + koyu tema** birlikte.

> ⚠️ **Yine de varsayılan yapılmadı.** AOT bu makinede ve bu senaryolarda çalışıyor;
> ama uygulamanın tamamı — ayarlar ekranı, çakışma çözümü, tüm dialoglar — AOT altında
> **çalıştırılmadı**. Avalonia'nın XAML yansıması AOT'ta çalışma zamanında kırılabiliyor
> ve bu, publish sırasında değil kullanıcının makinesinde ortaya çıkar. Kararı Faz 10
> (paketleme) veriyor; oraya somut sayılarla gidiliyor.

## 🔴 Boşta bellek: bütçe yanlış şeyi ölçüyordu

RSS 222 MB ile bütçenin 200 MB'ını aşıyor görünüyordu. `smaps` dökümü sebebi gösterdi:

```
50 MB  /usr/lib/libnvidia-gpucomp.so.610.43.03
13 MB  /usr/lib/libnvidia-gpucomp.so.610.43.03
12 MB  /usr/lib/libnvidia-glcore.so.610.43.03
 8 MB  /usr/lib/libnvidia-glcore.so.610.43.03
```

**~84 MB, NVIDIA sürücüsünün paylaşımlı kütüphaneleri.** GPU kullanan her süreçte ortak;
uygulamaya ait değiller. `LIBGL_ALWAYS_SOFTWARE=1` ile bile yükleniyorlar.

RSS'i hedeflemek üzerinde çalışılamayacak bir sayıyı kovalamak olurdu: yönetilen yığın
zaten **8 MB**, yani commit önbelleğini bütünüyle sıfırlasak RSS neredeyse hiç düşmez.
Bütçe **PSS**'e çevrildi (paylaşımlı sayfaları kullanıcı sayısına bölüyor):
**76 MB — hedefin çok altında.**

Ölçüm biçimi: `grep -E '^Rss:|^Pss:' /proc/<pid>/smaps_rollup`

## GC ayarları: ölçüldü, etkisi yok

`DOTNET_gcServer=0` ve `=1` arasında RSS farkı 2 MB, başlatma farkı gürültü içinde.
Yönetilen yığın 8 MB olduğu için beklenen sonuç bu; GC ayarı **eklenmedi**, çünkü
ölçülebilir bir kazanç sağlamayan yapılandırma yalnızca bakım yükü.

---

## Yeniden üretme

```
dotnet publish src/GitExt.Desktop -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true -o ./dist

dotnet publish src/GitExt.Desktop -c Release -r linux-x64 --self-contained \
  -p:PublishAot=true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o ./dist-aot

./dist/gitext-core --bench-startup
```
