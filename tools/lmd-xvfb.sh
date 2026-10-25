#!/bin/bash
# Lance LMD (Proton 10 + MelonLoader) sur un écran virtuel Xvfb, sans toucher au bureau.
# Usage : tools/lmd-xvfb.sh start [display]   |   tools/lmd-xvfb.sh stop [display]
# - WineD3D obligatoire : DXVK plante sous Xvfb (division par zéro dans dxgi, fréquence d'écran nulle).
# - Lancé hors du Steam client mais Steam doit tourner (steam_api).
# - Rendu sous Xvfb : bloqué dès la 1re frame (écran noir, « Loading Addons... », jamais de 2e Update),
#   même sans MelonLoader et même hors bac à sable. Contournement pour explorer la logique du jeu :
#   LMD_ARGS="-batchmode -nographics" tools/lmd-xvfb.sh start  -> menus et pistes se chargent (pas de rendu),
#   mais le sol n'a pas de collision (le vélo tombe à travers) : pas de test de conduite possible.
set -u
GAME="$HOME/.local/share/Steam/steamapps/common/Lonely Mountains - Downhill"
PROTON="$HOME/.local/share/Steam/steamapps/common/Proton 10.0/proton"
DISP="${2:-:99}"

start() {
    pgrep -f "[X]vfb $DISP" >/dev/null || { setsid Xvfb "$DISP" -screen 0 1024x768x24 </dev/null >/tmp/xvfb${DISP#:}.log 2>&1 & sleep 1; }
    export DISPLAY="$DISP"
    export STEAM_COMPAT_DATA_PATH="$HOME/.local/share/Steam/steamapps/compatdata/711540"
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.local/share/Steam"
    export SteamAppId=711540 SteamGameId=711540
    export WINEDLLOVERRIDES="version=n,b" PROTON_USE_WINED3D=1
    cd "$GAME" || exit 1
    # setsid + stdin fermé : sinon le processus est suspendu (SIGTSTP) en arrière-plan
    # LMD_ARGS : arguments Unity supplémentaires (ex. LMD_ARGS="-batchmode -nographics")
    # shellcheck disable=SC2086
    setsid "$PROTON" run ./LMD_Win_x64.exe ${LMD_ARGS:-} </dev/null >/tmp/lmd-run.log 2>&1 &
    echo "LMD lancé sur $DISP (log Proton : /tmp/lmd-run.log, log mod : $GAME/MelonLoader/Latest.log)"
}

stop() {
    pkill -f "[L]MD_Win_x64.exe"
    pkill -f "[X]vfb $DISP"
    echo "arrêté"
}

case "${1:-}" in
    start) start ;;
    stop) stop ;;
    *) echo "usage: $0 start|stop [display]"; exit 1 ;;
esac
