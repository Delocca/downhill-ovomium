#!/bin/bash
# Ovomium pour Linux / Steam Deck (jeu Windows lancé par Proton). Sans sudo.
#   bash install-linux.sh             installe (ou met à jour)
#   bash install-linux.sh uninstall   désinstalle
# Ce que fait l'installation : copie MelonLoader, Ovomium.dll et LMD-Camera.dll dans le dossier du jeu, puis
# règle la DLL « version » en « native,builtin » dans le registre du préfixe Proton du jeu
# (compatdata/711540/pfx/user.reg, copie de sauvegarde à côté), pour que Wine charge MelonLoader.
# Variables de test : OVOMIUM_STEAM_ROOTS (dossiers Steam séparés par « : »),
# OVOMIUM_SKIP_RUNNING_CHECK=1, OVOMIUM_ANSWER=o|n (réponse aux questions sans terminal),
# OVOMIUM_NO_TRASH=1 (supprimer au lieu de mettre à la corbeille).
set -euo pipefail

APP_ID=711540
EXE=LMD_Win_x64.exe
CAMERA=LMD-Camera.dll
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOAD="$HERE/fichiers"

say() { printf '%s\n' "$*"; }
die() { printf '\nERREUR : %s\n' "$*" >&2; exit 1; }

ask() { # ask "question" défaut(o|n) → 0 si oui
    local answer="${OVOMIUM_ANSWER:-}"
    if [[ -z "$answer" && -t 0 ]]; then
        read -r -p "$1 [o/n, défaut $2] " answer || true
    else
        say "$1 [o/n, défaut $2]"
    fi
    answer="${answer:-$2}"
    say "  -> $answer"
    [[ "$answer" =~ ^[oOyY] ]]
}

remove_path() { # corbeille si possible
    [[ -e "$1" ]] || return 0
    if [[ -z "${OVOMIUM_NO_TRASH:-}" ]] && command -v gio >/dev/null && gio trash "$1" 2>/dev/null; then
        say "  corbeille : $1"
    else rm -rf -- "$1"; say "  supprimé : $1"; fi
}

steam_roots() {
    if [[ -n "${OVOMIUM_STEAM_ROOTS:-}" ]]; then tr ':' '\n' <<<"$OVOMIUM_STEAM_ROOTS"; return; fi
    printf '%s\n' "$HOME/.steam/steam" "$HOME/.local/share/Steam" \
        "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/snap/steam/common/.local/share/Steam"
}

libraries() { # bibliothèques Steam (dossier Steam + libraryfolders.vdf), sans doublons
    local root
    while read -r root; do
        [[ -d "$root/steamapps" ]] || continue
        printf '%s\n' "$root"
        local vdf="$root/steamapps/libraryfolders.vdf"
        [[ -f "$vdf" ]] && sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"
    done < <(steam_roots) | while read -r lib; do
        [[ -d "$lib" ]] && realpath "$lib"
    done | awk '!seen[$0]++'
}

find_game() { # écrit LIB et GAME
    local lib acf dir
    while read -r lib; do
        acf="$lib/steamapps/appmanifest_$APP_ID.acf"
        [[ -f "$acf" ]] || continue
        dir=$(sed -n 's/^[[:space:]]*"installdir"[[:space:]]*"\(.*\)".*/\1/p' "$acf" | head -n1)
        if [[ -f "$lib/steamapps/common/$dir/$EXE" ]]; then
            LIB="$lib"; GAME="$lib/steamapps/common/$dir"; return 0
        fi
    done < <(libraries)
    return 1
}

check_closed() {
    [[ "${OVOMIUM_SKIP_RUNNING_CHECK:-}" == 1 ]] && return 0
    if pgrep -f "$EXE" >/dev/null; then die "le jeu est ouvert : quitte-le puis relance ce script."; fi
}

