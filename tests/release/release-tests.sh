#!/bin/bash
# Tests de tools/release.sh sans GitHub ni vraie clé : copie du dépôt dans /tmp (fichiers suivis, état courant),
# DLL factices, clé Ed25519 jetable, faux `gh` qui journalise, remote origin = dépôt nu local.
# Usage : tests/release/release-tests.sh   (dossier de travail : /tmp/lmd-reltest/)
# Besoins : vendor/ (sinon make-dist télécharge MelonLoader et LMD-Camera), openssl, python3, zip.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
W=/tmp/lmd-reltest
R="$W/repo"
FAIL=0
check() { if eval "$1"; then echo "OK   $2"; else echo "ÉCHEC $2"; FAIL=1; fi; }

# Fausse Ovomium.dll compilée : rien qu'un attribut MelonInfo (faux, même nom) de la version donnée, que make-dist
# et release.sh lisent pour vérifier la version.
fake_mod() {
    local d="$W/fakemod"
    mkdir -p "$d"
    printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>Ovomium</AssemblyName></PropertyGroup></Project>\n' >"$d/Ovomium.csproj"
    cat >"$d/Fake.cs" <<EOF
[assembly: MelonLoader.MelonInfo(typeof(Fake.Mod), "Ovomium", "$1", "Edia")]
namespace Fake { public class Mod { } }
namespace MelonLoader { [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class MelonInfoAttribute : System.Attribute
    { public MelonInfoAttribute(System.Type t, string name, string version, string author) { } } }
EOF
    dotnet build "$d" -nologo -v quiet -o "$d/out" >"$W/fakemod.log" 2>&1 || { cat "$W/fakemod.log"; exit 1; }
    cp "$d/out/Ovomium.dll" "$R/src/Ovomium/bin/Release/net6.0/Ovomium.dll"
}

setup_repo() {
    rm -rf "$W" && mkdir -p "$W/bin"
    git init -q "$R"
    (cd "$ROOT" && git ls-files -co --exclude-standard -z | xargs -0 tar -cf -) | tar -xf - -C "$R"
    [[ -d "$ROOT/vendor" ]] && cp -r "$ROOT/vendor" "$R/vendor"
    mkdir -p "$R/src/Ovomium/bin/Release/net6.0" "$R/src/Ovomium.Updater/bin/Release/net6.0"
    fake_mod "$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$R/src/Ovomium/OvomiumMod.cs")"
    echo "faux plugin" >"$R/src/Ovomium.Updater/bin/Release/net6.0/OvomiumUpdater.dll"
    openssl genpkey -algorithm ed25519 -out "$W/key.pem" 2>/dev/null
    openssl genpkey -algorithm ed25519 -out "$W/other-key.pem" 2>/dev/null
    PUB=$(openssl pkey -in "$W/key.pem" -pubout -outform DER | tail -c 32 | base64 -w0)
    openssl pkey -in "$W/key.pem" -pubout -out "$W/pub.pem"
    # Le mod doit contenir le dépôt et la clé publique (vérifié par release.sh) ; UpdaterVersion si le plugin manque.
    local consts="const string Repo = \"Delocca/downhill-ovomium\"; const string Key = \"$PUB\";"
    grep -rqs 'UpdaterVersion *=' "$R/src/Ovomium.Updater" --include='*.cs' || consts+=" const int UpdaterVersion = 1;"
    echo "static class ReleaseTest { $consts }" >"$R/src/Ovomium.Updater/ReleaseTest.cs"
    printf '# Journal\n\n## Non publié\n\n- Mise à jour automatique signée.\n\n## 0.0.1 — 2026-01-01\n\n- Avant.\n' \
        >"$R/CHANGELOG.md"
    git -C "$R" add -A && git -C "$R" -c user.name=t -c user.email=t@t commit -qm init
    git init -q --bare "$W/origin.git" && git -C "$R" remote add origin "$W/origin.git"
    cat >"$W/bin/gh" <<'EOF'
#!/bin/bash
echo "gh $*" >>/tmp/lmd-reltest/gh.log
case "$1 ${2:-}" in
    "repo view") echo PRIVATE ;;
    "release create") for a in "$@"; do [[ "$a" == --notes-file ]] && n=1 && continue; [[ -n "${n:-}" ]] && cp "$a" /tmp/lmd-reltest/notes.md && n=; done ;;
esac
EOF
    chmod +x "$W/bin/gh"
    VERSION=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$R/src/Ovomium/OvomiumMod.cs")
}

release() { # release [args] : clé jetable, faux gh ; sortie dans $W/out.txt
    PATH="$W/bin:$PATH" OVOMIUM_RELEASE_KEY="${KEY:-$W/key.pem}" OVOMIUM_PUBLIC_KEY="$PUB" \
        GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@t GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@t \
        bash "$R/tools/release.sh" --no-build "$@" >"$W/out.txt" 2>&1
}

sig_ok() { openssl pkeyutl -verify -rawin -pubin -inkey "$W/pub.pem" -in "$R/dist/ovomium-update.json" \
    -sigfile "$R/dist/ovomium-update.json.sig" >/dev/null 2>&1; }

setup_repo
head0=$(git -C "$R" rev-parse HEAD)

