# Distribution d'Ovomium (installeur Windows, Linux)

Objectif : un ami peu technique sous Windows extrait un zip, double-clique, répond « Oui » à Windows, et joue.

## Fabriquer le paquet

```
tools/make-dist.sh             # compile src/Ovomium et src/Ovomium.Updater (Release) puis emballe
tools/make-dist.sh --no-build  # emballe les DLL déjà compilées
```

→ `dist/downhill-ovomium-<version>.zip` (version lue dans `OvomiumMod.Version`) et son SHA-256, plus, à côté, ce que télécharge la mise à jour automatique : `dist/ovomium-update.json` (manifeste), `dist/Ovomium.dll` et `dist/LMD-Camera.dll`. Le manifeste n'est **pas signé** par make-dist (qui marche donc sans la clé) : c'est `tools/release.sh` qui le signe (voir « Publier une mise à jour »). Besoins : `refs/` (compilation), `vendor/MelonLoader.x64-0.7.3.zip` et `vendor/LMD-Camera-1.1.0/LMD-Camera.dll` (téléchargés depuis les releases GitHub officielles s'ils manquent ; empreintes SHA-256 vérifiées, épinglées dans le script). Zip reproductible : ordre trié, dates fixées à celle du dernier commit.

Le script convertit pour Windows : fins de ligne CRLF partout (un .bat en LF casse `goto`), BOM UTF-8 sur les `.ps1` et le `LISEZ-MOI.txt` (sans BOM, Windows PowerShell 5.1 lit en ANSI : accents cassés), et remplace `@MOD_VERSION@`, `@MELON_VERSION@`, `@TESTED_BUILD@`. Les sources dans `installer/` restent en UTF-8/LF sans BOM.

**Après une mise à jour du jeu** : vérifier le mod, puis mettre à jour `TESTED_BUILD` dans `tools/make-dist.sh` (`buildid` de `steamapps/appmanifest_711540.acf`). L'installeur prévient seulement (il ne bloque pas) si le build installé diffère.

## Contenu du zip

```
downhill-ovomium-<v>/
  Installer Ovomium.bat       lance fichiers\scripts\Install.ps1
  Desinstaller Ovomium.bat    lance fichiers\scripts\Uninstall.ps1
  LISEZ-MOI.txt                installer, premier lancement long, commandes, dépannage, désinstaller
  install-linux.sh             Linux / Steam Deck (Proton)
  fichiers/
    Ovomium.dll               le mod (LmdExplorer n'est jamais livré)
    OvomiumUpdater.dll         plugin MelonLoader de mise à jour automatique, installé dans <jeu>/Plugins/
    melonloader/               archive officielle MelonLoader 0.7.3 extraite (version.dll + MelonLoader/)
    MelonLoader-LICENSE.md, MelonLoader-NOTICE.txt   Apache-2.0 (aussi dans melonloader/MelonLoader/Documentation)
    LMD-Camera.dll             mod de vues 1re / 3e personne de flo-st, 1.1.0, tel quel
    LMD-Camera-LICENSE.txt     sa licence MIT (source : installer/licences/, CRLF)
    scripts/                   Common, Steam, Melon, Camera, Updater, Firewall, Install, Uninstall, Elevated (.ps1)
```

Noms de fichiers en ASCII (l'extracteur de zip de Windows gère mal les noms accentués de certains zips).

## Choix techniques

**`.bat` + PowerShell plutôt qu'un `.exe`.** Windows PowerShell 5.1 est présent sur tout Windows 10/11 ; le `.bat` lance `powershell.exe -NoProfile -ExecutionPolicy Bypass -File …` (Bypass : pas de blocage du script téléchargé, sauf stratégie de groupe d'entreprise). Raisons :
- Un `.exe` non signé déclenche SmartScreen (« Windows a protégé votre ordinateur ») **et** est une cible classique des faux positifs de Defender par apprentissage automatique (`Wacatac!ml`…). Un `.bat` peut aussi déclencher SmartScreen ou l'avertissement « Ouvrir le fichier » (sources divergentes, voir plus bas), mais le contenu reste lisible : n'importe qui peut vérifier ce qu'il fait.
- Smart App Control (Windows 11, activé sur certaines installations neuves) bloque **les deux** sans bouton de contournement : `.exe` non signé, et `.bat`/`.ps1` téléchargés (bloqués par extension, marque « téléchargé d'Internet »). Parade documentée dans le LISEZ-MOI : « Débloquer » le zip (Propriétés) avant de l'extraire, ce qui retire la marque des fichiers extraits. Seule une signature de code réglerait vraiment la question.
- Testable sous Linux (pwsh en conteneur), contrairement à un `.exe` .NET Framework que Wine fait tourner mal.

**Zéro question si tout va bien.** Les seules questions possibles : jeu introuvable (sélecteur de dossier), jeu ouvert (Réessayer), autre MelonLoader déjà là (remplacer ?), `version.dll` étranger (remplacer ?), réseau en profil Public (passer en Privé ?). Toutes sont journalisées avec la réponse.

**Pare-feu : règle créée à l'installation (UAC), pas la question de Windows au premier lancement.** La question de Windows (« Alerte de sécurité Windows ») s'ouvre **sous** un jeu plein écran (doc Microsoft), donc l'ami ne la voit pas ; et « Annuler » (ou un compte non administrateur) crée des règles de **blocage**, qui l'emportent sur toute règle d'autorisation. L'installeur demande donc l'élévation (UAC, une fois, fenêtre cachée) uniquement pour `Elevated.ps1` : retire les règles de blocage entrantes visant `LMD_Win_x64.exe` (profil Privé), ajoute la règle « Ovomium - Lonely Mountains: Downhill (UDP entrant) » (groupe `Ovomium`, programme = exe du jeu, UDP, tous ports car `Port` est réglable, profils Privé et Domaine), et passe en Privé les réseaux acceptés. Si la règle est déjà en place et qu'aucun réseau n'est Public, pas d'UAC (mise à jour = zéro question). UAC refusé : le message final explique quoi cocher si Windows pose la question.

**Profil réseau Public.** En Public, la règle (Privé) ne s'applique pas : les autres joueurs ne nous voient pas. L'installeur liste les connexions actives en Public et propose de les passer en Privé (recommandé à la maison, pas dans un lieu public).

**MelonLoader.** Version installée lue dans les métadonnées de `version.dll` (sinon `MelonLoader/net6/MelonLoader.dll`). Absent → installé ; même version → rien (réparé si incomplet) ; autre version → question (Oui recommandé si plus ancienne, Non si plus récente), l'ancien part à la corbeille ; `version.dll` qui n'est pas MelonLoader → question, Non par défaut (= abandon sans rien modifier). `UserData/` n'est jamais touché (la config `Ovomium.cfg` est créée par le mod lui-même).

**LMD-Camera (vues à la première et à la troisième personne).** Mod MelonLoader de flo-st (https://github.com/flo-st/LMD-Camera, licence MIT), livré **tel quel** : release officielle 1.1.0 (tag = commit `84785d8`), empreinte SHA-256 épinglée dans `make-dist.sh` (`CAMERA_SHA256`), licence copiée à côté (`installer/licences/LMD-Camera-LICENSE.txt`, exigence de la MIT) et créditée dans le LISEZ-MOI. Les touches 1/2/3 et `UserData/LMD-Camera.cfg` sont celles du mod. L'installeur copie `Mods/LMD-Camera.dll` en écrasant toute version existante (journalisé : « identique » ou « remplace une autre version »). **Règle de désinstallation** : le fichier n'est retiré que s'il est identique à `fichiers/LMD-Camera.dll` du paquet (SHA-256 sous Windows, `cmp` sous Linux) ; sinon c'est une copie du joueur, gardée et signalée (journal + message final). Comparer au fichier livré plutôt qu'à une empreinte recopiée dans les scripts : rien à dupliquer ni à tenir à jour. Limite : désinstaller avec un paquet livrant une autre version de LMD-Camera que celle installée garde le fichier. Ce retrait a lieu **avant** le test « autres mods », pour que notre copie ne retienne pas MelonLoader.

**Plugin de mise à jour (`Plugins/OvomiumUpdater.dll`).** Copié à chaque installation (dossier `Plugins/` créé si besoin). Une installation par-dessus efface une mise à jour téléchargée en attente (`UserData/Ovomium/update/`, `update.tmp/`) : elle serait plus ancienne que le paquet installé à la main (le plugin refuse déjà un retour arrière ; double sécurité). Il ne compte pas comme « autre mod » : sinon il empêcherait de retirer MelonLoader à la désinstallation.

**Désinstalleur.** Retire `Mods/Ovomium.dll` (corbeille), `Mods/LMD-Camera.dll` s'il est identique à la copie livrée (voir ci-dessus), `Plugins/OvomiumUpdater.dll` et l'état de l'updater dans `UserData/Ovomium/` (`update/`, `update.tmp/`, `update-state.json`, `installed.json` ; le dossier s'il est vide), et la règle du groupe `Ovomium` (UAC) ; propose de retirer MelonLoader seulement si aucun autre `.dll` dans `Mods/` ou `Plugins/` (sinon le garde sans question) ; garde les réglages (`UserData/Ovomium.cfg`) et le reste d'`UserData/`.

**Journal** : `Ovomium-installation.log` / `Ovomium-desinstallation.log` à côté des `.bat` (ou `%TEMP%` si le dossier n'est pas inscriptible) ; l'étape administrateur écrit dans le même fichier.

**Codes de sortie** : 0 réussi, 21-24 annulé (déjà expliqué à l'écran), 9 erreur, 1 PowerShell n'a pas pu lancer le script. Le `.bat` garde la console ouverte (pause) seulement pour 1 à 19.

## Linux / Steam Deck : `install-linux.sh`

Sans sudo, jeu fermé. Trouve le jeu (dossiers Steam natif, Flatpak, Snap ; `libraryfolders.vdf`, `appmanifest_711540.acf`), copie MelonLoader (comparaison octet à octet ; autre version → question), la DLL, LMD-Camera et le plugin de mise à jour (mêmes règles que sous Windows, voir plus haut), puis règle l'override dans le **registre du préfixe Proton** : `compatdata/711540/pfx/user.reg`, section `[Software\\Wine\\DllOverrides]`, `"version"="native,builtin"` (copie `user.reg.ovomium-sauvegarde` avant la première modification). Choisi plutôt que l'option de lancement Steam (`WINEDLLOVERRIDES="version=n,b" %command%`) : celle-ci vit dans `localconfig.vdf`, que Steam réécrit tant qu'il tourne (modifiable seulement Steam fermé, fragile) ; `user.reg` n'est réécrit que par Wine, donc sûr jeu fermé. C'est la méthode utilisée chez Edia. Si le préfixe n'existe pas encore (jeu jamais lancé), le script le dit et donne l'option de lancement en repli. `uninstall` retire la DLL, LMD-Camera s'il est identique à la copie livrée, le plugin de mise à jour et son état, et sur accord MelonLoader et l'override. À refaire si une mise à jour de Proton recrée le préfixe.

**Pare-feu ufw** (activé chez Edia sous Mint ; vu au premier test à 2 PC, le 2026-10-26 : l'amie voyait Edia, mais Edia ne la voyait pas) : ses règles ne sont lisibles qu'en root, donc le script lit seulement `ENABLED=yes` dans `/etc/ufw/ufw.conf` (`OVOMIUM_UFW_CONF` pour les tests). Si ufw est actif, il affiche `sudo ufw allow <Port>/udp`, avec le port d'`Ovomium.cfg` (47700 par défaut), sans rien lancer lui-même.

## Publier une mise à jour (mise à jour automatique signée)

Les joueurs n'ont plus à réinstaller : au lancement du jeu, le plugin `OvomiumUpdater.dll` regarde la dernière release de `Delocca/downhill-ovomium`, télécharge `ovomium-update.json` et sa signature `ovomium-update.json.sig`, vérifie la signature (Ed25519, clé publique intégrée au mod) puis les SHA-256 des DLL, et installe la mise à jour ; le joueur relance le jeu quand le message le demande. Sans la bonne signature, rien n'est installé : seule la personne qui a la **clé privée** peut publier une mise à jour.

Le dépôt GitHub reste **privé** la plupart du temps : tant qu'il l'est, le plugin ne voit pas les releases (réponse 404) et ne fait rien. On l'ouvre le temps que tout le monde se mette à jour, puis on le referme.

Toutes les commandes se lancent dans un terminal ouvert dans le dossier du projet (`lonely_moutain`), **hors du bac à sable de Claude** (elles ont besoin de `gh` et du réseau).

### Une seule fois

1. **Sauvegarder la clé privée** `~/.config/ovomium/release-key.pem` hors de ce PC : clé USB rangée, ou gestionnaire de mots de passe (pièce jointe). Jamais dans le dépôt, jamais sur GitHub ni dans un message. Clé perdue : les mods déjà installés n'accepteront plus aucune mise à jour (il faudrait réinstaller à la main chez tout le monde, avec un mod contenant une nouvelle clé). Clé volée : changer de clé de la même façon. `tools/release.sh` vérifie à chaque fois que la clé correspond à la clé publique du mod (`dseNI/klJpWEjY/3pE+HWOROQNvyUqUIdLyUld81pF8=`).
2. **Connecter `gh`** à GitHub : `gh auth login` (GitHub.com, HTTPS, navigateur). Vérifier : `gh auth status`.
3. **Créer le dépôt, privé** : `gh repo create Delocca/downhill-ovomium --private --source . --remote origin`. Avant de l'ouvrir la première fois, se demander si tout l'historique peut être public (il le sera pendant les fenêtres de mise à jour).

### À chaque mise à jour

1. **Augmenter la version** dans `src/Ovomium/OvomiumMod.cs` (`const string Version = "0.5.0"`, par exemple). Le plugin refuse une version égale ou plus ancienne que celle installée.
2. **Écrire les nouveautés** dans `CHANGELOG.md`, sous `## Non publié` (une ligne par changement, `- …`). Ce texte devient la description de la release. Si le fichier n'existe pas, `tools/release.sh` le crée et s'arrête pour que tu le remplisses.
3. **Commiter** tout (`git status` doit être propre).
4. **Essai à blanc** : `tools/release.sh --dry-run`. Il fait tout, sauf ce qui laisse une trace : vérifications, notes, paquet (`make-dist`), signature vérifiée ; ni commit, ni tag, ni envoi. Lire les messages : chaque étape est annoncée, la première erreur arrête tout avec une explication.
5. **Publier** : `tools/release.sh`. Il vérifie (arbre propre, remote `origin`, tag `v<version>` libre localement et sur GitHub, `gh` connecté, dépôt et clé publique présents dans le code du mod, clé privée qui correspond), fabrique le paquet, signe `dist/ovomium-update.json` et vérifie la signature, date la section du changelog (`## 0.5.0 — <date>`, commit « Changelog 0.5.0 »), crée le tag annoté `v0.5.0`, pousse, puis crée la release avec `downhill-ovomium-<v>.zip`, `ovomium-update.json`, `ovomium-update.json.sig`, `Ovomium.dll` et `LMD-Camera.dll`. Notes à la main plutôt que le changelog : `tools/release.sh --notes "Texte"` ou `tools/release.sh notes.md`.
6. **Ouvrir le dépôt** : `tools/repo-visibility.sh public` (le script de publication le rappelle à la fin). Prévenir les amis : lancer le jeu une fois, puis le relancer quand le message le demande.
7. **Refermer le dépôt** quand tout le monde l'a eue : `tools/repo-visibility.sh private`. État actuel : `tools/repo-visibility.sh status`.

Un nouvel ami installe toujours avec le zip (lien de la release, à télécharger pendant que le dépôt est ouvert, ou envoyé directement) ; les mises à jour suivantes lui arrivent seules.

**Plugin modifié de façon incompatible** : `minUpdater` du manifeste vaut la constante `UpdaterVersion` du plugin (lue dans `src/Ovomium.Updater/*.cs` par `make-dist`). L'augmenter fait refuser la mise à jour par les plugins plus anciens : ces joueurs-là devront réinstaller avec le zip.

**Si la publication échoue en route** : rien n'est modifié avant la signature vérifiée. Après le commit du changelog, une erreur (réseau, `gh`) peut laisser le commit et le tag en local : corriger la cause, puis, si le tag n'a pas été poussé, `git tag -d v<version>` et remettre la section en `## Non publié` (ou publier avec `--notes`). Si le tag est déjà sur GitHub mais que la release manque, ne pas bricoler : demander de l'aide (Claude).

## Tests

```
tests/installer/run-tests.sh [--no-build]
```

Emballe, extrait le zip dans `/tmp/lmd-insttest/`, puis :
1. **Analyse statique** (PSScriptAnalyzer 1.22.0 dans le conteneur `mcr.microsoft.com/powershell`) : syntaxe, commandes et types compatibles **Windows PowerShell 5.1** (profil Windows 10), BOM, erreurs d'analyse. Vérifié qu'elle attrape `??`, le ternaire et un paramètre propre à PS 7.
2. **Scénarios PowerShell** sous pwsh (Linux, sans fenêtres, réponses par `-Answers`), sur une fausse arborescence Steam à deux bibliothèques (chemin avec espaces et accents) : installation neuve (jeu dans la 2e bibliothèque, config existante intacte), réinstallation (« à jour »), jeu introuvable (code 22, dossier choisi à la main, dossier invalide refusé), MelonLoader plus ancien (Oui / défaut Oui, ancien retiré, autres mods signalés et intacts), plus récent (gardé par défaut), `version.dll` étranger (abandon sans rien modifier / remplacement), MelonLoader incomplet (réparé), autre build du jeu (avertissement), désinstallation (Non → MelonLoader gardé ; défaut → retiré, `Mods` vide retiré, réglages gardés ; autre mod présent → gardé sans question), LMD-Camera (copié, « identique » à la réinstallation, ancienne version écrasée ; à la désinstallation retiré s'il est identique et ne retient alors pas MelonLoader, gardé et signalé s'il diffère, MelonLoader alors gardé sans question), lecture VDF (format récent avec chemins Windows échappés, ancien format), plugin de mise à jour (copié dans `Plugins/`, mise à jour en attente effacée à la réinstallation, retiré avec son état à la désinstallation, réglages gardés, ne retient pas MelonLoader).
3. **`install-linux.sh`** sur une fausse arborescence Steam/Proton : installation, override dans la bonne section sans toucher au reste du registre, sauvegarde, idempotence, désinstallation (avec/sans MelonLoader), LMD-Camera (mêmes cas que sous PowerShell), préfixe absent, autre MelonLoader, jeu introuvable, ufw actif (port de la config ou par défaut) ou inactif, plugin de mise à jour (mêmes cas que sous PowerShell).
4. **Paquet** : `LMD-Camera.dll` identique à `vendor/` et licence présente ; `OvomiumUpdater.dll` présent ; manifeste compact (clés dans l'ordre, version du zip, SHA-256 des DLL nues de `dist/`, identiques à celles du zip) ; zip et manifeste reproductibles (deux emballages → mêmes octets ; échoue à tort si la DLL du mod est recompilée pendant les tests).

```
tests/release/release-tests.sh
```

Publication (`tools/release.sh`) sans GitHub ni vraie clé, dans une copie du dépôt sous `/tmp/lmd-reltest/` (DLL factices, clé Ed25519 jetable, faux `gh` qui journalise, `origin` = dépôt nu local) : essai à blanc (signature de 64 octets vérifiée par `openssl`, ni commit ni tag ni gh, changelog intact, rappel d'ouvrir le dépôt), autre clé privée refusée, arbre modifié refusé, publication (tag annoté poussé sur le commit « Changelog », changelog daté, release avec les 5 fichiers et les notes de « Non publié », rappel dépôt privé), version déjà publiée refusée, `CHANGELOG.md` absent (créé, arrêt).

Hors de portée sous Linux (à tester sur un vrai Windows, voir la liste dans le rapport de livraison) : registre Steam, fenêtres (MessageBox, sélecteur de dossier), UAC, cmdlets du pare-feu et des profils réseau, corbeille, détection du jeu ouvert, SmartScreen / Smart App Control. Sous Wine 9, PowerShell 7 (.NET 8 et .NET 6) ne démarre pas ; seul le `.bat` a pu y être lancé (cas « zip non extrait » vérifié).