# Override « version » dans user.reg : set (ajoute/remplace) ou unset (retire).
set_override() {
    local reg="$LIB/steamapps/compatdata/$APP_ID/pfx/user.reg" mode="$1"
    if [[ ! -f "$reg" ]]; then
        say "  Préfixe Proton absent ($reg)."
        [[ "$mode" == set ]] && say "  -> Lance le jeu une fois sans mod, quitte-le, puis relance ce script." \
            "Ou, dans Steam : Propriétés du jeu → Options de lancement : WINEDLLOVERRIDES=\"version=n,b\" %command%"
        return 0
    fi
    [[ -f "$reg.ovomium-sauvegarde" ]] || cp -p "$reg" "$reg.ovomium-sauvegarde"
    local tmp
    tmp=$(mktemp "$reg.XXXXXX")
    awk -v mode="$mode" '
        BEGIN { section = "[Software\\\\Wine\\\\DllOverrides]"; line = "\"version\"=\"native,builtin\"" }
        index($0, section) == 1 { inside = 1; found = 1; print; next }
        inside && /^\[/ { if (mode == "set" && !done) { print line; done = 1 } inside = 0 }
        inside && /^"version"=/ { if (mode == "set" && !done) { print line; done = 1 } next }
        inside && /^$/ && mode == "set" && !done { print line; done = 1 }
        { print }
        END {
            if (inside && mode == "set" && !done) print line
            if (!found && mode == "set") { print ""; print section; print line }
        }' "$reg" >"$tmp"
    mv "$tmp" "$reg"
    say "  user.reg : override version → $mode ($reg)"
}

other_mods() {
    find "$GAME/Mods" "$GAME/Plugins" -name '*.dll' ! -name 'Ovomium.dll' 2>/dev/null || true
}

install_melon() {
    local want="$PAYLOAD/melonloader"
    if [[ ! -e "$GAME/version.dll" && ! -d "$GAME/MelonLoader" ]]; then
        cp -r "$want/." "$GAME/"; say "MelonLoader : installé"; return
    fi
    if cmp -s "$want/version.dll" "$GAME/version.dll" &&
        cmp -s "$want/MelonLoader/net6/MelonLoader.dll" "$GAME/MelonLoader/net6/MelonLoader.dll"; then
        say "MelonLoader : déjà à jour"; return
    fi
    say "Ce jeu a déjà une autre version de MelonLoader (ou un autre version.dll)."
    [[ -n "$(other_mods)" ]] && say "Autres mods présents : ils pourraient ne plus marcher." && other_mods
    if ask "Remplacer par la version fournie (prévue pour Ovomium) ?" o; then
        remove_path "$GAME/version.dll"; remove_path "$GAME/MelonLoader"
        cp -r "$want/." "$GAME/"; say "MelonLoader : remplacé"
    else
        say "MelonLoader : gardé (Ovomium risque de ne pas marcher)"
    fi
}

install_camera() { # mod LMD-Camera (flo-st, MIT), livré tel quel ; écrase une version existante
    local target="$GAME/Mods/$CAMERA" detail=""
    if [[ -e "$target" ]]; then
        if cmp -s "$PAYLOAD/$CAMERA" "$target"; then detail=" (déjà là, identique)"
        else detail=" (remplace une autre version)"; fi
    fi
    cp "$PAYLOAD/$CAMERA" "$target"
    say "Mod LMD-Camera copié : $target$detail"
}

uninstall_camera() { # retire LMD-Camera seulement si identique à la copie livrée (sinon : installée par le joueur)
    local target="$GAME/Mods/$CAMERA"
    [[ -e "$target" ]] || return 0
    if [[ -f "$PAYLOAD/$CAMERA" ]] && cmp -s "$PAYLOAD/$CAMERA" "$target"; then
        remove_path "$target"
    else
        say "LMD-Camera gardé : $target n'est pas la copie installée par Ovomium (supprime-le si tu n'en veux plus)."
    fi
}

do_install() {
    [[ -f "$PAYLOAD/Ovomium.dll" && -f "$PAYLOAD/$CAMERA" ]] || die "fichiers manquants : extrais tout le zip."
    install_melon
    mkdir -p "$GAME/Mods"
    cp "$PAYLOAD/Ovomium.dll" "$GAME/Mods/Ovomium.dll"
    say "Mod copié : $GAME/Mods/Ovomium.dll"
    install_camera
    [[ -f "$GAME/UserData/Ovomium.cfg" ]] && say "Réglages existants gardés : $GAME/UserData/Ovomium.cfg"
    set_override set
    say "" "Installé ! Lance le jeu depuis Steam." \
        "Premier lancement LONG (2 à 10 min, fenêtre noire de MelonLoader) : c'est normal, ne ferme rien." \
        "En jeu : F8 = joueurs, F6 = course (manette : Select + A). Voir LISEZ-MOI.txt."
}

do_uninstall() {
    remove_path "$GAME/Mods/Ovomium.dll"
    uninstall_camera # avant le test « autres mods » : notre copie ne doit pas retenir MelonLoader
    if [[ -n "$(other_mods)" ]]; then
        say "MelonLoader gardé : d'autres mods s'en servent."
    elif [[ -e "$GAME/version.dll" || -d "$GAME/MelonLoader" ]] &&
        ask "Aucun autre mod : retirer aussi MelonLoader ?" o; then
        remove_path "$GAME/version.dll"; remove_path "$GAME/MelonLoader"
        local d; for d in Mods Plugins UserLibs; do rmdir "$GAME/$d" 2>/dev/null || true; done
        set_override unset
    fi
    say "" "Ovomium est désinstallé. Tes réglages restent dans $GAME/UserData."
}

main() {
    local action="${1:-install}"
    LIB="" GAME=""
    find_game || die "Lonely Mountains: Downhill introuvable dans les bibliothèques Steam."
    say "Jeu : $GAME"
    check_closed
    case "$action" in
        install) do_install ;;
        uninstall) do_uninstall ;;
        *) die "action inconnue : $action (install ou uninstall)" ;;
    esac
}

main "$@"
