#!/bin/bash
# Paquet de distribution : dist/downhill-ovomium-<version>.zip (installeur Windows, Linux, mod, MelonLoader, LMD-Camera).
#   tools/make-dist.sh            compile le mod (Release) puis emballe
#   tools/make-dist.sh --no-build emballe la DLL déjà compilée
# Besoins : refs/ (pour compiler), vendor/MelonLoader.x64-0.7.3.zip et vendor/LMD-Camera-1.1.0/LMD-Camera.dll
# (téléchargés si absents, empreintes vérifiées).
# Voir docs/distribution.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MELON_VERSION=0.7.3
MELON_ZIP="$ROOT/vendor/MelonLoader.x64-$MELON_VERSION.zip"
MELON_URL="https://github.com/LavaGang/MelonLoader/releases/download/v$MELON_VERSION/MelonLoader.x64.zip"
MELON_SHA256=5b2b2f3d1cd42b59ec886c5bdc2663edae87a0097a4f4a8f58c0965a99dda416
# Mod LMD-Camera de flo-st (vue à la première personne, MIT), livré tel quel.
# Tag 1.1.0 = commit 84785d826359f886e76ab534c9565cf1f97c1ee4 de https://github.com/flo-st/LMD-Camera
CAMERA_VERSION=1.1.0
CAMERA_DLL="$ROOT/vendor/LMD-Camera-$CAMERA_VERSION/LMD-Camera.dll"
CAMERA_URL="https://github.com/flo-st/LMD-Camera/releases/download/$CAMERA_VERSION/LMD-Camera.dll"
CAMERA_SHA256=59ca8f0b191b056f5f4d871a18612a9e8f822303a877cde67df9189c8493b52b
# Build Steam de LMD 1.6.0 avec lequel le mod a été testé (appmanifest_711540.acf, 2026-10-25).
# L'installeur prévient si le jeu installé a un autre build.
TESTED_BUILD=12535418

MOD_VERSION=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$ROOT/src/Ovomium/OvomiumMod.cs")
[[ -n "$MOD_VERSION" ]] || { echo "version du mod introuvable" >&2; exit 1; }
NAME="downhill-ovomium-$MOD_VERSION"
DLL="$ROOT/src/Ovomium/bin/Release/net6.0/Ovomium.dll"

fetch_pinned() { # fichier url sha256 : télécharge si absent, puis vérifie l'empreinte épinglée
    if [[ ! -f "$1" ]]; then
        mkdir -p "$(dirname "$1")"
        curl -fL -o "$1.part" "$2"
        mv "$1.part" "$1"
    fi
    echo "$3  $1" | sha256sum -c --quiet - ||
        { echo "empreinte de $1 inattendue" >&2; exit 1; }
}

build_mod() {
    [[ "${1:-}" == --no-build ]] && return
    dotnet build "$ROOT/src/Ovomium" -c Release -nologo -v quiet
}

# Texte pour Windows : fins de ligne CRLF, BOM UTF-8 si demandé (PowerShell 5.1 lit sinon en ANSI).
to_windows() { # source destination [bom]
    { [[ "${3:-}" == bom ]] && printf '\xEF\xBB\xBF'
      sed -e 's/\r$//' -e 's/$/\r/' \
          -e "s/@MOD_VERSION@/$MOD_VERSION/g" -e "s/@MELON_VERSION@/$MELON_VERSION/g" \
          -e "s/@TESTED_BUILD@/$TESTED_BUILD/g" "$1"; } >"$2"
}

stage() { # dossier de travail → arborescence du zip
    local out="$1/$NAME" src="$ROOT/installer"
    mkdir -p "$out/fichiers/scripts" "$out/fichiers/melonloader"
    to_windows "$src/Installer Ovomium.bat" "$out/Installer Ovomium.bat"
    to_windows "$src/Desinstaller Ovomium.bat" "$out/Desinstaller Ovomium.bat"
    to_windows "$src/LISEZ-MOI.txt" "$out/LISEZ-MOI.txt" bom
    local ps
    for ps in "$src"/scripts/*.ps1; do to_windows "$ps" "$out/fichiers/scripts/$(basename "$ps")" bom; done
    install -m 755 "$src/install-linux.sh" "$out/install-linux.sh"
    cp "$DLL" "$out/fichiers/Ovomium.dll"
    unzip -q "$MELON_ZIP" -d "$out/fichiers/melonloader"
    cp "$out/fichiers/melonloader/MelonLoader/Documentation/LICENSE.md" "$out/fichiers/MelonLoader-LICENSE.md"
    cp "$out/fichiers/melonloader/MelonLoader/Documentation/NOTICE.txt" "$out/fichiers/MelonLoader-NOTICE.txt"
    cp "$CAMERA_DLL" "$out/fichiers/LMD-Camera.dll"
    to_windows "$src/licences/LMD-Camera-LICENSE.txt" "$out/fichiers/LMD-Camera-LICENSE.txt"
}

pack() { # dossier de travail → dist/NAME.zip (ordre et dates fixes : zip reproductible)
    local stamp zip="$ROOT/dist/$NAME.zip"
    stamp=$(git -C "$ROOT" log -1 --format=%cd --date=format:%Y%m%d%H%M.%S)
    find "$1/$NAME" -exec touch -h -t "$stamp" {} +
    mkdir -p "$ROOT/dist"
    (cd "$1" && find "$NAME" -print | LC_ALL=C sort | zip -q -X -@ "$1/$NAME.zip")
    mv -f "$1/$NAME.zip" "$zip"
    echo "$zip"
    sha256sum "$zip" | cut -d' ' -f1
}

main() {
    fetch_pinned "$MELON_ZIP" "$MELON_URL" "$MELON_SHA256"
    fetch_pinned "$CAMERA_DLL" "$CAMERA_URL" "$CAMERA_SHA256"
    build_mod "${1:-}"
    [[ -f "$DLL" ]] || { echo "DLL absente : $DLL" >&2; exit 1; }
    WORK=$(mktemp -d /tmp/lmd-dist.XXXXXX)
    trap 'rm -rf "$WORK"' EXIT
    stage "$WORK"
    pack "$WORK"
}

main "$@"
