<div align="center">

# gitext-core

[English](./README.md) · **Türkçe**

**Hızlı, yerel (native), çapraz platform bir Git arayüzü — GitExtensions deneyimi, Windows'a bağlı kalmadan.**

<!-- TODO(readme-01): CI çalıştıktan ve ilk sürüm yayınlandıktan sonra rozetler eklenecek.
     Planlanan: build durumu, son sürüm, indirme sayısı, lisans. -->

> ⚠️ **Durum: pre-alpha — henüz kullanılabilir değil.**
> Proje derleniyor ve bir pencere açıyor, ama **henüz Git ile hiçbir şey yapmıyor.**
> Yayınlanmış bir sürüm yok. Aşağıda *(planlanan)* olarak işaretlenen her şey hedeflenen
> son durumu anlatır, mevcut davranışı değil.

</div>

<!-- TODO(readme-02): Ana ekran görüntüsü buraya (commit grafiği, koyu tema, gerçek bir repo).
     Commit grafiği gösterilebilir hale gelene kadar bekliyor. -->

---

## Bu proje neden var?

Windows tarafında [GitExtensions](https://github.com/gitextensions/gitextensions) üç şey için
seviliyor: karmaşık geçmişleri gerçekten okunabilir kılan bir commit grafiği, Git'in nasıl
çalıştığını birebir yansıtan bir arayüz ve hiçbir zaman önünüze geçmeyen bir hız.

Windows Forms üzerine kurulu olduğu için Linux'ta yerel olarak çalışamıyor. Alternatiflerin çoğu
Electron tabanlı; yani özünde metin ve grafik çizen bir uygulama için yüzlerce megabayt RAM.

**gitext-core**, bu deneyimi modern .NET ve [Avalonia UI](https://avaloniaui.net/) üzerine yeniden
kuruyor: yerel render, yerel performans, Linux/Windows/macOS için tek kod tabanı.

Bu bir port değil, GitExtensions'dan **ilham alan temiz oda (clean-room) yeniden uygulamasıdır**
ve GitExtensions projesiyle bağlantılı değildir.

### Tasarım ilkeleri

| | |
|---|---|
| **Git'in kendisi, Git kılıklı bir soyutlama değil** | Arayüz Git'in gerçek modelini yansıtır — ref'ler, nesneler, index, çalışma dizini. Hiçbir şey "senin iyiliğin için" gizlenmez. |
| **Komutu göster** | Her işlem, arkasında çalışan `git` komutunu gösterir. Araçtan öğrenebilmeli ve aynı komutu terminalde tekrarlayabilmelisin. |
| **Devasa repolarda hızlı** | Sanallaştırılmış, artımlı render. 500 bin commit'lik bir geçmiş ekran tazeleme hızında kaymalı. |
| **Önce klavye** | Sık kullanılan her işlem fare olmadan erişilebilir. |
| **Kaynak dostu** | Yerel bileşenler, tarayıcı motoru yok. |
| **Telemetri yok** | Hiçbir zaman. |

---

## Özellikler *(planlanan)*

- **Görsel commit grafiği** — dallar, merge'ler, tag'ler ve ref'ler renk kodlu bir DAG olarak; çok büyük geçmişler için sanallaştırılmış.
- **Diff ve dosya inceleme** — yan yana ve birleşik (unified) diff, kelime seviyesinde vurgulama, boşluk kontrolleri.
- **Staging ve commit** — dosya, hunk ve tek satır seviyesinde stage/unstage; amend; sign-off; commit mesajı şablonları.
- **Branch ve remote işlemleri** — checkout, oluşturma, yeniden adlandırma, silme, fetch, pull, push, takip, prune.
- **İleri düzey işlemler** — interactive rebase, cherry-pick, revert, stash yönetimi, reset, tag yönetimi.
- **Merge conflict çözümü** — uygulama içi üç yollu görünüm, artı kendi `merge.tool` ayarınla entegrasyon.
- **Repo gezinme** — herhangi bir revizyondaki dosya ağacı, blame, dosya geçmişi, yeniden adlandırmalar boyunca takip.
- **Submodule, worktree ve Git LFS farkındalığı.**
- **Reflog tarayıcı** — kaybolan commit'leri bul, son işlemini geri al.
- **Arama** — commit mesajları, diff içeriği ve dosya içeriği üzerinde.

---

## Platform desteği

| Platform | Durum |
|---|---|
| Linux — X11 | Derleniyor ve çalışıyor |
| Linux — Wayland | Derleniyor ve çalışıyor (opt-in backend, aşağıya bak) |
| Windows 10/11 | Cross-compile ediliyor; hedef platformda henüz çalıştırılmadı |
| macOS (Apple Silicon + Intel) | Cross-compile ediliyor; hedef platformda henüz çalıştırılmadı |

Linux birinci sınıf hedef ve geliştirmenin yapıldığı yer. Windows ve macOS yapıları aynı kod
tabanından, aynı yayın hattıyla üretiliyor.

---

## Kurulum

> **Bunların hiçbiri henüz mevcut değil.** Yayınlanmış sürüm yok. Bu bölüm hedeflenen dağıtım
> kanallarını belgeliyor; her komut ilk sürüm çıkmadan önce doğrulanacak.

Dağıtım öncelikli olarak **GitHub Releases** üzerinden, ayrıca topluluk paket depolarıyla yapılır.

### Gereksinimler

- **Git ≥ 2.30** kurulu ve `PATH` üzerinde olmalı.
  gitext-core, Git'i yeniden yazmak yerine gerçek `git` binary'sini çalıştırır; bu sayede
  hook'ların, credential helper'ların, `.gitconfig`'in, LFS kurulumun ve alias'ların terminalde
  olduğu gibi çalışmaya devam eder.
- .NET çalışma zamanı gerekmez — resmi yapılar self-contained'dir.

### Linux

#### AppImage *(evrensel seçenek)*

```bash
curl -LO https://github.com/ibrahimhates/gitext-core/releases/download/v<sürüm>/gitext-core-<sürüm>-x86_64.AppImage
chmod +x gitext-core-<sürüm>-x86_64.AppImage
./gitext-core-<sürüm>-x86_64.AppImage
```

Masaüstü entegrasyonu (menü girdisi, ikon) için [Gear Lever](https://github.com/mijorus/gearlever)
veya `appimaged` kullanılabilir.

#### Flatpak *(planlanan — Flathub)*

```bash
flatpak install flathub io.github.ibrahimhates.GitExtCore
flatpak run io.github.ibrahimhates.GitExtCore
```

#### Arch Linux / Manjaro *(AUR)*

```bash
yay -S gitext-core-bin      # hazır binary (önerilen)
yay -S gitext-core          # kaynaktan derle
```

#### Fedora / RHEL / openSUSE *(RPM)*

```bash
sudo dnf install ./gitext-core-<sürüm>-1.x86_64.rpm
```

#### Debian / Ubuntu / Linux Mint *(DEB)*

```bash
sudo apt install ./gitext-core_<sürüm>_amd64.deb
```

#### Taşınabilir arşiv

```bash
tar -xzf gitext-core-<sürüm>-linux-x64.tar.gz
./gitext-core/gitext-core
```

### Windows

```powershell
winget install gitext-core   # planlanan
```

Ya da Releases'ten taşınabilir `gitext-core-<sürüm>-win-x64.zip` dosyasını indirip
`gitext-core.exe` çalıştırılabilir.

### macOS

```bash
brew install --cask gitext-core   # planlanan
```

---

## Kullanım

> **Henüz geçerli değil.** Uygulama şu an boş bir pencere açıyor. Bu bölüm, özellikler
> tamamlandıkça gerçek iş akışını belgeleyecek.

<!-- TODO(readme-15): Gerçek kullanım kılavuzu yazılacak. Sırasıyla kapsaması gerekenler:
     1. Repo açma (klasör seçici, son açılanlar, sürükle-bırak)
     2. Commit grafiğini okuma — şerit renkleri ve ref rozetleri ne anlama geliyor
     3. Bir commit'i ve diff'ini inceleme
     4. Dosya/hunk/satır seviyesinde stage ve commit
     5. Branch, fetch, pull, push
     6. Rebase, cherry-pick, stash ve conflict çözümü
     7. Git çıktı paneli — çalışan komutu görme
     8. Klavye kısayolları referansı
     Her bölüm için ekran görüntüsü gerekli. İlgili özellik tamamlanana kadar bekliyor. -->

### Pencere backend'i seçimi (Linux)

gitext-core varsayılan olarak X11 kullanır; bu, Wayland oturumlarında da XWayland üzerinden
çalışır. Avalonia'nın yerel Wayland backend'i opt-in'dir ve ortam değişkeniyle seçilir:

```bash
GITEXT_BACKEND=wayland gitext-core   # yerel Wayland
GITEXT_BACKEND=x11     gitext-core   # X11'e zorla
GITEXT_BACKEND=auto    gitext-core   # varsayılan
```

Pencere açılmıyor veya hatalı render ediliyorsa ilk denenecek şey backend değiştirmektir.

---

## Kaynaktan derleme

### Ön gereksinimler

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | **10.0** veya üzeri | [indir](https://dotnet.microsoft.com/download) |
| Git | **2.30** veya üzeri | Aynı zamanda çalışma zamanı bağımlılığı |

Linux'ta ayrıca Avalonia'nın render için kullandığı masaüstü kütüphaneleri gerekir. Normal bir
masaüstü kurulumunda bunlar zaten mevcuttur.

### Klonla, derle, çalıştır

```bash
git clone https://github.com/ibrahimhates/gitext-core.git
cd gitext-core

dotnet restore
dotnet build -c Release
dotnet run --project src/GitExt.Desktop
```

### Test

```bash
dotnet test
```

### Self-contained binary üretme

```bash
# Linux
dotnet publish src/GitExt.Desktop -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/linux-x64

# Windows
dotnet publish src/GitExt.Desktop -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/win-x64

# macOS (Apple Silicon)
dotnet publish src/GitExt.Desktop -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:PublishReadyToRun=true \
  -o ./dist/osx-arm64
```

Dört hedef RID de (`linux-x64`, `win-x64`, `osx-arm64`, `osx-x64`) Linux'tan cross-compile edilir.
Şimdiye kadar yalnızca Linux çıktısı çalıştırılıp doğrulandı.

### Proje yapısı

```
src/
├── GitExt.Core/      # Git süreç katmanı, modeller, çıktı ayrıştırıcıları — UI bağımlılığı yok
├── GitExt.Graph/     # Commit DAG şerit atama algoritması — saf, yoğun test edilmiş
├── GitExt.UI/        # Avalonia view'ları, view model'lar, kontroller, temalar
└── GitExt.Desktop/   # Giriş noktası, platform bootstrap, DI composition root
```

`GitExt.Core` ve `GitExt.Graph` hiçbir UI paketine referans veremez. Bu kural derleme zamanında
zorlanır — ihlal, kod incelemesini beklemeden `GITEXT001` hatasıyla derlemeyi kırar.

### Teknoloji

| Alan | Seçim |
|---|---|
| UI framework | Avalonia 12.1 |
| Git erişimi | `git` CLI, alt süreç olarak çalıştırılıyor |
| Çalışma zamanı | .NET 10 |
| MVVM | CommunityToolkit.Mvvm |
| Testler | xUnit v3 + Shouldly |

---

## Katkı

Temel oturduktan sonra katkılar memnuniyetle karşılanır.

O zamana kadar en faydalı katkı issue açmaktır: hata bildirimleri, eksik kapsam ve GitExtensions
kullanıcılarının günlük hayatta neyin gerçekten önemli olduğuna dair deneyim aktarımları.

---

## Teşekkür

- [GitExtensions](https://github.com/gitextensions/gitextensions) — bu projenin kendini kıyasladığı standart.
- [Avalonia UI](https://avaloniaui.net/) — yerel çapraz platform .NET arayüzünü pratik kılan framework.
- [Git](https://git-scm.com/) — işin aslı.

---

## Lisans

[GNU General Public License v3.0 veya üzeri](./LICENSE) — `GPL-3.0-or-later`

Copyright (C) 2026 gitext-core contributors.

gitext-core özgür yazılımdır: kullanabilir, inceleyebilir, paylaşabilir ve değiştirebilirsiniz.
Değiştirilmiş bir sürümü dağıtırsanız, o da aynı lisans altında özgür yazılım olmak zorundadır.

Bu program faydalı olacağı umuduyla dağıtılmaktadır, ancak **hiçbir garanti verilmez**;
satılabilirlik veya belirli bir amaca uygunluk zımni garantisi dahi verilmez.

> Bu Türkçe çeviri kolaylık amaçlıdır. Hukuki olarak bağlayıcı olan, `LICENSE` dosyasındaki
> orijinal İngilizce GPL-3.0 metnidir.
