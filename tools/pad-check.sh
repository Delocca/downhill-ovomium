#!/bin/bash
# Vérification courte des commandes de course à la manette, sur le vrai bureau d'Edia, avec sa manette.
# Lancement (depuis Claude Code) :  ! gnome-terminal -- bash tools/pad-check.sh
# Déploie LmdMulti en mode test (aucun temps enregistré), démarre un bot qui accepte les courses, lance
# LMD via Steam, guide les essais, puis remet tout comme avant. Journal : /tmp/lmd-padcheck/
set -u -o pipefail
SESSION=/tmp/lmd-padcheck
mkdir -p "$SESSION"
exec > >(tee -a "$SESSION/session.log") 2>&1

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="$HOME/.local/share/Steam/steamapps/common/Lonely Mountains - Downhill"
CFG="$GAME/UserData/LmdMulti.cfg"
LOG="$GAME/MelonLoader/Latest.log"
BOT_DLL="$ROOT/tools/LmdBot/bin/Release/net8.0/LmdBot.dll"
JOIN_PID="" HOST_PID=""

say() { printf '\n\e[1;36m== %s\e[0m\n' "$*"; }
mark() { echo "[$(date +%H:%M:%S)] REPÈRE : $*"; }

cleanup() {
    say "Nettoyage"
    for pid in $JOIN_PID $HOST_PID; do kill "$pid" 2>/dev/null; done
    for dll in LmdMulti LmdExplorer; do
        if [ -f "$SESSION/$dll.dll.avant" ]; then cp "$SESSION/$dll.dll.avant" "$GAME/Mods/$dll.dll"
        elif [ -f "$GAME/Mods/$dll.dll" ]; then trash-put "$GAME/Mods/$dll.dll"; fi
    done
    if [ -f "$SESSION/LmdMulti.cfg.avant" ]; then cp "$SESSION/LmdMulti.cfg.avant" "$CFG"
    elif [ -f "$CFG" ]; then trash-put "$CFG"; fi
    echo "Mods/ et config remis comme avant."
    cp "$LOG" "$SESSION/MelonLoader-Latest.log" 2>/dev/null
    grep -a "manette\|\[écran\]\|course :" "$SESSION/MelonLoader-Latest.log" > "$SESSION/resume.txt" 2>/dev/null
    echo "Tout est dans $SESSION (journal, log du mod, résumé)."
}

prepare() {
    say "Préparation"
    if pgrep -f "[L]MD_Win_x64.exe" >/dev/null; then echo "Le jeu tourne déjà : ferme-le puis relance ce script."; read -r; exit 1; fi
    pgrep -x steam >/dev/null || { echo "Steam n'est pas lancé : lance-le puis relance ce script."; read -r; exit 1; }
    echo "Compilation (mod + bot)…"
    dotnet build "$ROOT/src/LmdMulti" -c Release -nologo -v q | tail -1 || exit 1
    dotnet build "$ROOT/tools/LmdBot" -c Release -nologo -v q | tail -1 || exit 1
    [ -f "$CFG" ] && cp "$CFG" "$SESSION/LmdMulti.cfg.avant"
    for dll in LmdMulti LmdExplorer; do [ -f "$GAME/Mods/$dll.dll" ] && cp "$GAME/Mods/$dll.dll" "$SESSION/$dll.dll.avant"; done
    [ -f "$GAME/Mods/LmdExplorer.dll" ] && trash-put "$GAME/Mods/LmdExplorer.dll" # outil de dev : pas pour ce test
    # Mode test : rien de ce qui se passe pendant l'essai n'est enregistré (classements, succès, progression).
    printf '[LmdMulti]\nTestMode = true\nShowPanel = true\n' > "$CFG"
    cp "$ROOT/src/LmdMulti/bin/Release/net6.0/LmdMulti.dll" "$GAME/Mods/"
    echo "Mod déployé (mode test)."
    dotnet "$BOT_DLL" --name "Bot-course" --port 47710 --race join --race-time 40 > "$SESSION/bot-course.log" 2>&1 &
    JOIN_PID=$!
}

invite() {
    [ -n "$HOST_PID" ] && kill "$HOST_PID" 2>/dev/null
    dotnet "$BOT_DLL" --name "Bot-hôte" --port 47711 --race host --race-delay 2 --race-time 40 > "$SESSION/bot-hote.log" 2>&1 &
    HOST_PID=$!
    echo "Bot-hôte t'invite dans ~3 s (sur ta piste, ou Sierra Rivera piste 2 si tu es au menu)."
}

guide() {
    cat <<'EOF'

  ┌──────────────────────── Vérification manette (5 min) ────────────────────────┐
  │ « Select » = le petit bouton à gauche au centre (View ⧉ / Back / Share).     │
  │ Le jeu est en MODE TEST : rien n'est enregistré, roule comme tu veux.        │
  └──────────────────────────────────────────────────────────────────────────────┘
  1. MENU (écran de choix de piste) : maintiens Select puis appuie sur A.
     → message « Course sur … : 1 joueur invité », la piste se charge.
     ✔ A ne doit RIEN valider dans le menu du jeu.
  2. Pendant l'attente / le compte à rebours : appuie sur Select seul.
     ✔ Le jeu ne doit PAS ouvrir la pause (Start, lui, l'ouvre toujours).
  3. Après « GO ! », roule un peu. Maintiens Select + B : une jauge rouge
     « Abandon… » se remplit. Relâche AVANT la fin.
     ✔ Rien ne se passe, et le vélo ne s'est pas crashé (B seul = crash volontaire).
  4. Maintiens Select + B jusqu'au bout (1,5 s) → « Tu as abandonné ».
  5. Le texte d'aide en bas du panneau de course doit parler de « Select + … ».
     Appuie sur une flèche du clavier puis regarde : il doit dire « F6 … ».
  6. Ici, tape i + Entrée : un bot t'invite. Le bandeau doit dire « Select + A
     rejoindre » (si tu as touché la manette en dernier). Accepte avec Select + A.
  7. Ferme le jeu, puis tape q + Entrée ici.

  Commandes ici : i = recevoir une invitation · n texte = noter une observation · q = fin
EOF
}

menu_loop() {
    while true; do
        read -r -p "> " cmd rest
        case "$cmd" in
            i) mark "invitation demandée"; invite ;;
            n) mark "NOTE D'EDIA : $rest" ;;
            q) mark "fin"; break ;;
            "") ;;
            *) guide ;;
        esac
    done
}

trap cleanup EXIT
prepare
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
