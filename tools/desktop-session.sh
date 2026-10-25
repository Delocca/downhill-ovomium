#!/bin/bash
# Session de validation Ovomium sur le vrai bureau d'Edia.
# Lancement (depuis Claude Code) :  ! gnome-terminal -- bash tools/desktop-session.sh
# Déploie le mod en mode écho + debug, démarre un bot, lance LMD via Steam, guide la session,
# puis remet tout en place (mod retiré, config restaurée). Journal : /tmp/lmd-session/session.log
set -u -o pipefail
SESSION=/tmp/lmd-session
mkdir -p "$SESSION"
exec > >(tee -a "$SESSION/session.log") 2>&1

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="$HOME/.local/share/Steam/steamapps/common/Lonely Mountains - Downhill"
CFG="$GAME/UserData/Ovomium.cfg"
DATA="$GAME/UserData/Ovomium"
BOT_DLL="$ROOT/tools/LmdBot/bin/Release/net8.0/LmdBot.dll"
ORBIT_PID="" REPLAY_PID=""

say() { printf '\n\e[1;36m== %s\e[0m\n' "$*"; }
mark() { echo "[$(date +%H:%M:%S)] REPÈRE : $*"; }  # repères horodatés pour relire le journal

cleanup() {
    say "Nettoyage"
    for pid in $ORBIT_PID $REPLAY_PID; do kill "$pid" 2>/dev/null; done
    [ -f "$GAME/Mods/Ovomium.dll" ] && trash-put "$GAME/Mods/Ovomium.dll" && echo "mod retiré de Mods/"
    if [ -f "$SESSION/Ovomium.cfg.avant" ]; then cp "$SESSION/Ovomium.cfg.avant" "$CFG"; echo "config d'avant restaurée"
    elif [ -f "$CFG" ]; then trash-put "$CFG"; echo "config de session retirée"; fi
    cp "$GAME/MelonLoader/Latest.log" "$SESSION/MelonLoader-Latest.log" 2>/dev/null
    ls "$DATA/recordings" 2>/dev/null > "$SESSION/enregistrements.txt"
    echo "Tout est dans $SESSION (journal, log du mod, captures du bureau, logs des bots)."
}

prepare() {
    say "Préparation"
    if pgrep -f "[L]MD_Win_x64.exe" >/dev/null; then echo "Le jeu tourne déjà : ferme-le puis relance ce script."; read -r; exit 1; fi
    pgrep -x steam >/dev/null || pgrep -f "[s]team.sh" >/dev/null || { echo "Steam n'est pas lancé : lance-le puis relance ce script."; read -r; exit 1; }
    echo "Compilation (mod + bot, mode Release)…"
    dotnet build "$ROOT/src/Ovomium" -c Release -nologo -v q | tail -1 || exit 1
    dotnet build "$ROOT/tools/LmdBot" -c Release -nologo -v q | tail -1 || exit 1
    [ -f "$CFG" ] && cp "$CFG" "$SESSION/Ovomium.cfg.avant"
    # Config de session : écho, journal détaillé, enregistrement des poses (le mod complète le reste).
    printf '[Ovomium]\nEchoMode = true\nEchoDelaySeconds = 2.0\nDebugLog = true\nRecordPoses = true\nShowPanel = true\n' > "$CFG"
    cp "$ROOT/src/Ovomium/bin/Release/net6.0/Ovomium.dll" "$GAME/Mods/"
    [ -f "$GAME/Mods/LmdExplorer.dll" ] && echo "⚠ LmdExplorer.dll est aussi dans Mods/ (laissé tel quel)."
    echo "Mod déployé, config de session écrite."
}

start_orbit_bot() {
    dotnet "$BOT_DLL" --name "Bot-orbite" --port 47710 > "$SESSION/bot-orbite.log" 2>&1 &
    ORBIT_PID=$!
    echo "Bot-orbite démarré (journal : $SESSION/bot-orbite.log)"
}

start_replay_bot() {
    [ -n "$REPLAY_PID" ] && kill "$REPLAY_PID" 2>/dev/null
    dotnet "$BOT_DLL" --name "Bot-rejeu" --port 47711 --replay latest > "$SESSION/bot-rejeu.log" 2>&1 &
    REPLAY_PID=$!
    sleep 1
    head -2 "$SESSION/bot-rejeu.log"
}

screenshot() {
    local n; n=$(date +%H%M%S)
    echo "Capture du bureau dans 5 s : reviens au jeu !"
    sleep 5
    import -window root "$SESSION/bureau-$n.png" && echo "capture : $SESSION/bureau-$n.png"
}

guide() {
    cat <<'EOF'

  ┌─────────────────────────── Mode d'emploi ───────────────────────────┐
  │ F10 dans le jeu = capture d'écran du jeu (à chaque étape ✔).        │
  │ F8 = panneau des joueurs · F9 = écho on/off · F7 = aspect fantômes. │
  └─────────────────────────────────────────────────────────────────────┘
  1. MENU : en haut à droite, le panneau « Ovomium 0.1.0 · écoute sur le
     port 47700 · écho ». Bot-orbite y est listé (menu, ping en ms) et un
     message « Bot-orbite a rejoint » s'affiche au centre.            ✔ F10
  2. Lance une piste. Au départ, deux fantômes semi-transparents :
     - « (écho) » (pseudo ambre) = toi, 2 s en retard ;
     - « Bot-orbite » qui tourne autour de toi à 4 m en copiant ta pose.
     Pseudos colorés au-dessus des têtes, pastilles de même couleur dans
     le panneau. F7 : essaie les 3 aspects (translucide, teinté, normal). ✔ F10
  3. Roule 30 s. Regarde ton écho : fluide ? vélo complet (roues qui
     tournent, suspension, pédalier) ? rider cohérent (mains au guidon,
     pieds aux pédales, tête, cheveux/casque) ?                    ✔ F10 ×2
  4. Crashe exprès : 2 s après, l'écho rejoue la chute (ragdoll), puis
     réapparaît au point de reprise sans glisser à travers la carte. ✔ F10
  5. F8 deux fois, F9 deux fois (messages au centre).
  6. Retourne au menu : les fantômes disparaissent. Puis ici, tape r +
     Entrée : Bot-rejeu rejoue ta descente. Relance la MÊME piste et
     regarde Bot-rejeu descendre.                                  ✔ F10
  7. Quitte le jeu, puis tape q + Entrée ici.

  Commandes ici : r = (re)lancer Bot-rejeu · c = capture du bureau (5 s)
                  n = noter une observation · q = fin de session
EOF
}

menu_loop() {
    while true; do
        read -r -p "> " cmd rest
        case "$cmd" in
            r) mark "lancement Bot-rejeu"; start_replay_bot ;;
            c) mark "capture bureau"; screenshot ;;
            n) mark "NOTE D'EDIA : $rest" ;;
            q) mark "fin"; break ;;
            "") ;;
            *) guide ;;
        esac
    done
}

trap cleanup EXIT
prepare
start_orbit_bot
say "Lancement du jeu via Steam"
steam steam://rungameid/711540 >/dev/null 2>&1 &
mark "jeu lancé"
guide
menu_loop
if pgrep -f "[L]MD_Win_x64.exe" >/dev/null; then echo "Le jeu tourne encore : ferme-le puis appuie sur Entrée."; read -r; fi
cleanup
trap - EXIT
echo "Terminé ! Appuie sur Entrée pour fermer."
read -r
