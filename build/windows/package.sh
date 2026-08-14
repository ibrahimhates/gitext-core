#!/usr/bin/env bash
#
# Windows paketleme (P10-T15, P10-T16): taşınabilir ZIP + Inno Setup betiği.
#
# Kullanım:
#   build/windows/package.sh
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/windows/package.sh
#
# Linux'tan çapraz derleniyor — Windows makinesi gerekmiyor. Ama ÇALIŞTIRILMIYOR:
# üretilen .exe bu makinede test edilemez. README bunu dürüstçe söylemeli (P10-T25).
#
# ⚠️ ÖLÇÜLDÜ — `zip` bu makinede kurulu değil. Python'un zipfile modülü kullanılıyor:
# her .NET SDK kurulumunda zaten python3 bulunma ihtimali yüksek ve ek bağımlılık
# eklemek yayın hattını kırılganlaştırırdı.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

VERSION="$(gitext_require_releasable_version)"

RID="${RID:-win-x64}"
OUT="$ROOT/dist"
STAGE="$OUT/$RID/gitext-core"

echo "== gitext-core $VERSION ($RID)"

rm -rf "$OUT/$RID"
mkdir -p "$STAGE"

# İkon Windows çalıştırılabilirine gömülüyor; publish'ten ÖNCE üretilmeli.
echo "== ikonlar"
build/icons/generate.sh "$OUT/icons" >/dev/null

echo "== yayın (self-contained, tek dosya)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$STAGE"

rm -f "$STAGE"/*.pdb

cp LICENSE "$STAGE/"
cp README.md "$STAGE/"

# ⚠️ Sürüm doğrulaması Linux tarafındaki gibi YAPILAMIYOR: gitext-core.exe bu makinede
# çalıştırılamaz. Bunun yerine PE dosyasına gömülen sürüm kaynağı okunuyor — aynı
# soruyu farklı bir yoldan yanıtlıyor: paketin adı ile ikilinin içindeki sürüm aynı mı?
if grep -aq "$VERSION" "$STAGE/gitext-core.exe"; then
    echo "   sürüm ikilide bulundu: $VERSION"
else
    echo "!! SÜRÜM UYUŞMAZLIĞI: '$VERSION' gitext-core.exe içinde bulunamadı." >&2
    exit 1
fi

echo "== ZIP"
ZIP="$OUT/gitext-core-$VERSION-$RID.zip"
rm -f "$ZIP"

python3 - "$ZIP" "$OUT/$RID" <<'PY'
import os, sys, zipfile

target, root = sys.argv[1], sys.argv[2]

with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for directory, _, files in os.walk(root):
        for name in files:
            full = os.path.join(directory, name)
            archive.write(full, os.path.relpath(full, root))
PY

echo "   $ZIP ($(du -h "$ZIP" | cut -f1))"

# ---------------------------------------------------------------- Inno Setup

# Inno Setup Windows'ta (veya Wine altında) çalışıyor; burada yalnızca betiği
# üretiyoruz. Derlemesi CI'ın Windows runner'ında yapılıyor.
ISS="$OUT/gitext-core.iss"

cat > "$ISS" <<EOF
; gitext-core kurulum betiği (P10-T16) — ÜRETİLMİŞ DOSYA, elle düzenlemeyin.
; Kaynak: build/windows/package.sh

#define AppName "gitext-core"
#define AppVersion "$VERSION"
#define AppPublisher "gitext-core contributors"
#define AppURL "https://github.com/ibrahimhates/gitext-core"
#define AppExe "gitext-core.exe"

[Setup]
AppId={{8F3C1A94-6E52-4B7D-9A16-2C5D8E4F0B31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
DefaultDirName={autopf}\\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=$STAGE\\LICENSE
OutputDir=.
OutputBaseFilename=gitext-core-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Yönetici hakkı GEREKMİYOR: kullanıcı kendi profiline kurabiliyor. Yönetici istemek,
; kurulumu gereksiz yere zorlaştırır ve çoğu kurumsal ortamda engellenir.
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add gitext-core to PATH"; GroupDescription: "Integration:"; Flags: unchecked

[Files]
Source: "$STAGE\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\{#AppName}"; Filename: "{app}\\{#AppExe}"
Name: "{autodesktop}\\{#AppName}"; Filename: "{app}\\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \\
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// PATH'e aynı yolu iki kez eklemek, kaldırma sonrası çöp bırakır ve PATH'i şişirir.
function NeedsAddPath(Param: string): Boolean;
var
  OldPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OldPath) then
  begin
    Result := True;
    exit;
  end;

  Result := Pos(';' + ExpandConstant(Param) + ';', ';' + OldPath + ';') = 0;
end;

// git bir ÇALIŞMA ZAMANI bağımlılığı (ADR-0002). Kurulum onu getirmiyor; kullanıcıya
// ŞİMDİ söylenmesi, uygulamayı ilk açtığında öğrenmesinden iyi.
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if not Exec('cmd.exe', '/c where git', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    if MsgBox('Git was not found on this system.' + #13#10#13#10 +
              'gitext-core runs the real git command line, so it needs Git for Windows ' +
              '(https://git-scm.com/download/win) to be installed.' + #13#10#13#10 +
              'Continue with the installation anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
EOF

echo "== Inno Setup betiği"
echo "   $ISS"
echo "   (derleme Windows'ta: iscc gitext-core.iss)"
