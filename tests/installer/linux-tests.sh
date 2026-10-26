#!/bin/bash
# Scénarios de install-linux.sh sur une fausse arborescence Steam/Proton dans /tmp.
# Usage : linux-tests.sh <dossier extrait du zip> <dossier de travail>
set -uo pipefail
DIST="$1" W="$2"
CAM=LMD-Camera.dll
FAIL=0
check() { if eval "$1"; then echo "OK   $2"; else echo "ÉCHEC $2"; FAIL=1; fi; }

# Steam principal + 2e bibliothèque (le jeu et son préfixe Proton y sont).
setup() {
    rm -rf "$W" && mkdir -p "$W"
    STEAM="$W/Steam" LIB2="$W/Bibliothèque 2"
    GAME="$LIB2/steamapps/common/Lonely Mountains - Downhill"
    REG="$LIB2/steamapps/compatdata/711540/pfx/user.reg"
    mkdir -p "$STEAM/steamapps" "$GAME" "$(dirname "$REG")"
    printf '"libraryfolders"\n{\n\t"0"\n\t{\n\t\t"path"\t\t"%s"\n\t}\n\t"1"\n\t{\n\t\t"path"\t\t"%s"\n\t}\n}\n' \
        "$STEAM" "$LIB2" >"$STEAM/steamapps/libraryfolders.vdf"
    printf '"AppState"\n{\n\t"appid"\t\t"711540"\n\t"installdir"\t\t"Lonely Mountains - Downhill"\n}\n' \
        >"$LIB2/steamapps/appmanifest_711540.acf"
    echo exe >"$GAME/LMD_Win_x64.exe"
    printf 'WINE REGISTRY Version 2\n\n[Software\\\\Wine\\\\AppDefaults\\\\X.exe\\\\DllOverrides] 1\n"atiadlxx"="builtin"\n\n[Software\\\\Wine\\\\DllOverrides] 1785139151\n#time=1\n"atl100"="native,builtin"\n"d3d11"="native"\n\n[Software\\\\Wine\\\\Fonts] 1\n"x"="y"\n' >"$REG"
    UFW="$W/ufw.conf" && echo 'ENABLED=no' >"$UFW" # le vrai /etc/ufw/ufw.conf de la machine n'est jamais lu
}

run() { # run [args] — sans terminal, réponses par OVOMIUM_ANSWER
    OVOMIUM_STEAM_ROOTS="$STEAM" OVOMIUM_SKIP_RUNNING_CHECK=1 OVOMIUM_NO_TRASH=1 OVOMIUM_UFW_CONF="$UFW" \
        bash "$DIST/install-linux.sh" "$@" >"$W/out.txt" 2>&1
}

count_override() { grep -c '^"version"="native,builtin"' "$REG"; }
section_has_override() {
    awk '/^\[Software\\\\Wine\\\\DllOverrides\]/{s=1;next} /^\[/{s=0} s && /^"version"="native,builtin"/{f=1} END{exit !f}' "$REG"
}

setup
mkdir -p "$GAME/UserData" && echo 'Port = 47701' >"$GAME/UserData/Ovomium.cfg"
run; code=$?
check '[[ $code == 0 ]]' "install : code 0 ($code)"
check 'cmp -s "$GAME/Mods/Ovomium.dll" "$DIST/fichiers/Ovomium.dll"' 'install : DLL copiée (2e bibliothèque)'
check 'cmp -s "$GAME/Mods/$CAM" "$DIST/fichiers/$CAM"' 'install : LMD-Camera copié'
check 'cmp -s "$GAME/version.dll" "$DIST/fichiers/melonloader/version.dll"' 'install : MelonLoader copié'
check 'grep -q 47701 "$GAME/UserData/Ovomium.cfg"' 'install : config gardée'
check 'section_has_override && [[ $(count_override) == 1 ]]' 'install : override version dans [Software\\Wine\\DllOverrides]'
check '[[ -f "$REG.ovomium-sauvegarde" ]]' 'install : sauvegarde de user.reg'
check 'grep -q "^\"d3d11\"=\"native\"" "$REG" && grep -q "^\"x\"=\"y\"" "$REG"' 'install : reste du registre intact'
check '! grep -q ufw "$W/out.txt"' 'install, ufw inactif : rien à signaler'
echo 'ENABLED=yes' >"$UFW"
run; check 'grep -q "sudo ufw allow 47701/udp" "$W/out.txt"' 'install, ufw actif : commande donnée avec le port de la config'
echo 'ENABLED=no' >"$UFW"
run; check '[[ $(count_override) == 1 ]] && grep -q "déjà à jour" "$W/out.txt"' 'réinstallation : idempotente'
check 'grep -q "LMD-Camera copié.*identique" "$W/out.txt"' 'réinstallation : LMD-Camera identique, signalé'

