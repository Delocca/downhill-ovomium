#!/bin/bash
# Lance LMD (Proton 10 + MelonLoader) sur un écran virtuel, sans toucher au bureau d'Edia.
# Usage : tools/lmd-virtual.sh start|stop|shot [fichier.png]
# Voir en direct : tools/watch-virtual.sh
#
# Mode par défaut : Xorg « dummy » (:98, paquet xserver-xorg-video-dummy, config tools/xdummy.conf)
#   + DXVK sur la carte NVIDIA -> rendu complet, conduite possible (vérifié 2026-10-25).
# Mode LMD_SCREEN=xvfb (:99) : Xvfb n'annonce pas de fréquence -> DXVK plante (division par zéro dans dxgi),
#   WineD3D y reste figé à la 1re frame. Seul usage : LMD_ARGS="-batchmode -nographics" (logique sans rendu,
#   mais le sol n'a pas de collision, le vélo tombe).
# Steam doit tourner (steam_api). Clavier simulé : xdotool avec DISPLAY de l'écran virtuel, JAMAIS :0.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="$HOME/.local/share/Steam/steamapps/common/Lonely Mountains - Downhill"
PROTON="$HOME/.local/share/Steam/steamapps/common/Proton 10.0/proton"
SCREEN_KIND="${LMD_SCREEN:-dummy}"
if [ "$SCREEN_KIND" = xvfb ]; then DISP=:99; else DISP=:98; fi

start_screen() {
    xdpyinfo -display "$DISP" >/dev/null 2>&1 && return
    if [ "$SCREEN_KIND" = xvfb ]; then
        setsid Xvfb "$DISP" -screen 0 1024x768x24 </dev/null >/tmp/xvfb${DISP#:}.log 2>&1 &
    else
        # Xorg lancé directement (pas le wrapper suid) : autorisé sans root avec le pilote dummy
        (cd /tmp && setsid /usr/lib/xorg/Xorg "$DISP" -noreset -nolisten tcp -config "$HERE/xdummy.conf" \
            -logfile /tmp/xdummy${DISP#:}.log </dev/null >/dev/null 2>&1 &)
    fi
    local t=0
    until xdpyinfo -display "$DISP" >/dev/null 2>&1; do
        sleep 0.5; t=$((t+1)); [ $t -gt 20 ] && { echo "écran virtuel $DISP injoignable"; exit 1; }
    done
}

start() {
    start_screen
    export DISPLAY="$DISP"
    export STEAM_COMPAT_DATA_PATH="$HOME/.local/share/Steam/steamapps/compatdata/711540"
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.local/share/Steam"
    export SteamAppId=711540 SteamGameId=711540
    export WINEDLLOVERRIDES="version=n,b"
    [ "$SCREEN_KIND" = xvfb ] && export PROTON_USE_WINED3D=1
    cd "$GAME" || exit 1
    # setsid + stdin fermé : sinon le processus est suspendu (SIGTSTP) en arrière-plan
    # shellcheck disable=SC2086
    setsid "$PROTON" run ./LMD_Win_x64.exe ${LMD_ARGS:-} </dev/null >/tmp/lmd-run.log 2>&1 &
    echo "LMD lancé sur $DISP ($SCREEN_KIND). Logs : /tmp/lmd-run.log, $GAME/MelonLoader/Latest.log"
}

stop() {
    pkill -f "[L]MD_Win_x64.exe"
    # wineserver resté vivant : le lancement suivant échouerait (« no driver could be loaded »)
    WINEPREFIX="$HOME/.local/share/Steam/steamapps/compatdata/711540/pfx" "$(dirname "$PROTON")/files/bin/wineserver" -k 2>/dev/null
    pkill -f "[X]vfb $DISP"
    pkill -f "[X]org $DISP"
    echo "arrêté"
}

# Capture de la fenêtre du jeu (sinon de tout l'écran virtuel)
shot() {
    local out="${1:-/tmp/lmd-shot.png}" id
    id=$(xdotool search --onlyvisible --name "^Lonely Mountains: Downhill$" 2>/dev/null | head -1)
    DISPLAY="$DISP" import -window "${id:-root}" "$out" && echo "$out"
}

case "${1:-}" in
    start) start ;;
    stop) stop ;;
    shot) DISPLAY="$DISP" shot "${2:-}" ;;
    *) echo "usage: $0 start|stop|shot [fichier.png]"; exit 1 ;;
esac
