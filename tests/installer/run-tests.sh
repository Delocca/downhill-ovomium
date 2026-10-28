#!/bin/bash
# Tests de l'installeur (sans Windows) : emballe dist/, l'extrait dans /tmp, puis
#  1. analyse PSScriptAnalyzer (syntaxe compatible Windows PowerShell 5.1) ;
#  2. installeur/désinstalleur PowerShell sous pwsh (conteneur podman), fausse arborescence Steam ;
#  3. install-linux.sh avec une fausse arborescence Steam/Proton ;
#  4. paquet : LMD-Camera livré tel quel avec sa licence, plugin de mise à jour, manifeste, zip reproductible.
# La chaîne de publication signée (tools/release.sh) a ses propres tests : tests/release/release-tests.sh.
# Usage : tests/installer/run-tests.sh [--no-build]   (journal : /tmp/lmd-insttest/)
# Le registre, les fenêtres, le pare-feu et l'UAC ne sont testables que sur un vrai Windows.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$ROOT/tests/installer"
T=/tmp/lmd-insttest
IMAGE=mcr.microsoft.com/powershell:latest

rm -rf "$T" && mkdir -p "$T"

build_dist() {
    bash "$ROOT/tools/make-dist.sh" "${1:-}" | tail -n 2 >"$T/dist1.txt"
    ZIP=$(head -n1 "$T/dist1.txt")
    unzip -q "$ZIP" -d "$T/dist"
    DIST=$(find "$T/dist" -mindepth 1 -maxdepth 1 -type d)
}

# Fausses DLL MelonLoader d'autres versions (FileVersion lue par l'installeur).
build_fake_melons() {
    local v
    for v in 0.6.1 0.8.0; do
        mkdir -p "$T/fake-$v"
        cat >"$T/fake-$v/MelonLoader.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><FileVersion>$v.0</FileVersion>
  <AssemblyName>MelonLoader</AssemblyName><OutputPath>out</OutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup>
</Project>
EOF
        echo 'namespace Fake { public class C {} }' >"$T/fake-$v/C.cs"
        dotnet build "$T/fake-$v" -nologo -v quiet >/dev/null
        cp "$T/fake-$v/out/MelonLoader.dll" "$T/MelonLoader-$v.dll"
    done
}

run_pwsh() {
    podman run --rm -v /tmp:/tmp:z -e PSModulePath=/tmp/lmd-psmodules "$IMAGE" pwsh -NoProfile -File "$@"
}

ensure_analyzer() { # 1.22.0 : les suivantes exigent pwsh ≥ 7.4.6
    [[ -d /tmp/lmd-psmodules/PSScriptAnalyzer/1.22.0 ]] && return
    rm -rf /tmp/lmd-psmodules && mkdir -p /tmp/lmd-psmodules
    podman run --rm -v /tmp:/tmp:z "$IMAGE" pwsh -NoProfile -c \
        'Save-Module -Name PSScriptAnalyzer -RequiredVersion 1.22.0 -Path /tmp/lmd-psmodules -Repository PSGallery'
}

check_camera_payload() { # LMD-Camera livré tel quel (même fichier que vendor/) avec sa licence
    if cmp -s "$DIST/fichiers/LMD-Camera.dll" "$ROOT/vendor/LMD-Camera-1.1.0/LMD-Camera.dll" &&
        grep -q 'MIT License' "$DIST/fichiers/LMD-Camera-LICENSE.txt"; then
        echo "OK   LMD-Camera.dll tel quel + licence MIT dans fichiers/"
    else
        echo "ÉCHEC LMD-Camera.dll ou sa licence absents/modifiés dans fichiers/"; return 1
    fi
}

# Mise à jour automatique : plugin dans le paquet ; manifeste compact dont les SHA-256 sont ceux des DLL nues de dist/,
# elles-mêmes identiques à celles du paquet.
check_update_files() {
    local d="$ROOT/dist"
    cp "$d/ovomium-update.json" "$T/manifest1.json"
    if [[ -f "$DIST/fichiers/OvomiumUpdater.dll" ]] && cmp -s "$d/Ovomium.dll" "$DIST/fichiers/Ovomium.dll" &&
        cmp -s "$d/LMD-Camera.dll" "$DIST/fichiers/LMD-Camera.dll" &&
        python3 -I - "$d" "$(basename "$DIST")" <<'EOF'
import hashlib, json, sys
folder, name = sys.argv[1:]
raw = open(f"{folder}/ovomium-update.json", "rb").read()
m = json.loads(raw)
assert raw == json.dumps(m, separators=(",", ":"), ensure_ascii=False).encode(), "pas compact"
assert list(m) == ["version", "minUpdater", "files"] and isinstance(m["minUpdater"], int) and m["minUpdater"] >= 1
assert name == f"downhill-ovomium-{m['version']}", "version différente du zip"
assert [(f["name"], f["dest"]) for f in m["files"]] == [("Ovomium.dll", "Mods"), ("LMD-Camera.dll", "Mods")]
for f in m["files"]:
    assert f["sha256"] == hashlib.sha256(open(f"{folder}/{f['name']}", "rb").read()).hexdigest(), f["name"]
EOF
    then
        echo "OK   plugin de mise à jour dans fichiers/, manifeste et DLL nues cohérents ($(cat "$T/manifest1.json"))"
    else
        echo "ÉCHEC plugin de mise à jour, manifeste ou DLL nues de dist/"; return 1
    fi
}

check_reproducible() {
    bash "$ROOT/tools/make-dist.sh" --no-build | tail -n 2 >"$T/dist2.txt"
    if [[ "$(tail -n1 "$T/dist1.txt")" == "$(tail -n1 "$T/dist2.txt")" ]] &&
        cmp -s "$T/manifest1.json" "$ROOT/dist/ovomium-update.json"; then
        echo "OK   zip et manifeste reproductibles ($(tail -n1 "$T/dist2.txt"))"
    else
        echo "ÉCHEC zip ou manifeste non reproductible"; return 1
    fi
}

main() {
    build_dist "${1:-}"
    build_fake_melons
    cp "$HERE/InstallerTests.ps1" "$HERE/Analyze.ps1" "$T/"
    ensure_analyzer
    local fail=0
    echo "== Analyse (PS 5.1) =="; run_pwsh "$T/Analyze.ps1" "$DIST/fichiers/scripts" || fail=1
    echo "== Installeur PowerShell =="; run_pwsh "$T/InstallerTests.ps1" "$DIST" "$T" || fail=1
    echo "== install-linux.sh =="; bash "$HERE/linux-tests.sh" "$DIST" "$T/linux" || fail=1
    echo "== Paquet =="; check_camera_payload || fail=1; check_update_files || fail=1; check_reproducible || fail=1
    [[ $fail == 0 ]] && echo "TOUS LES TESTS PASSENT" || echo "DES TESTS ÉCHOUENT"
    return $fail
}

main "$@"
