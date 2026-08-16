# Homebrew cask (P10-T23).
#
# GENERATED TEMPLATE: the version and the sha256 are filled in during the release.
#
# ⚠️ OUR OWN TAP, NOT homebrew-cask (the P10-T23 decision). homebrew-cask does not accept
# unsigned and un-notarized applications; notarization requires a paid Apple Developer
# account, which was not obtained (P10-T22).
#
# Installation:
#   brew tap ibrahimhates/tap
#   brew install --cask gitext-core

cask "gitext-core" do
  version "0.0.0"

  # Apple Silicon and Intel are built separately; Homebrew picks the right one.
  # Producing a universal binary would mean joining the two with `lipo`, which can only be
  # done on macOS — and all it buys is a single file.
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

  # git is a RUNTIME dependency (ADR-0002): the application runs git as a subprocess.
  # On macOS the git that comes with the Xcode Command Line Tools is enough.
  depends_on formula: "git"
  depends_on macos: ">= :monterey"

  app "gitext-core.app"

  # 🔴 THE QUARANTINE ATTRIBUTE IS REMOVED (P10-T22). Because the application is not
  # notarized, Gatekeeper blocks it as "damaged" — the message is misleading, the file is not
  # damaged, only unsigned. Without this line the user has to run
  # `xattr -dr com.apple.quarantine` by hand, and most users will not; they conclude the
  # application is broken. Homebrew does this with the user's explicit consent to install.
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
