#!/bin/bash
# Mise à jour automatique de bout en bout, hors jeu, sur les vrais binaires et la vraie chaîne de publication :
# copie du dépôt dans /tmp avec une clé Ed25519 jetable (clé publique du mod remplacée dans la copie), faux jeu
# installé par install-linux.sh à la version courante, version +1 publiée par `tools/release.sh --dry-run`, flux de
# releases GitHub factice en file://, puis le code des DLL installées (harnais Harness/, par réflexion) :
# mod (téléchargement, vérification, update/) puis plugin (installation dans Mods/), et les refus attendus.
# Usage : tests/update-e2e/run-tests.sh   (dossier de travail : /tmp/lmd-e2etest/)
# Besoins : refs/ (compilation), vendor/ (sinon téléchargé), dotnet, openssl, python3, zip.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
W=/tmp/lmd-e2etest
R="$W/repo"
GAME="$W/steam/steamapps/common/Lonely Mountains Downhill"
DATA="$GAME/UserData/Ovomium"
FAIL=0
check() { if eval "$1"; then echo "OK   $2"; else echo "ÉCHEC $2"; FAIL=1; fi; }
die() { echo "ERREUR : $*" >&2; exit 1; }
H() { OVOMIUM_REFS="$R/refs" dotnet "$W/harness/Harness.dll" "$@"; }
commit() { git -C "$R" add -A && git -C "$R" -c user.name=t -c user.email=t@t commit -qm "$1"; }

# Copie du dépôt (fichiers suivis, état courant) dont le mod et le plugin font confiance à une clé jetable.
setup_repo() {
    [[ -d "$ROOT/refs" ]] || die "refs/ absent : il faut les références du jeu pour compiler le mod."
    rm -rf "$W" && mkdir -p "$R"
    (cd "$ROOT" && git ls-files -co --exclude-standard -z | xargs -0 tar -cf -) | tar -xf - -C "$R"
    cp -r "$ROOT/refs" "$R/refs"
    [[ -d "$ROOT/vendor" ]] && cp -r "$ROOT/vendor" "$R/vendor"
    openssl genpkey -algorithm ed25519 -out "$W/key.pem" 2>/dev/null
    PUB=$(openssl pkey -in "$W/key.pem" -pubout -outform DER | tail -c 32 | base64 -w0)
    python3 -I - "$R/src/Ovomium.Core/Update/UpdatePackage.cs" "$PUB" <<'EOF' || die "clé publique du mod introuvable"
import re, sys
p, pub = sys.argv[1:]
src = open(p, encoding="utf-8").read()
new, n = re.subn(r'(ReleaseKey = Convert\.FromBase64String\(")[^"]+', lambda m: m.group(1) + pub, src)
if n != 1: sys.exit(1)
open(p, "w", encoding="utf-8").write(new)
EOF
    git init -q "$R" && commit base
    OLD=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$R/src/Ovomium/OvomiumMod.cs")
    NEW=$(python3 -I -c 'import sys; a=sys.argv[1].split("."); a[2]=str(int(a[2])+1); print(".".join(a))' "$OLD")
}

# Faux jeu Steam/Proton, Ovomium $OLD installé par le vrai install-linux.sh du paquet ; copie de référence gardée.
install_old() {
    bash "$R/tools/make-dist.sh" >"$W/dist-old.txt" 2>&1 || die "make-dist ($OLD) : voir $W/dist-old.txt"
    mkdir -p "$W/old" && unzip -q "$R/dist/downhill-ovomium-$OLD.zip" -d "$W/old"
    local s="$W/steam/steamapps"
    mkdir -p "$GAME" "$s/compatdata/711540/pfx"
    printf '"AppState"\n{\n\t"appid"\t\t"711540"\n\t"installdir"\t\t"Lonely Mountains Downhill"\n}\n' >"$s/appmanifest_711540.acf"
    echo exe >"$GAME/LMD_Win_x64.exe"
    printf 'WINE REGISTRY Version 2\n\n' >"$s/compatdata/711540/pfx/user.reg"
    OVOMIUM_STEAM_ROOTS="$W/steam" OVOMIUM_SKIP_RUNNING_CHECK=1 OVOMIUM_ANSWER=o OVOMIUM_NO_TRASH=1 OVOMIUM_UFW_CONF=/dev/null \
        bash "$W/old/downhill-ovomium-$OLD/install-linux.sh" >"$W/install.txt" 2>&1 || die "install-linux.sh : voir $W/install.txt"
    cp -a "$GAME" "$W/game-old"
    # Paquet $OLD authentique (signé), pour les rejeux.
    mkdir -p "$W/assets-old" && cp "$R/dist"/{ovomium-update.json,Ovomium.dll,LMD-Camera.dll} "$W/assets-old/"
    openssl pkeyutl -sign -rawin -inkey "$W/key.pem" -in "$W/assets-old/ovomium-update.json" -out "$W/assets-old/ovomium-update.json.sig"
}

