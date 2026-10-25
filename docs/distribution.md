# Distribution de LmdMulti (installeur Windows, Linux)

Objectif : un ami peu technique sous Windows extrait un zip, double-clique, répond « Oui » à Windows, et joue.

## Fabriquer le paquet

```
tools/make-dist.sh             # compile src/LmdMulti (Release) puis emballe
tools/make-dist.sh --no-build  # emballe la DLL déjà compilée
```

→ `dist/LmdMulti-<version>.zip` (version lue dans `LmdMultiMod.Version`) et son SHA-256. Besoins : `refs/` (compilation), `vendor/MelonLoader.x64-0.7.3.zip` (téléchargé depuis la release GitHub officielle s'il manque ; empreinte SHA-256 vérifiée, épinglée dans le script). Zip reproductible : ordre trié, dates fixées à celle du dernier commit.

Le script convertit pour Windows : fins de ligne CRLF partout (un .bat en LF casse `goto`), BOM UTF-8 sur les `.ps1` et le `LISEZ-MOI.txt` (sans BOM, Windows PowerShell 5.1 lit en ANSI : accents cassés), et remplace `@MOD_VERSION@`, `@MELON_VERSION@`, `@TESTED_BUILD@`. Les sources dans `installer/` restent en UTF-8/LF sans BOM.

**Après une mise à jour du jeu** : vérifier le mod, puis mettre à jour `TESTED_BUILD` dans `tools/make-dist.sh` (`buildid` de `steamapps/appmanifest_711540.acf`). L'installeur prévient seulement (il ne bloque pas) si le build installé diffère.

## Contenu du zip

```
LmdMulti-<v>/
  Installer LmdMulti.bat       lance fichiers\scripts\Install.ps1
  Desinstaller LmdMulti.bat    lance fichiers\scripts\Uninstall.ps1
  LISEZ-MOI.txt                installer, premier lancement long, commandes, dépannage, désinstaller
  install-linux.sh             Linux / Steam Deck (Proton)
  fichiers/
    LmdMulti.dll               le mod (LmdExplorer n'est jamais livré)
    melonloader/               archive officielle MelonLoader 0.7.3 extraite (version.dll + MelonLoader/)
    MelonLoader-LICENSE.md, MelonLoader-NOTICE.txt   Apache-2.0 (aussi dans melonloader/MelonLoader/Documentation)
    scripts/                   Common, Steam, Melon, Firewall, Install, Uninstall, Elevated (.ps1)
```

Noms de fichiers en ASCII (l'extracteur de zip de Windows gère mal les noms accentués de certains zips).

## Choix techniques

**`.bat` + PowerShell plutôt qu'un `.exe`.** Windows PowerShell 5.1 est présent sur tout Windows 10/11 ; le `.bat` lance `powershell.exe -NoProfile -ExecutionPolicy Bypass -File …` (Bypass : pas de blocage du script téléchargé, sauf stratégie de groupe d'entreprise). Raisons :
- Un `.exe` non signé déclenche SmartScreen (« Windows a protégé votre ordinateur ») **et** est une cible classique des faux positifs de Defender par apprentissage automatique (`Wacatac!ml`…). Un `.bat` peut aussi déclencher SmartScreen ou l'avertissement « Ouvrir le fichier » (sources divergentes, voir plus bas), mais le contenu reste lisible : n'importe qui peut vérifier ce qu'il fait.
- Smart App Control (Windows 11, activé sur certaines installations neuves) bloque **les deux** sans bouton de contournement : `.exe` non signé, et `.bat`/`.ps1` téléchargés (bloqués par extension, marque « téléchargé d'Internet »). Parade documentée dans le LISEZ-MOI : « Débloquer » le zip (Propriétés) avant de l'extraire, ce qui retire la marque des fichiers extraits. Seule une signature de code réglerait vraiment la question.
- Testable sous Linux (pwsh en conteneur), contrairement à un `.exe` .NET Framework que Wine fait tourner mal.

**Zéro question si tout va bien.** Les seules questions possibles : jeu introuvable (sélecteur de dossier), jeu ouvert (Réessayer), autre MelonLoader déjà là (remplacer ?), `version.dll` étranger (remplacer ?), réseau en profil Public (passer en Privé ?). Toutes sont journalisées avec la réponse.

**Pare-feu : règle créée à l'installation (UAC), pas la question de Windows au premier lancement.** La question de Windows (« Alerte de sécurité Windows ») s'ouvre **sous** un jeu plein écran (doc Microsoft), donc l'ami ne la voit pas ; et « Annuler » (ou un compte non administrateur) crée des règles de **blocage**, qui l'emportent sur toute règle d'autorisation. L'installeur demande donc l'élévation (UAC, une fois, fenêtre cachée) uniquement pour `Elevated.ps1` : retire les règles de blocage entrantes visant `LMD_Win_x64.exe` (profil Privé), ajoute la règle « LmdMulti - Lonely Mountains: Downhill (UDP entrant) » (groupe `LmdMulti`, programme = exe du jeu, UDP, tous ports car `Port` est réglable, profils Privé et Domaine), et passe en Privé les réseaux acceptés. Si la règle est déjà en place et qu'aucun réseau n'est Public, pas d'UAC (mise à jour = zéro question). UAC refusé : le message final explique quoi cocher si Windows pose la question.

**Profil réseau Public.** En Public, la règle (Privé) ne s'applique pas : les autres joueurs ne nous voient pas. L'installeur liste les connexions actives en Public et propose de les passer en Privé (recommandé à la maison, pas dans un lieu public).

**MelonLoader.** Version installée lue dans les métadonnées de `version.dll` (sinon `MelonLoader/net6/MelonLoader.dll`). Absent → installé ; même version → rien (réparé si incomplet) ; autre version → question (Oui recommandé si plus ancienne, Non si plus récente), l'ancien part à la corbeille ; `version.dll` qui n'est pas MelonLoader → question, Non par défaut (= abandon sans rien modifier). `UserData/` n'est jamais touché (la config `LmdMulti.cfg` est créée par le mod lui-même).

**Désinstalleur.** Retire `Mods/LmdMulti.dll` (corbeille) et la règle du groupe `LmdMulti` (UAC) ; propose de retirer MelonLoader seulement si aucun autre `.dll` dans `Mods/` ou `Plugins/` (sinon le garde sans question) ; garde `UserData/`.

**Journal** : `LmdMulti-installation.log` / `LmdMulti-desinstallation.log` à côté des `.bat` (ou `%TEMP%` si le dossier n'est pas inscriptible) ; l'étape administrateur écrit dans le même fichier.

**Codes de sortie** : 0 réussi, 21-24 annulé (déjà expliqué à l'écran), 9 erreur, 1 PowerShell n'a pas pu lancer le script. Le `.bat` garde la console ouverte (pause) seulement pour 1 à 19.

## Linux / Steam Deck : `install-linux.sh`

Sans sudo, jeu fermé. Trouve le jeu (dossiers Steam natif, Flatpak, Snap ; `libraryfolders.vdf`, `appmanifest_711540.acf`), copie MelonLoader (comparaison octet à octet ; autre version → question) et la DLL, puis règle l'override dans le **registre du préfixe Proton** : `compatdata/711540/pfx/user.reg`, section `[Software\\Wine\\DllOverrides]`, `"version"="native,builtin"` (copie `user.reg.lmdmulti-sauvegarde` avant la première modification). Choisi plutôt que l'option de lancement Steam (`WINEDLLOVERRIDES="version=n,b" %command%`) : celle-ci vit dans `localconfig.vdf`, que Steam réécrit tant qu'il tourne (modifiable seulement Steam fermé, fragile) ; `user.reg` n'est réécrit que par Wine, donc sûr jeu fermé. C'est la méthode utilisée chez Edia. Si le préfixe n'existe pas encore (jeu jamais lancé), le script le dit et donne l'option de lancement en repli. `uninstall` retire la DLL, et sur accord MelonLoader et l'override. À refaire si une mise à jour de Proton recrée le préfixe.

## Tests

```
tests/installer/run-tests.sh [--no-build]
```

Emballe, extrait le zip dans `/tmp/lmd-insttest/`, puis :
1. **Analyse statique** (PSScriptAnalyzer 1.22.0 dans le conteneur `mcr.microsoft.com/powershell`) : syntaxe, commandes et types compatibles **Windows PowerShell 5.1** (profil Windows 10), BOM, erreurs d'analyse. Vérifié qu'elle attrape `??`, le ternaire et un paramètre propre à PS 7.
2. **Scénarios PowerShell** sous pwsh (Linux, sans fenêtres, réponses par `-Answers`), sur une fausse arborescence Steam à deux bibliothèques (chemin avec espaces et accents) : installation neuve (jeu dans la 2e bibliothèque, config existante intacte), réinstallation (« à jour »), jeu introuvable (code 22, dossier choisi à la main, dossier invalide refusé), MelonLoader plus ancien (Oui / défaut Oui, ancien retiré, autres mods signalés et intacts), plus récent (gardé par défaut), `version.dll` étranger (abandon sans rien modifier / remplacement), MelonLoader incomplet (réparé), autre build du jeu (avertissement), désinstallation (Non → MelonLoader gardé ; défaut → retiré, `Mods` vide retiré, réglages gardés ; autre mod présent → gardé sans question), lecture VDF (format récent avec chemins Windows échappés, ancien format).
3. **`install-linux.sh`** sur une fausse arborescence Steam/Proton : installation, override dans la bonne section sans toucher au reste du registre, sauvegarde, idempotence, désinstallation (avec/sans MelonLoader), préfixe absent, autre MelonLoader, jeu introuvable.
4. **Zip reproductible** (deux emballages → même SHA-256).

Hors de portée sous Linux (à tester sur un vrai Windows, voir la liste dans le rapport de livraison) : registre Steam, fenêtres (MessageBox, sélecteur de dossier), UAC, cmdlets du pare-feu et des profils réseau, corbeille, détection du jeu ouvert, SmartScreen / Smart App Control. Sous Wine 9, PowerShell 7 (.NET 8 et .NET 6) ne démarre pas ; seul le `.bat` a pu y être lancé (cas « zip non extrait » vérifié).
