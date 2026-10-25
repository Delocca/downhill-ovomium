#!/bin/bash
# Pilote LMD au clavier sur l'écran virtuel (:98, voir lmd-virtual.sh) : menus, conduite, captures.
# JAMAIS sur :0 (bureau d'Edia). Usage :
#   tools/lmd-keys.sh enter                  lance le jeu (si besoin) et entre dans la piste sélectionnée
#   tools/lmd-keys.sh key K [K…]             touches successives (Return, Escape, Up, F9…), 0,4 s d'écart
#   tools/lmd-keys.sh hold K SECONDES        maintient une touche
#   tools/lmd-keys.sh drive NOM IPS ÉTAPE…   conduit en filmant : ÉTAPE = touches+jointes:durée
#                                            (ex. Up:1.3 Up+Left:0.8 none:2) ; vidéo /tmp/NOM.mp4,
#                                            IPS images/s extraites dans /tmp/NOM/, planche /tmp/NOM-montage.png
#   tools/lmd-keys.sh restart                menu pause > Recommencer la tentative (hors crash)
#   tools/lmd-keys.sh menu                   menu pause > Quitter vers le menu (hors crash)
#   tools/lmd-keys.sh shot FICHIER.png       capture de la fenêtre du jeu
# Touches du jeu (clavier) : Haut accélérer, Bas freiner, Gauche/Droite diriger, Maj sprinter,
# Retour arrière redémarrer au point de contrôle, Entrée valider/recommencer après un crash, Échap pause.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
LOG="$HOME/.local/share/Steam/steamapps/common/Lonely Mountains - Downhill/MelonLoader/Latest.log"
export DISPLAY=:98
win() { xdotool search --onlyvisible --name "^Lonely Mountains: Downhill$" 2>/dev/null | head -1; }
focus() { local w; w=$(win); [ -n "$w" ] && xdotool windowfocus "$w" 2>/dev/null; xdotool mousemove 1023 767; }
keys() { focus; for k in "$@"; do xdotool key "$k"; sleep 0.4; done; }

# Écran titre > Commencer la partie > montagne > piste > défi > vélo : six fois Entrée (cinq si LmdMulti
# a passé l'écran titre au lancement, réglage SkipIntro).
enter() {
    local presses=6
    if [ -z "$(win)" ]; then
        "$HERE/lmd-virtual.sh" start
        local t=0; until [ -n "$(win)" ]; do sleep 2; t=$((t+1)); [ $t -gt 60 ] && { echo "pas de fenêtre"; return 1; }; done
        # SkipIntro : attendre que le mod ait passé l'écran titre ; sinon l'écran titre met ~28 s à répondre
        t=0; until grep -aq "intro : écran titre passé" "$LOG"; do sleep 1; t=$((t+1)); [ $t -gt 40 ] && break; done
        sleep 2
    fi
    grep -aq "intro : écran titre passé" "$LOG" && presses=5
    local before; before=$(grep -ac "piste locale : [A-Z]" "$LOG")
    for _ in $(seq "$presses"); do keys Return; sleep 2.6; done
    local t=0; until [ "$(grep -ac "piste locale : [A-Z]" "$LOG")" -gt "$before" ]; do
        sleep 2; t=$((t+1)); [ $t -gt 30 ] && { echo "pas entré en piste (capture : /tmp/lmd-enter-echec.png)"; shot /tmp/lmd-enter-echec.png; return 1; }
    done
    sleep 8 # intro de la piste : vélo bloqué
    grep -a "piste locale" "$LOG" | tail -1
}

drive() {
    local name=$1 fps=$2; shift 2
    local w; w=$(win); eval "$(xdotool getwindowgeometry --shell "$w")"; focus
    ffmpeg -loglevel error -y -f x11grab -framerate 30 -video_size "${WIDTH}x${HEIGHT}" -i ":98+$X,$Y" \
        -c:v libx264 -preset ultrafast -crf 23 "/tmp/$name.mp4" </dev/null &
    local ff=$!
    sleep 0.3
    for step in "$@"; do
        local ks; IFS=+ read -ra ks <<< "${step%%:*}"
        for k in "${ks[@]}"; do [ "$k" != none ] && xdotool keydown "$k"; done
        sleep "${step##*:}"
        for k in "${ks[@]}"; do [ "$k" != none ] && xdotool keyup "$k"; done
    done
    sleep 0.3; kill -INT $ff; wait $ff
    mkdir -p "/tmp/$name"; find "/tmp/$name" -name '*.png' -delete
    ffmpeg -loglevel error -i "/tmp/$name.mp4" -vf "fps=$fps" "/tmp/$name/%03d.png"
    montage "/tmp/$name"/*.png -tile 6x -geometry 320x240+2+2 "/tmp/$name-montage.png"
    echo "/tmp/$name-montage.png ($(find "/tmp/$name" -name '*.png' | wc -l) images)"
}

shot() { local w; w=$(win); import -window "${w:-root}" "${1:-/tmp/lmd-shot.png}" && echo "${1:-/tmp/lmd-shot.png}"; }

case "${1:-}" in
    enter) enter ;;
    key) shift; keys "$@" ;;
    hold) focus; xdotool keydown "$2"; sleep "$3"; xdotool keyup "$2" ;;
    drive) shift; drive "$@" ;;
    restart) keys Escape Down Down Return ;;
    menu) keys Escape Down Down Down Down Down Return ;;
    shot) shot "${2:-}" ;;
    *) sed -n '2,15p' "$0"; exit 1 ;;
esac
