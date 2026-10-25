#!/bin/bash
# Affiche en direct l'écran virtuel des tests dans une fenêtre, en lecture seule.
# Usage : tools/watch-virtual.sh [display]   (défaut :98 = Xorg dummy ; :99 = Xvfb). Touche q pour fermer.
DISP="${1:-:98}"
if ! xdpyinfo -display "$DISP" >/dev/null 2>&1; then
    echo "Aucun écran virtuel actif sur $DISP (rien n'est lancé pour l'instant)."
    exit 1
fi
SIZE=$(xdpyinfo -display "$DISP" | awk '/dimensions:/ {print $2}')
# Détaché (setsid) : lancé via « ! » dans Claude Code, un processus attaché peut se terminer aussitôt
DISPLAY="${DISPLAY:-:0}" setsid ffplay -loglevel error -window_title "Écran virtuel $DISP (lecture seule)" \
    -f x11grab -framerate 10 -video_size "$SIZE" -i "$DISP" </dev/null >/tmp/watch-virtual.log 2>&1 &
echo "Fenêtre de visionnage ouverte (q pour fermer). Erreurs éventuelles : /tmp/watch-virtual.log"