# Version +1 commitée dans la copie, publiée à blanc (paquet compilé, manifeste signé avec la clé jetable).
release_new() {
    python3 -I - "$R/src/Ovomium/OvomiumMod.cs" "$OLD" "$NEW" <<'EOF'
import sys
p, old, new = sys.argv[1:]
s = open(p, encoding="utf-8").read()
open(p, "w", encoding="utf-8").write(s.replace(f'const string Version = "{old}";', f'const string Version = "{new}";', 1))
EOF
    printf '# Journal\n\n## Non publié\n\n- Version de test.\n' >"$R/CHANGELOG.md"
    commit "$NEW"
    OVOMIUM_RELEASE_KEY="$W/key.pem" OVOMIUM_PUBLIC_KEY="$PUB" bash "$R/tools/release.sh" --dry-run >"$W/release.txt" 2>&1
    RELEASE_CODE=$?
    mkdir -p "$W/assets" && cp "$R/dist"/{ovomium-update.json,ovomium-update.json.sig,Ovomium.dll,LMD-Camera.dll} "$W/assets/"
}

feed() { # feed nom tag:dossier… : liste de releases au format de l'API GitHub, fichiers en file://
    python3 -I - "$W/$1.json" "${@:2}" <<'EOF'
import json, os, sys
out, *rels = sys.argv[1:]
feed = [{"tag_name": t, "draft": False, "prerelease": False, "body": "- test",
         "assets": [{"name": n, "browser_download_url": "file://" + os.path.join(d, n)} for n in sorted(os.listdir(d))]}
        for t, d in (r.split(":", 1) for r in rels)]
json.dump(feed, open(out, "w"))
EOF
    echo "file://$W/$1.json"
}