OVOMIUM_ANSWER=n run uninstall
check '[[ ! -e "$GAME/Mods/Ovomium.dll" && -e "$GAME/version.dll" && $(count_override) == 1 ]]' 'désinstallation + n : MelonLoader et override gardés'
check '[[ ! -e "$GAME/Mods/$CAM" ]]' 'désinstallation : LMD-Camera livré retiré'
run; OVOMIUM_ANSWER=o run uninstall
check 'grep -q "retirer aussi MelonLoader" "$W/out.txt"' 'désinstallation : LMD-Camera livré ne compte pas comme autre mod'
check '[[ ! -e "$GAME/version.dll" && ! -d "$GAME/MelonLoader" && ! -d "$GAME/Mods" ]]' 'désinstallation + o : MelonLoader retiré'
check '[[ $(count_override) == 0 ]] && grep -q "^\"d3d11\"=\"native\"" "$REG"' 'désinstallation : override retiré, reste intact'
check '[[ -f "$GAME/UserData/Ovomium.cfg" ]]' 'désinstallation : réglages gardés'

setup; mkdir -p "$GAME/Mods" && echo ancienne >"$GAME/Mods/$CAM"
echo 'ENABLED=yes' >"$UFW"
run
check 'grep -q "sudo ufw allow 47700/udp" "$W/out.txt"' 'install sans config, ufw actif : port par défaut'
echo 'ENABLED=no' >"$UFW"
check 'cmp -s "$GAME/Mods/$CAM" "$DIST/fichiers/$CAM" && grep -q "remplace une autre version" "$W/out.txt"' 'LMD-Camera existant : remplacé, journalisé'
echo joueur >"$GAME/Mods/$CAM"
OVOMIUM_ANSWER=o run uninstall
check 'grep -q joueur "$GAME/Mods/$CAM" && grep -q "LMD-Camera gardé" "$W/out.txt"' 'désinstallation, LMD-Camera différent : gardé et dit'
check '[[ ! -e "$GAME/Mods/Ovomium.dll" && -e "$GAME/version.dll" ]] && ! grep -q "retirer aussi" "$W/out.txt"' 'désinstallation, LMD-Camera différent : MelonLoader gardé sans question'

setup; rm "$REG"
run; code=$?
check '[[ $code == 0 ]] && grep -q "WINEDLLOVERRIDES" "$W/out.txt" && [[ -f "$GAME/Mods/Ovomium.dll" ]]' 'sans préfixe Proton : copie + consigne option de lancement'

setup; mkdir -p "$GAME/MelonLoader" && echo vieux >"$GAME/version.dll"
OVOMIUM_ANSWER=n run
check 'grep -q vieux "$GAME/version.dll" && grep -q "gardé" "$W/out.txt"' 'autre MelonLoader + n : gardé'
OVOMIUM_ANSWER=o run
check 'cmp -s "$GAME/version.dll" "$DIST/fichiers/melonloader/version.dll"' 'autre MelonLoader + o : remplacé'

setup; rm -rf "$GAME"
run; code=$?
check '[[ $code != 0 ]] && grep -q introuvable "$W/out.txt"' 'jeu introuvable : erreur claire'

exit $FAIL