release --dry-run; code=$?
check '[[ $code == 0 ]]' "essai à blanc : code 0 ($code)"
check 'sig_ok && [[ $(stat -c %s "$R/dist/ovomium-update.json.sig") == 64 ]]' 'essai à blanc : signature de 64 octets vérifiée par openssl'
check 'grep -q "Mise à jour automatique signée" "$W/out.txt"' 'essai à blanc : notes tirées de « Non publié »'
check '[[ $(git -C "$R" rev-parse HEAD) == "$head0" && -z $(git -C "$R" tag) && ! -e "$W/gh.log" ]]' 'essai à blanc : ni commit, ni tag, ni gh'
check 'grep -q "^## Non publié" "$R/CHANGELOG.md" && ! grep -q "^## $VERSION" "$R/CHANGELOG.md"' 'essai à blanc : changelog intact'
check 'grep -q "tools/repo-visibility.sh public" "$W/out.txt"' 'essai à blanc : rappel d’ouvrir le dépôt'

KEY="$W/other-key.pem" release --dry-run; code=$?
check '[[ $code != 0 ]] && grep -q "ne correspond pas" "$W/out.txt"' 'autre clé privée : refusée avant signature'

openssl genpkey -algorithm ec -pkeyopt ec_paramgen_curve:P-256 -out "$W/ec-key.pem" 2>/dev/null
echo "pas une clé" >"$W/junk-key.pem"
KEY="$W/ec-key.pem" release --dry-run; code=$?
check '[[ $code != 0 ]] && grep -q "pas une clé privée Ed25519" "$W/out.txt"' 'clé EC (pas Ed25519) : refusée'
KEY="$W/junk-key.pem" release --dry-run; code=$?
check '[[ $code != 0 ]] && grep -q "pas une clé privée Ed25519" "$W/out.txt"' 'fichier de clé illisible : refusé'

# Version augmentée sans recompiler (--no-build) : la DLL se dit encore $VERSION.
bumped=$(python3 -I -c 'import sys; a=sys.argv[1].split("."); a[2]=str(int(a[2])+1); print(".".join(a))' "$VERSION")
python3 -I -c 'import sys; p=sys.argv[1]; s=open(p).read(); open(p,"w").write(s.replace(f"Version = \"{sys.argv[2]}\"", f"Version = \"{sys.argv[3]}\"", 1))' \
    "$R/src/Ovomium/OvomiumMod.cs" "$VERSION" "$bumped"
git -C "$R" -c user.name=t -c user.email=t@t commit -qam "version $bumped"
release --dry-run; code=$?
check '[[ $code != 0 ]] && grep -q "se déclare $VERSION, pas $bumped" "$W/out.txt" && [[ ! -e "$R/dist/downhill-ovomium-$bumped.zip" ]]' \
    'version augmentée sans recompiler : paquet refusé'
git -C "$R" reset -q --hard HEAD~1

IFS=. read -r major minor _ <<<"$VERSION"
git -C "$R" tag "v$major.$((minor + 10)).0" # plus récente, mais avant $VERSION dans l'ordre alphabétique
release --dry-run; code=$?
check '[[ $code != 0 ]] && grep -q "pas plus récente que le dernier tag v$major.$((minor + 10)).0" "$W/out.txt"' \
    'version inférieure au dernier tag (comparaison numérique) : refusée'
git -C "$R" tag -d "v$major.$((minor + 10)).0" >/dev/null
git -C "$R" tag v0.0.1 # plus ancienne : acceptée

echo modif >>"$R/CHANGELOG.md"
release; code=$?
check '[[ $code != 0 ]] && grep -q "pas commitées" "$W/out.txt"' 'arbre modifié : publication refusée'
git -C "$R" checkout -q CHANGELOG.md

release; code=$?
check '[[ $code == 0 ]]' "publication : code 0 ($code)"
check 'sig_ok' 'publication : signature vérifiée par openssl'
check '[[ $(git -C "$W/origin.git" cat-file -t "v$VERSION") == tag ]]' "publication : tag annoté v$VERSION poussé"
check 'git -C "$R" log -1 --format=%s | grep -qx "Changelog $VERSION" && grep -q "^## $VERSION — $(date +%F)" "$R/CHANGELOG.md"' \
    'publication : changelog daté et commité'
check '[[ $(git -C "$W/origin.git" rev-parse "v$VERSION^{commit}") == $(git -C "$R" rev-parse HEAD) ]]' 'publication : tag sur le commit du changelog'
rc="gh release create v$VERSION dist/downhill-ovomium-$VERSION.zip dist/ovomium-update.json dist/ovomium-update.json.sig dist/Ovomium.dll dist/LMD-Camera.dll --repo Delocca/downhill-ovomium"
check 'grep -qF "$rc" "$W/gh.log"' 'publication : release avec les 5 fichiers'
check 'grep -q "Mise à jour automatique signée" "$W/notes.md" && ! grep -q "Avant" "$W/notes.md"' 'publication : notes = section du changelog'
check 'grep -q "privé" "$W/out.txt"' 'publication : rappel, le dépôt est privé'

release; code=$?
check '[[ $code != 0 ]] && grep -q "existe déjà" "$W/out.txt"' 'deuxième publication de la même version : refusée'

git -C "$R" rm -q CHANGELOG.md && git -C "$R" -c user.name=t -c user.email=t@t commit -qm "sans changelog"
git -C "$R" tag -d "v$VERSION" >/dev/null && git -C "$W/origin.git" tag -d "v$VERSION" >/dev/null
release; code=$?
check '[[ $code != 0 && -f "$R/CHANGELOG.md" ]] && grep -q "^## Non publié" "$R/CHANGELOG.md" && grep -q "vide" "$W/out.txt"' \
    'sans CHANGELOG.md : créé avec « Non publié », arrêt'

[[ $FAIL == 0 ]] && echo "TOUS LES TESTS DE PUBLICATION PASSENT" || echo "DES TESTS DE PUBLICATION ÉCHOUENT"
exit $FAIL