reset_game() { rm -rf "$GAME" && cp -a "${1:-$W/game-old}" "$GAME"; }
stage() { rm -rf "$DATA/update" && mkdir -p "$DATA/update" && cp "$1"/* "$DATA/update/"; } # paquet posé sans le mod
mods_version() { H version "$GAME/Mods/Ovomium.dll"; }
mods_untouched() { cmp -s "$GAME/Mods/Ovomium.dll" "$1/Mods/Ovomium.dll"; }
state_is() { grep -q "\"result\":\"$1\"" "$DATA/update-state.json" 2>/dev/null; }
tampered() { # tampered nom fichier : copie des assets, un octet du fichier changé
    rm -rf "$W/$1" && cp -r "$W/assets" "$W/$1"
    python3 -I -c 'import sys; p=sys.argv[1]; b=bytearray(open(p,"rb").read()); i=b.find(b"\"sha256\":\"")+10 if p.endswith(".json") else len(b)//2; b[i]^=1; open(p,"wb").write(b)' "$W/$1/$2"
}

test_update() { # le scénario normal : téléchargement par le mod, installation par le plugin au lancement suivant
    local url out
    url=$(feed feed "v$NEW:$W/assets" "v$OLD:$W/assets-old")
    out=$(H mod "$GAME" "$url")
    check '[[ $(cat "$DATA/installed.json") == "{\"version\":\"$OLD\"}" ]]' "mod $OLD : installed.json écrit au démarrage"
    check 'grep -q "Ready : $NEW" <<<"$out" && cmp -s "$DATA/update/Ovomium.dll" "$W/assets/Ovomium.dll" && [[ ! -e "$DATA/update.tmp" ]]' \
        "mod $OLD : $NEW téléchargée, vérifiée, prête dans update/"
    check 'mods_untouched "$W/game-old"' 'mod : Mods/ pas touché avant le redémarrage'
    out=$(H plugin "$GAME")
    check 'grep -q "installed $NEW" <<<"$out" && state_is installed && [[ ! -e "$DATA/update" ]]' "plugin : $NEW installée, update-state.json = installed"
    check '[[ $(mods_version) == "$NEW" ]] && cmp -s "$GAME/Mods/Ovomium.dll" "$W/assets/Ovomium.dll"' "Mods/Ovomium.dll remplacée, MelonInfo = $NEW"
    check 'cmp -s "$GAME/Mods/LMD-Camera.dll" "$W/assets/LMD-Camera.dll" && [[ -z $(find "$GAME/Mods" -name "*.old" -o -name "*.new") ]]' \
        'LMD-Camera.dll en place, ni .old ni .new restants'
    check '[[ $(cat "$DATA/installed.json") == "{\"version\":\"$NEW\"}" ]]' "plugin : installed.json = $NEW"
    rm -f "$DATA/update-state.json" # lu puis effacé par le mod (AutoUpdate.ReportLastInstall)
    cp -a "$GAME" "$W/game-new"
    out=$(H mod "$GAME" "$url"); H plugin "$GAME" >/dev/null
    check 'grep -q "$NEW est la dernière version" <<<"$out" && [[ ! -e "$DATA/update-state.json" ]]' "mod $NEW relancé : rien à faire"
}

test_refusals() { # vrais artefacts altérés ou rejoués : refusés, Mods/ intact
    local out
    tampered assets-badman ovomium-update.json
    reset_game; out=$(H mod "$GAME" "$(feed feed-badman "v$NEW:$W/assets-badman")")
    check 'grep -q "signature invalide" <<<"$out" && [[ ! -e "$DATA/update" ]]' 'mod : manifeste modifié d’un octet, refusé'
    reset_game; stage "$W/assets-badman"; out=$(H plugin "$GAME")
    check 'grep -q "rejected.*signature invalide" <<<"$out" && state_is rejected && [[ -d "$DATA/update.rejected" ]] && mods_untouched "$W/game-old"' \
        'plugin : manifeste modifié d’un octet, refusé, quarantaine, Mods/ intact'
    tampered assets-baddll Ovomium.dll
    reset_game; out=$(H mod "$GAME" "$(feed feed-baddll "v$NEW:$W/assets-baddll")")
    check 'grep -q "empreinte fausse pour Ovomium.dll" <<<"$out" && [[ ! -e "$DATA/update" ]]' 'mod : Ovomium.dll modifiée, refusée'
    reset_game; stage "$W/assets-baddll"; out=$(H plugin "$GAME")
    check 'grep -q "rejected.*empreinte fausse" <<<"$out" && state_is rejected && mods_untouched "$W/game-old"' 'plugin : Ovomium.dll modifiée, refusée, Mods/ intact'
    reset_game "$W/game-new"; stage "$W/assets-old"; out=$(H plugin "$GAME")
    check 'grep -q "rejected $OLD.*pas plus récente" <<<"$out" && mods_untouched "$W/game-new"' "plugin : paquet $OLD signé rejoué sur $NEW, refusé"
    reset_game; stage "$W/assets-old"; out=$(H plugin "$GAME")
    check 'grep -q "rejected $OLD.*pas plus récente" <<<"$out" && mods_untouched "$W/game-old"' "plugin : paquet $OLD signé rejoué sur $OLD, refusé"
    reset_game; out=$(H mod "$GAME" "$(feed feed-tag "v9.9.9:$W/assets")")
    check 'grep -q "le manifeste annonce $NEW" <<<"$out" && [[ ! -e "$DATA/update" ]]' 'mod : tag de release qui ne correspond pas au manifeste, refusé'
}

setup_repo
dotnet build "$ROOT/tests/update-e2e/Harness" -nologo -v quiet -o "$W/harness" >"$W/harness.txt" 2>&1 || die "harnais : voir $W/harness.txt"
install_old
check '[[ $(mods_version) == "$OLD" && -f "$GAME/Plugins/OvomiumUpdater.dll" ]]' "jeu factice : Ovomium $OLD et plugin installés par install-linux.sh"
release_new
check '[[ $RELEASE_CODE == 0 ]] && grep -q "\"version\":\"$NEW\"" "$W/assets/ovomium-update.json"' "release.sh --dry-run : $NEW emballée et signée (voir $W/release.txt)"
test_update
test_refusals

if [[ $FAIL == 0 ]]; then echo "TOUS LES TESTS DE BOUT EN BOUT PASSENT"; else echo "DES TESTS DE BOUT EN BOUT ÉCHOUENT"; exit 1; fi
