#!/bin/bash
# Affiche en direct l'écran virtuel des tests dans une fenêtre, en lecture seule.
# Usage : tools/watch-virtual.sh [display]   (défaut :98 = Xorg dummy ; :99 = Xvfb). Touche q pour fermer.
# Survit aux relances du jeu : si l'écran virtuel s'arrête, attend son retour et rouvre la fenêtre.
# Fermer la fenêtre (q) pendant que l'écran virtuel tourne arrête le visionnage.
DISP="${1:-:98}"
LOG=/tmp/watch-virtual.log

screen_up() { xdpyinfo -display "$DISP" >/dev/null 2>&1; }

watch_loop() {
    while true; do
        until screen_up; do sleep 2; done
        local size
        size=$(xdpyinfo -display "$DISP" | awk '/dimensions:/ {print $2}')
        DISPLAY="${DISPLAY:-:0}" ffplay -loglevel error -window_title "Écran virtuel $DISP (lecture seule)" \
            -f x11grab -framerate 10 -video_size "$size" -i "$DISP" </dev/null >>"$LOG" 2>&1
        # ffplay fermé alors que l'écran tourne encore : c'est la personne qui a quitté
        sleep 1
        screen_up && break
        echo "$(date +%T) écran virtuel arrêté, attente de son retour…" >>"$LOG"
    done
}

if pgrep -f "[w]atch-virtual.sh.*loop" >/dev/null; then
    echo "Le visionnage tourne déjà."
    exit 0
fi
if [ "${2:-}" = loop ]; then
    watch_loop
    exit 0
fi
# Détaché (setsid) : lancé via « ! » dans Claude Code, un processus attaché peut se terminer aussitôt
setsid bash "$0" "$DISP" loop </dev/null >/dev/null 2>&1 &
screen_up || echo "Écran virtuel $DISP pas encore actif : la fenêtre s'ouvrira dès qu'il démarre."
echo "Visionnage lancé (q dans la fenêtre pour arrêter). Journal : $LOG"
