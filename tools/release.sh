#!/bin/bash
# Publie une mise à jour d'Ovomium : release GitHub v<version> que le plugin de mise à jour des joueurs télécharge,
# après en avoir vérifié la signature Ed25519. Version = OvomiumMod.Version (src/Ovomium/OvomiumMod.cs).
# Usage : tools/release.sh [--dry-run] [--no-build] [--notes "Texte" | notes.md]
#   sans notes  section « Non publié » de CHANGELOG.md, datée et renommée en <version> (commit « Changelog <version> »)
#   --dry-run   essai à blanc : vérifications, paquet, signature vérifiée, mais ni commit, ni tag, ni push, ni gh
#   --no-build  emballe les DLL déjà compilées
# Prérequis : arbre git propre, remote origin, `gh auth login` fait, tag v<version> inexistant, clé privée de
# signature dans ~/.config/ovomium/release-key.pem. À lancer hors bac à sable. Pas à pas : docs/distribution.md.
# Variables de test : OVOMIUM_RELEASE_KEY (chemin de la clé privée), OVOMIUM_PUBLIC_KEY (clé publique attendue).
set -euo pipefail

# Même dépôt que la constante du mod (« Delocca/downhill-ovomium » dans src/) et que tools/repo-visibility.sh.
GITHUB_REPO="Delocca/downhill-ovomium"
# Clé publique de signature (Ed25519, 32 octets en base64) : la même que celle intégrée au mod.
PUBLIC_KEY="${OVOMIUM_PUBLIC_KEY:-dseNI/klJpWEjY/3pE+HWOROQNvyUqUIdLyUld81pF8=}"
KEY="${OVOMIUM_RELEASE_KEY:-$HOME/.config/ovomium/release-key.pem}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
MANIFEST=dist/ovomium-update.json
CHANGELOG=CHANGELOG.md
UNRELEASED="## Non publié"
DRY_RUN="" BUILD_ARG="" NOTES_TEXT="" NOTES_SOURCE=changelog
TMP=$(mktemp -d /tmp/ovomium-release.XXXXXX)
trap 'rm -rf "$TMP"' EXIT

say() { printf '%s\n' "$@"; }
step() { printf '\n=== %s ===\n' "$*"; }
die() { printf '\nERREUR : %s\n' "$*" >&2; exit 1; }
# Bloquant pour une vraie publication, simple avertissement en essai à blanc.
blocker() { if [[ -n "$DRY_RUN" ]]; then say "  (essai à blanc, ignoré) $*"; else die "$*"; fi; }

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --dry-run) DRY_RUN=1 ;;
            --no-build) BUILD_ARG=--no-build ;;
            --notes) [[ -n "${2:-}" ]] || die "--notes attend un texte"; NOTES_TEXT="$2"; NOTES_SOURCE=text; shift ;;
            -*) die "option inconnue : $1" ;;
            *) [[ -f "$1" ]] || die "fichier de notes introuvable : $1"; NOTES_TEXT="$(cat "$1")"; NOTES_SOURCE=file ;;
        esac
        shift
    done
}

read_version() {
    VERSION=$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' src/Ovomium/OvomiumMod.cs)
    [[ -n "$VERSION" ]] || die "version introuvable dans src/Ovomium/OvomiumMod.cs"
    TAG="v$VERSION"
    ZIP="dist/downhill-ovomium-$VERSION.zip"
}

check_git() {
    step "Vérifications"
    if [[ -n "$(git status --porcelain)" ]]; then
        git status --short
        blocker "des modifications ne sont pas commitées : commite-les (ou range-les) d'abord."
    fi
    git rev-parse -q --verify "refs/tags/$TAG" >/dev/null &&
        die "le tag $TAG existe déjà : augmente la version dans src/Ovomium/OvomiumMod.cs."
    if ! git remote get-url origin >/dev/null 2>&1; then
        blocker "pas de remote origin : crée d'abord le dépôt $GITHUB_REPO (voir docs/distribution.md)."
    elif [[ -z "$DRY_RUN" ]]; then
        git ls-remote origin >/dev/null || die "dépôt origin inaccessible ($(git remote get-url origin))."
        [[ -z "$(git ls-remote --tags origin "$TAG")" ]] || die "le tag $TAG existe déjà sur GitHub."
    fi
    if [[ -n "$DRY_RUN" ]]; then say "  gh : non vérifié (essai à blanc)"
    else gh auth status >/dev/null 2>&1 || die "gh n'est pas connecté à GitHub : lance « gh auth login »."; fi
    # Le mod doit connaître ce dépôt et cette clé, sinon les joueurs ne recevraient jamais la mise à jour.
    grep -rqF --include='*.cs' "$GITHUB_REPO" src ||
        blocker "« $GITHUB_REPO » absent du code du mod (src/) : le mod chercherait ses mises à jour ailleurs."
    grep -rqF --include='*.cs' "$PUBLIC_KEY" src ||
        blocker "clé publique absente du code du mod (src/) : le mod refuserait la signature."
    say "  OK : version $VERSION, tag $TAG libre"
}

# Clé publique (base64) → fichier PEM : en-tête DER fixe d'une clé publique Ed25519 + 32 octets.
public_pem() {
    { printf '\x30\x2a\x30\x05\x06\x03\x2b\x65\x70\x03\x21\x00'; base64 -d <<<"$PUBLIC_KEY"; } |
        openssl pkey -pubin -inform DER -out "$1"
}

