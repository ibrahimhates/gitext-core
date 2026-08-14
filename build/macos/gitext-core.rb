# Homebrew cask (P10-T23).
#
# ÜRETİLMİŞ ŞABLON: sürüm ve sha256 yayın sırasında doldurulur.
#
# ⚠️ KENDİ TAP'İMİZ, homebrew-cask DEĞİL (P10-T23 kararı). homebrew-cask imzasız ve
# notarize edilmemiş uygulamaları kabul etmiyor; notarization ücretli bir Apple
# Developer hesabı gerektiriyor ve alınmadı (P10-T22).
#
# Kurulum:
#   brew tap ibrahimhates/tap
#   brew install --cask gitext-core

cask "gitext-core" do
  version "0.0.0"

  # Apple Silicon ve Intel ayrı derleniyor; Homebrew doğru olanı seçiyor.
  # Universal binary üretmek iki ikiliyi `lipo` ile birleştirmeyi gerektiriyor ve
  # bu yalnızca macOS'ta yapılabiliyor — kazancı da yalnızca tek dosya olması.
  on_arm do
    sha256 "0000000000000000000000000000000000000000000000000000000000000000"
    url "https://github.com/ibrahimhates/gitext-core/releases/download/v#{version}/gitext-core-#{version}-osx-arm64.dmg"
  end

  on_intel do
    sha256 "0000000000000000000000000000000000000000000000000000000000000000"
    url "https://github.com/ibrahimhates/gitext-core/releases/download/v#{version}/gitext-core-#{version}-osx-x64.dmg"
  end

  name "gitext-core"
  desc "Fast native Git GUI"
  homepage "https://github.com/ibrahimhates/gitext-core"

  # git bir ÇALIŞMA ZAMANI bağımlılığı (ADR-0002): uygulama git'i alt süreç olarak
  # çalıştırıyor. macOS'ta Xcode Command Line Tools ile gelen git de yeterli.
  depends_on formula: "git"
  depends_on macos: ">= :monterey"

  app "gitext-core.app"

  # 🔴 KARANTİNA ÖZNİTELİĞİ KALDIRILIYOR (P10-T22). Uygulama notarize edilmediği için
  # Gatekeeper onu "hasarlı" diye engelliyor — mesaj yanıltıcı, dosya hasarlı değil,
  # yalnızca imzasız. Bu satır olmadan kullanıcı elle `xattr -dr com.apple.quarantine`
  # çalıştırmak zorunda kalır ve çoğu kullanıcı bunu yapmaz, uygulamanın bozuk olduğunu
  # düşünür. Homebrew bu işlemi kullanıcının açık kurulum onayıyla yapıyor.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/gitext-core.app"],
                   sudo: false
  end

  zap trash: [
    "~/Library/Application Support/gitext-core",
    "~/Library/Preferences/io.github.ibrahimhates.GitExtCore.plist",
    "~/Library/Saved Application State/io.github.ibrahimhates.GitExtCore.savedState",
  ]

  caveats <<~EOS
    gitext-core is not notarized by Apple.

    macOS will therefore refuse to open it on first launch unless the quarantine
    attribute is removed — this cask does that for you during installation.

    Notarization requires a paid Apple Developer account, which this project does not
    have. Verify your download against the SHA256SUMS file published with each release:
      https://github.com/ibrahimhates/gitext-core/releases
  EOS
end
