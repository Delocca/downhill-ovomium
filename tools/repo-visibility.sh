#!/bin/bash
# Ouvre (public) ou ferme (private) le dépôt GitHub d'Ovomium. Il reste privé sauf pendant les fenêtres de mise à
# jour : quand il est privé, le mod des joueurs ne voit pas les releases (réponse 404) et ne se met donc pas à jour.
# À lancer hors bac à sable, gh authentifié (`gh auth login`).
# Usage : tools/repo-visibility.sh public|private|status
set -euo pipefail

# Même dépôt que la constante du mod (src/, « Delocca/downhill-ovomium ») et que tools/release.sh.
GITHUB_REPO="Delocca/downhill-ovomium"

case "${1:-status}" in
    public|private)
        # Option de confirmation exigée par gh ≥ 2.53, inconnue des versions antérieures (Ubuntu 24.04 : 2.45).
        ACCEPT=()
        gh repo edit --help 2>/dev/null | grep -q -- --accept-visibility-change-consequences &&
            ACCEPT=(--accept-visibility-change-consequences)
        gh repo edit "$GITHUB_REPO" --visibility "$1" "${ACCEPT[@]}"
        echo "Dépôt $GITHUB_REPO : $1"
        if [[ "$1" == public ]]; then echo "Pense à le refermer quand tout le monde a mis à jour : $0 private"; fi ;;
    status)
        gh repo view "$GITHUB_REPO" --json visibility --jq '"Dépôt '"$GITHUB_REPO"' : " + (.visibility | ascii_downcase)' ;;
    *)
        echo "Usage : $0 public|private|status" >&2; exit 1 ;;
esac
