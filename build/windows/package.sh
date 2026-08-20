#!/usr/bin/env bash
#
# Windows packaging (P10-T15, P10-T16): portable ZIP + Inno Setup script.
#
# Usage:
#   build/windows/package.sh
#   MINVER_VERSION_OVERRIDE=1.0.0-test build/windows/package.sh
#
# Cross-compiled from Linux — no Windows machine needed. But it is NOT RUN: the
# resulting .exe can't be tested on this machine. The README should say so honestly
# (P10-T25).
#
# ⚠️ MEASURED — `zip` is not installed on this machine. Python's zipfile module is
# used instead: python3 is already likely present on any .NET SDK install, and
# adding an extra dependency would make the release pipeline more fragile.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# shellcheck source=../version.sh
. "$ROOT/build/version.sh"

VERSION="$(gitext_require_releasable_version)"

RID="${RID:-win-x64}"
OUT="$ROOT/dist"
STAGE="$OUT/$RID/gitext-core"

# ⚠️ The Inno Setup script needs WINDOWS paths. Under Git Bash `pwd` yields an MSYS path
# (/d/a/...), and Inno cannot read it — it treats the value as relative and glues it onto
# OutputDir, producing the nonsense the compiler reported:
#   Could not read "D:\a\...\dist\/d/a/.../win-x64/gitext-core\LICENSE"
# Everything else in this script keeps using the MSYS form, which is what the shell and
# `dotnet publish` want; only the values embedded into the .iss are converted.
to_windows_path() {
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        # No cygpath (running on Linux for a local trial): /d/a/x -> D:\a\x
        printf '%s' "$1" | sed -E 's|^/([a-zA-Z])/|\U\1:/|; s|/|\\|g'
    fi
}

STAGE_WIN="$(to_windows_path "$STAGE")"

echo "== gitext-core $VERSION ($RID)"

rm -rf "$OUT/$RID"
mkdir -p "$STAGE"

# The icon is embedded into the Windows executable; it must be generated BEFORE publish.
echo "== icons"
build/icons/generate.sh "$OUT/icons"

# 🔴 IncludeNativeLibrariesForSelfExtract is REQUIRED alongside PublishSingleFile. MEASURED on
# the v0.1.0 release: without it .NET leaves the native libraries (libSkiaSharp, libHarfBuzzSharp)
# NEXT TO the binary instead of embedding them, so "single file" was not single at all. install.sh
# copies only the binary, and the installed application died at startup with
#   DllNotFoundException: Unable to load shared library 'libSkiaSharp'
# Verified after the fix: the binary is alone in its directory and runs from an isolated directory
# with no libraries beside it.
echo "== publish (self-contained, single file)"
dotnet publish src/GitExt.Desktop \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:MinVerVersionOverride="$VERSION" \
    -o "$STAGE"

rm -f "$STAGE"/*.pdb

cp LICENSE "$STAGE/"
cp README.md "$STAGE/"

# ⚠️ Version verification CANNOT be done the way it is on the Linux side:
# gitext-core.exe can't be run on this machine. Instead the version resource
# embedded in the PE file is read — answering the same question via a different
# path: is the version in the package's name the same as the one inside the binary?
if grep -aq "$VERSION" "$STAGE/gitext-core.exe"; then
    echo "   version found in binary: $VERSION"
else
    echo "!! VERSION MISMATCH: '$VERSION' not found inside gitext-core.exe." >&2
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

# Inno Setup runs on Windows (or under Wine); here we only generate the script.
# It's compiled on CI's Windows runner.
ISS="$OUT/gitext-core.iss"

cat > "$ISS" <<EOF
; gitext-core installer script (P10-T16) — GENERATED FILE, do not edit by hand.
; Source: build/windows/package.sh

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
LicenseFile=$STAGE_WIN\\LICENSE
OutputDir=.
OutputBaseFilename=gitext-core-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Admin rights are NOT REQUIRED: the user can install to their own profile. Requiring
; admin would make the install unnecessarily harder and is blocked in most corporate environments.
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add gitext-core to PATH"; GroupDescription: "Integration:"; Flags: unchecked

[Files]
Source: "$STAGE_WIN\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\{#AppName}"; Filename: "{app}\\{#AppExe}"
Name: "{autodesktop}\\{#AppName}"; Filename: "{app}\\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \\
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Adding the same path to PATH twice leaves garbage after uninstall and bloats PATH.
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

// git is a RUNTIME dependency (ADR-0002). The installer doesn't bring it along; it's
// better to tell the user NOW than have them find out when they first open the app.
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

echo "== Inno Setup script"
echo "   $ISS"
echo "   (compile on Windows: iscc gitext-core.iss)"
