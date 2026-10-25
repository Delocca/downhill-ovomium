#!/bin/bash
# Affiche en direct l'écran virtuel (Xvfb) dans une fenêtre, en lecture seule.
# Usage : tools/watch-xvfb.sh [display]   (défaut :99). Fermer la fenêtre ou touche q pour quitter.
DISP="${1:-:99}"
if ! xdpyinfo -display "$DISP" >/dev/null 2>&1; then
    echo "Aucun écran virtuel actif sur $DISP (rien n'est lancé pour l'instant)."
    exit 1
fi
SIZE=$(xdpyinfo -display "$DISP" | awk '/dimensions:/ {print $2}')
# Détaché (setsid) : lancé via « ! » dans Claude Code, un processus attaché se termine aussitôt
DISPLAY="${DISPLAY:-:0}" setsid ffplay -loglevel error -window_title "Écran virtuel $DISP (lecture seule)" \
    -f x11grab -framerate 10 -video_size "$SIZE" -i "$DISP" </dev/null >/tmp/watch-xvfb.log 2>&1 &
echo "Fenêtre de visionnage ouverte (q pour fermer). Erreurs éventuelles : /tmp/watch-xvfb.log"