check_key() {
    step "Clé de signature"
    [[ -f "$KEY" ]] || die "clé privée introuvable : $KEY (restaure ta sauvegarde, voir docs/distribution.md)."
    openssl pkey -in "$KEY" -noout -text 2>/dev/null | head -n1 | grep -q '^ED25519 Private-Key' ||
        die "$KEY n'est pas une clé privée Ed25519 lisible."
    local derived
    derived=$(openssl pkey -in "$KEY" -pubout -outform DER | tail -c 32 | base64 -w0)
    [[ "$derived" == "$PUBLIC_KEY" ]] ||
        die "la clé $KEY ne correspond pas à la clé publique du mod ($PUBLIC_KEY) : mauvaise clé ?"
    say "  OK : la clé privée correspond à la clé publique du mod"
}

# Notes de release dans $TMP/notes.md. Crée CHANGELOG.md s'il n'existe pas.
prepare_notes() {
    step "Notes de version"
    if [[ "$NOTES_SOURCE" != changelog ]]; then printf '%s\n' "$NOTES_TEXT" >"$TMP/notes.md"; return; fi
    if [[ ! -f "$CHANGELOG" ]]; then
        printf "# Journal des modifications d'Ovomium\n\n%s\n\n" "$UNRELEASED" >"$CHANGELOG"
        say "  $CHANGELOG créé avec une section « Non publié »."
    fi
    # Section « Non publié » sans ses lignes vides de tête et de queue.
    awk -v h="$UNRELEASED" '$0==h{p=1;next} /^## /{p=0} p' "$CHANGELOG" |
        awk 'NF{f=1} f' | tac | awk 'NF{f=1} f' | tac >"$TMP/notes.md"
    if [[ ! -s "$TMP/notes.md" ]]; then
        blocker "$CHANGELOG : section « Non publié » vide. Écris dessous ce qui change, commite, puis relance."
        echo "(essai à blanc : pas de notes)" >"$TMP/notes.md"
    fi
    sed 's/^/  | /' "$TMP/notes.md"
}

build_package() {
    step "Paquet (tools/make-dist.sh)"
    bash tools/make-dist.sh $BUILD_ARG
    local f
    for f in "$ZIP" "$MANIFEST" dist/Ovomium.dll dist/LMD-Camera.dll; do [[ -f "$f" ]] || die "fichier absent : $f"; done
    python3 -I -c 'import json,sys; v=json.load(open(sys.argv[1],"rb"))["version"]; sys.exit(v!=sys.argv[2])' \
        "$MANIFEST" "$VERSION" || die "$MANIFEST n'annonce pas la version $VERSION."
}

sign_manifest() {
    step "Signature du manifeste"
    rm -f "$MANIFEST.sig"
    openssl pkeyutl -sign -rawin -inkey "$KEY" -in "$MANIFEST" -out "$MANIFEST.sig"
    [[ $(stat -c %s "$MANIFEST.sig") == 64 ]] || die "signature de taille inattendue."
    public_pem "$TMP/public.pem"
    openssl pkeyutl -verify -rawin -pubin -inkey "$TMP/public.pem" -in "$MANIFEST" -sigfile "$MANIFEST.sig" >/dev/null ||
        die "la signature produite ne se vérifie pas avec la clé publique du mod."
    say "  OK : $MANIFEST.sig vérifiée avec la clé publique du mod"
}

stamp_changelog() { # « ## Non publié » → nouvelle section « ## <version> — <date> », commitée
    [[ "$NOTES_SOURCE" == changelog ]] || return 0
    step "Changelog daté (commit « Changelog $VERSION »)"
    awk -v h="$UNRELEASED" -v v="## $VERSION — $(date +%F)" '$0==h{print h; print ""; print v; next} {print}' \
        "$CHANGELOG" >"$TMP/changelog" && cp "$TMP/changelog" "$CHANGELOG"
    git add "$CHANGELOG" && git commit -q -m "Changelog $VERSION"
}

publish() {
    stamp_changelog
    step "Tag $TAG, envoi sur GitHub, release"
    git tag -a "$TAG" -m "Ovomium $VERSION"
    git push origin HEAD "$TAG"
    gh release create "$TAG" "$ZIP" "$MANIFEST" "$MANIFEST.sig" dist/Ovomium.dll dist/LMD-Camera.dll \
        --repo "$GITHUB_REPO" --title "Ovomium $VERSION" --notes-file "$TMP/notes.md"
    say "" "Release publiée : https://github.com/$GITHUB_REPO/releases/tag/$TAG"
}

remind_visibility() {
    local visibility="?"
    [[ -z "$DRY_RUN" ]] && visibility=$(gh repo view "$GITHUB_REPO" --json visibility --jq .visibility 2>/dev/null || echo "?")
    if [[ "$visibility" == PUBLIC ]]; then
        say "" "Le dépôt est public : les joueurs reçoivent la mise à jour au prochain lancement du jeu." \
            "Referme-le quand tout le monde l'a eue : tools/repo-visibility.sh private"
    else
        say "" "RAPPEL : tant que le dépôt est privé, les joueurs ne voient PAS la mise à jour." \
            "  1. Ouvre-le :   tools/repo-visibility.sh public" \
            "  2. Chaque joueur lance le jeu une fois (le mod télécharge), puis le relance quand le message le demande." \
            "  3. Referme-le : tools/repo-visibility.sh private"
    fi
}

main() {
    parse_args "$@"
    read_version
    say "Publication d'Ovomium $VERSION ($TAG) sur $GITHUB_REPO${DRY_RUN:+ — ESSAI À BLANC}"
    check_git
    check_key
    prepare_notes
    build_package
    sign_manifest
    if [[ -n "$DRY_RUN" ]]; then
        step "Essai à blanc terminé"
        say "Rien n'a été commité, tagué ni envoyé. Fichiers prêts dans dist/ :" \
            "  $ZIP" "  $MANIFEST" "  $MANIFEST.sig" "  dist/Ovomium.dll" "  dist/LMD-Camera.dll"
    else
        publish
    fi
    remind_visibility
}

main "$@"
