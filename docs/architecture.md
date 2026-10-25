# Architecture d'Ovomium (v0.4 : fantômes réseau, mode course, démarrage rapide, AZERTY, vue 1re personne)

Ovomium (anciennement LmdMulti) est un ensemble d'améliorations pour Lonely Mountains: Downhill 1.6.0, sous forme de mod MelonLoader. Multijoueur : chaque joueur simule son propre vélo ; les autres apparaissent comme des marionnettes sans collision, avec leur pseudo au-dessus. N'importe qui peut lancer une course (F1, ou Select + A à la manette) : les autres reçoivent une invitation, la piste se charge toute seule chez ceux qui acceptent, départ commun après un compte à rebours, classement en direct et tableau final (voir « Mode course »).

## Dossiers

| Dossier | Rôle |
|---|---|
| `src/Ovomium.Core/` | Cœur sans Unity, partagé par le mod, le bot et les tests : protocole, réseau, interpolation, enregistrements |
| `src/Ovomium.Core/Protocol/` | Format binaire des messages (`Messages`, `PacketIO`, `QuatCodec`, `ProtocolInfo` = version, squelette transmis) |
| `src/Ovomium.Core/Net/` | `UdpNode` (socket UDP + fil réseau), `PeerTable` (pairs connus), `Endpoints` (broadcast, IP manuelles), `ReliableChannel` (messages fiables), `ClockSync` (décalage d'horloge entre pairs) |
| `src/Ovomium.Core/Race/` | Course sans Unity : `RaceHost` / `RaceGuest` (machines à états de l'hôte et d'un invité), `RaceLobby` (course en cours et aiguillage des messages, commun au mod et au bot), `RaceInvites` (arbitrage des invitations), `RaceMessages` (format), `RaceTypes` (règles, phases, classement), `RaceButtons` (commandes) |
| `src/Ovomium.Core/Poses/` | `PoseFrame` (une pose), `SnapshotBuffer` (interpolation retardée), `PoseRecorder` (fichiers .lmdrec) |
| `src/Ovomium/` | Le mod : `OvomiumMod` (point d'entrée, relie tout), `Config` (MelonPreferences), `KeySetting` (touche réglable), `Log` |
| `src/Ovomium/Game/` | Lecture de l'état du jeu : piste en cours, nom de piste, pseudo Steam |
| `src/Ovomium/Ghosts/` | Capture de la pose locale (`PoseCapture`, `LocalSender`), marionnettes (`Puppet`, `GhostManager`), squelette (`BoneRig`), aspect et couleur des fantômes (`GhostLook`) |
| `src/Ovomium/Race/` | Course côté jeu : `RaceController` (session, commandes, réseau), `PadReader` (manette via Rewired), `RaceKeys` (noms des commandes à l'écran), `RaceRun` (ma participation : chargement, blocage au départ, sections, arrivée), `TrackLoader` (amener le joueur au départ d'une piste), `TrackAccess` (sélection, verrou de piste contourné), `RaceGame` (appels et patchs du jeu), `RaceHud` (affichage) |
| `src/Ovomium/UI/` | OnGUI : panneau des joueurs, pseudos au-dessus des fantômes, notifications |
| `src/Ovomium/Tweaks/` | Confort hors multijoueur : `IntroSkip` (démarrage direct au menu, `SkipIntro`), `KeyLayout` + `KeyboardRemap` (touches lettres du jeu adaptées à la disposition du clavier, `KeyboardLayout`) |
| `src/Ovomium/View/` | Vue : `LmdCameraBridge` (pont par réflexion vers le mod LMD-Camera de flo-st, livré à côté), `CameraToggle` (bascule 1re personne, touche `CameraKey` / R3), `LmdVrBridge` (compatibilité avec le mod LMD-VR : touches, VR démarrée) |
| `src/Ovomium.Core/Keys/` | `KeyChoice` : touche effective (réglage, défaut, défaut avec LMD-VR) et conflits avec LMD-VR |
| `src/Ovomium/Diagnostics/` | Statistiques (réseau, images/s, coût du mod), relevé des os en mode `DebugLog`, `TestMode` (aucun résultat enregistré par le jeu pendant les tests) |
| `tools/LmdBot/` | Faux joueur(s) en console (Linux) : orbite autour du joueur ou rejeu d'un .lmdrec, jusqu'à 64 dans un processus ; pair de course simulé (`RaceBot`) |
| `tools/lmd-virtual.sh`, `tools/lmd-keys.sh` | Jeu sur écran virtuel (`:98`) et pilotage au clavier : entrer en piste, rouler en filmant, captures |
| `tests/Ovomium.Tests/` | Tests xUnit du cœur (`dotnet test tests/Ovomium.Tests`) |
| `src/LmdExplorer/` | Outil de dev séparé (exploration de l'API du jeu), pas livré aux joueurs |
| `installer/` | Installeur Windows (`.bat` + scripts PowerShell), désinstalleur, `LISEZ-MOI.txt`, `install-linux.sh` ; emballés par `tools/make-dist.sh` dans `dist/downhill-ovomium-<version>.zip`, testés par `tests/installer/run-tests.sh`. Voir `docs/distribution.md` |

Le mod compile les sources du cœur directement (fichiers liés dans `Ovomium.csproj`) : **une seule DLL**, `Ovomium.dll`, à copier dans `<jeu>/Mods/` (le paquet y ajoute `LMD-Camera.dll`, mod tiers facultatif : voir « Démarrage, clavier, vue »). Aucune dépendance externe : UDP maison, avec une petite couche fiable (`ReliableChannel`) pour les rares messages de course ; les poses, envoyées en continu, n'en ont pas besoin.

## Flux

```
Fil principal (Unity, OnUpdate)                     Fil réseau (UdpNode)
 RefreshLocal : piste + pseudo ── SetLocal ──────▶   annonce ~1 Hz : broadcast + IP manuelles + pairs connus
 LocalSender : capture 25 Hz ── SendPose(octets) ─▶  unicast à chaque pair (+ boucle locale si écho)
 DrainEvents ◀──────────── NetEvent (file) ───────   réception, décodage, ping/pong, expiration (5 s)
 GhostManager : tampon + interpolation → marionnettes
 OnGUI : pseudos, panneau, notifications
```

- Le fil réseau ne touche jamais à Unity ; il dépose des `NetEvent` (pair arrivé/changé/parti, pose décodée, message de journal) dans une file que le fil principal vide à chaque image.
- Capture et application des poses se font dans `OnUpdate`, avant l'évaluation des contraintes Unity : cheveux et phares de la marionnette suivent ses os dans la même image.
- Une marionnette n'existe que si le pair est **sur la même piste que nous** et émet encore ; elle est détruite sinon (menu, autre piste, départ).

## Découverte

- Tout le monde écoute sur le port `Port` (47700 par défaut). S'il est pris, le mod essaie les 9 suivants.
- Le mod ne s'annonce qu'une fois son pseudo connu (Steam prêt, ou 20 s au plus, puis « Joueur-NNNN »).
- Annonces envoyées à 255.255.255.255 **et** au broadcast dirigé de chaque interface (Windows n'envoie 255.255.255.255 que sur une seule interface, parfois le VPN).
- Annonces aussi envoyées en unicast aux IP manuelles (`ManualPeers`, ex. Tailscale) et à chaque pair connu : il suffit qu'un des deux entende l'autre pour que les deux se découvrent.
- Les poses partent en unicast vers chaque pair (le broadcast Wi-Fi à 25 Hz est peu fiable).

## Protocole (version 2)

Petit-boutiste. En-tête de 12 octets **figé pour toutes les versions** : `'L' 'M'`, version (u8), type (u8), id d'instance de l'émetteur (u64, aléatoire à chaque lancement). Un paquet d'une autre version n'est jamais décodé : le pair est listé « version incompatible » et une notification s'affiche. La v2 (mod 0.2.0) n'est donc pas compatible avec la v1 (mod 0.1.0) : tout le monde doit mettre à jour, et le panneau le dit.

| Type | Contenu après l'en-tête |
|---|---|
| 1 Announce | SteamId u64, pseudo (u8 longueur + UTF-8, ≤ 48 o), piste (≤ 32 o, vide au menu), en piste u8, version du mod (≤ 16 o) |
| 2 Pose | temps émetteur ms u32, drapeaux u8 (1 = ragdoll, 2 = os présents), racine : position 3×f32 + rotation u32 ; si os : 31 rotations u32, masque de positions u32, puis 3×half par os du masque |
| 3 Ping | temps de l'émetteur u32 |
| 4 Pong | temps du ping u32 (renvoyé tel quel), heure du répondeur u32 (v2) |
| 5 Bye | rien : départ propre, le fantôme disparaît tout de suite |
| 6 Reliable (v2) | flux u32, numéro u32, contenu (message de course) |
| 7 Ack (v2) | flux u32, numéro u32 accusé |

**Messages fiables** (`ReliableChannel`) : chaque message vers un pair reçoit un numéro (1, 2, 3…), est renvoyé toutes les 150 ms jusqu'à son accusé, abandonné après 15 s. Le récepteur accuse tout, livre dans l'ordre, ignore les doublons ; un trou qui dure plus de 3 s est sauté (l'émetteur a abandonné). Le « flux » est un nombre aléatoire tiré par l'émetteur pour chaque pair : s'il oublie ce pair (départ, expiration) et recommence à 1, le nouveau flux remet la réception à zéro au lieu de passer pour des doublons. Testé avec 30 % de paquets perdus (tests, `LmdBot --loss 0.3`).

**Horloges** : chaque pong donne un échantillon de décalage (heure du répondeur − (envoi du ping + aller-retour / 2)) ; `ClockSync` garde celui de plus petit aller-retour parmi les 12 derniers (erreur ≤ moitié de cet aller-retour, ~1 ms en LAN).

- **Racine** = monde de `bike/Bind_JNT` (vélo normal) ou de `RagDoll(Clone)/bike/Bind_JNT` (pendant un crash) : même arbre, mêmes noms d'os.
- **Rotations** : « smallest three » sur 32 bits (erreur < 0,25°).
- **Positions locales** : seulement pour les os qui translatent en roulant (`Skeleton.DefaultPositionMask`, relevé en conduite réelle : cog, spine_1/2/3, épaules, mains, roue avant ; suspension et pédalier par prudence). Pendant un crash, toutes (le ragdoll place chaque os par la physique). En `DebugLog`, toutes, pour relever lesquels bougent vraiment. Le masque est libre côté émetteur : le changer ne casse pas la compatibilité.
- Taille : environ 250 octets par pose (≈ 420 pendant un crash ou en DebugLog). À 25 Hz et 15 joueurs : ≈ 95 Ko/s reçus par joueur.

## Interpolation (`SnapshotBuffer`)

Style Gaffer On Games : on affiche chaque pair tel qu'il était il y a `InterpolationDelayMs` (100 ms). L'horloge de l'émetteur est recalée sur la nôtre par le décalage minimal (réception − émission) vu sur la fenêtre gardée. On interpole entre les deux poses qui encadrent l'instant visé (positions linéaires, rotations nlerp). Sans pose plus récente, on fige la dernière. Un saut de plus de 15 m (respawn) n'est pas interpolé. Si l'horloge de l'émetteur repart en arrière (redémarrage), le tampon est vidé.

## Mode course

**Commandes** (pas de configuration nécessaire ; logique dans `Core/Race/RaceButtons`, testée) :

| Action | Clavier | Manette |
|---|---|---|
| Lancer une course sur la piste où l'on est (ou celle sélectionnée au menu) / accepter l'invitation / (hôte) partir sans attendre / fermer les résultats | **F1** (appui bref) | **Select** (View/Back) maintenu + **A** |
| Abandonner / (hôte) annuler avant le départ | **F1 maintenu 1,5 s** | **Select + B maintenus 1,5 s** |
| Refuser une invitation | rien : elle expire (15 s), le bandeau le dit | idem |

- Anti-erreur : une jauge « Abandon… » (ou « Annulation de la course… ») se remplit au centre ; relâcher avant la fin ne fait rien. Une seule touche au clavier (plus de seconde touche « abandonner » à côté) : quand seul « lancer » a un sens, F1 agit dès l'appui ; quand seul « abandonner » en a un, un appui bref affiche « Pour abandonner : F1 maintenu 1,5 s ».
- Manette (`Race/PadReader`, via Rewired, la bibliothèque d'entrées du jeu, `game-api.md` § 9) : dans le jeu, A = sprint / valider, B = crash volontaire / retour, Back = pause en piste (comme Start). Le mod retire donc Back des cartes manette du jeu (en mémoire, Start met toujours en pause) et coupe les cartes manette tant que Select est tenu, jusqu'au relâchement de tous les boutons : le jeu ne voit ni le A ni le B des combinaisons (vérifié avec une manette virtuelle : A ne valide rien au menu, B ne crashe pas, Select n'ouvre pas la pause). Presser Select **avant** A ou B. Pendant la combinaison, direction et gaz sont coupés.
- Les textes (bandeau, panneau de course, panneau des joueurs, jauge) affichent les commandes du dernier périphérique utilisé (`Race/RaceKeys` : manette selon Rewired, clavier dès qu'on touche F1).

**Déroulé** (logique dans `Core/Race`, testée sans Unity ; `RaceTypes.RaceRules` pour les valeurs) :
1. **Invitation** : celui qui appuie sur F1 devient l'hôte et invite tous les joueurs compatibles détectés (16 coureurs au plus). Bandeau central chez eux : hôte, piste, « F1 rejoindre » (ou « Select + A »), « sans réponse : refusée dans 15 s ». Un joueur encore en lice dans une course refuse automatiquement ; un invité déjà arrivé ou qui a abandonné reçoit l'invitation (l'hôte, lui, reste occupé jusqu'à la fin : il mène sa course).
   - **Lancements simultanés** (`RaceInvites`, testé) : deux joueurs qui appuient sur F1 au même moment s'invitent l'un l'autre avant d'avoir vu l'invitation de l'autre. Règle identique partout, sans échange : priorité à l'hôte d'identifiant d'instance le plus petit. L'autre annule sa course toute neuve (« la sienne est retenue ») et reçoit l'invitation, acceptée d'office si c'est la même piste ; un tiers garde l'invitation prioritaire quel que soit l'ordre d'arrivée. Une course dont un invité a déjà accepté n'est jamais abandonnée.
2. **Chargement** : chez chaque coureur (hôte compris), `TrackLoader` amène au départ, d'où qu'il soit, une fois fini un éventuel chargement en cours du jeu (jamais deux changements de scène à la fois) : même piste → « recommencer la tentative » (vérifié aussi depuis le menu pause) ; autre piste → retour au menu ; écran titre → « commencer » ; puis sélection, 0,6 s pour le menu, et lancement. Une piste verrouillée chez lui est ouverte temporairement (`TrackAccess`, voir `game-api.md` § 4) ; une fois prêt au départ, la sélection du menu (et du profil) redevient celle d'avant la course. Une montagne non possédée (DLC) est refusée avec un message.
3. **Attente** : vélo bloqué au départ (patch de `LockMovement(false)`) ; « prêt » 1,5 s après la disparition de l'écran de chargement. Chaque invité a 30 s depuis son acceptation pour être prêt, sinon il est exclu (« la course est partie sans toi »). L'hôte peut partir sans attendre (F1). Personne de prêt : course annulée.
4. **Départ** : l'hôte fixe l'heure du départ dans son horloge (maintenant + 4,5 s), chacun la convertit dans la sienne grâce au décalage mesuré (`ClockSync`) ; « Prêts ? », puis 3-2-1 géant au centre, puis « GO ! » : déblocage du vélo. Mesuré en local : GO à 7-8 ms de l'heure prévue.
5. **Course** : chrono du mod (depuis le départ commun, il continue pendant crashs et restarts) ; sections franchies et arrivée signalées par patchs du tracker de manche du jeu. Classement en direct : arrivés par temps, puis en course par checkpoints franchis (le premier à les franchir devant), puis abandons. Quitter la piste ou le jeu = abandon.
6. **Fin** : quand tout le monde est arrivé ou a abandonné, ou 60 s après la première arrivée (les autres : abandon), ou 30 min au plus. Tableau final (place, temps, écart au premier, abandons) au centre pendant 30 s (F1 pour fermer).

**Départs et coupures** (scénarios testés dans `RaceRobustnessTests` et `RaceNetworkTests`) :
- **L'hôte quitte** (jeu fermé : vu après 5 s de silence ; départ propre : « Bye », tout de suite). Avant le départ : course annulée chez les invités (« L'hôte a quitté »), vélo débloqué. Après le départ fixé (compte à rebours ou course) : chacun finit sa descente seul (départ commun conservé, chrono local, classement figé, « hôte parti » dans le panneau), puis résultats partiels (« l'hôte a quitté pendant la course »). Si l'hôte revient (coupure réseau), il reprend la main.
- **Un invité se coupe** : exclu avant le départ, abandon après ; s'il revient (même instance du jeu, coupure réseau), il retrouve sa place si la course le permet, et une arrivée faite pendant la coupure est redite à l'hôte. S'il a de son côté annulé (hôte cru parti avant le départ), il le dit à l'hôte, qui ne l'attend pas. Un jeu relancé est un nouveau joueur.
- **Message perdu** : un invité exclu qui se manifeste encore est prévenu ; un invité qui ne voit jamais le départ abandonne seul 90 s après la fin du délai de réponse (« L'hôte ne répond plus »).
- **Arrivée pendant une course** : le nouveau venu voit les fantômes, n'est pas invité ; s'il lance sa propre course, les coureurs en lice la refusent d'office.
- **Version différente** : pair listé « version incompatible », jamais invité ni décodé.
- **Charge** : 15 joueurs dont 8 en course, 20 % de paquets perdus (vrais nœuds UDP sur la boucle locale) : départ commun à ±30 ms, classement complet chez tous.

**Messages de course** (dans un message fiable ; type u8, id de course u32, puis) : `Invite` piste, pseudo de l'hôte ; `Reply` accepte u8 ; `Status` (invité → hôte) état u8, checkpoints u8, temps ms u32 ; `Standings` (hôte → coureurs, à chaque changement) révision u32, phase u8, raison u8, piste, id de l'hôte u64, heure de l'hôte, départ, fin (u32 ms de l'horloge de l'hôte, 0 = aucune), puis par coureur id u64, pseudo (≤ 32 o), état, checkpoints, temps ; `Cancel` raison u8. L'hôte fait foi ; un invité garde son propre état s'il est en avance sur le dernier classement reçu (ex. arrivé localement).

**Choix écartés** : départ par « dans N ms » (faussé par les renvois de messages), horloge commune par NTP (inutile : le ping existant suffit), chrono et arrivée lus dans les champs du tracker du jeu (ils gardent la manche précédente tant que le joueur n'a pas démarré : fausse arrivée observée), « prêt » sur la fin de l'intro (le jeu ne libère le vélo qu'au premier appui du joueur).

## Démarrage, clavier, vue

**Démarrage rapide** (`Tweaks/IntroSkip`, `SkipIntro` vrai par défaut ; séquence relevée dans `game-api.md` § 10). Prefix sur la mise en place de l'écran de logos (`SplashScreen`) : délais et durées à zéro, logos masqués. Puis, à l'écran titre (`MainMenuScreen`), une fois par lancement : le mod attend la fin de son animation d'arrivée (`prop_Boolean_0` faux, 2 s au plus) et fait comme un appui (drapeau « attend un appui » baissé, `Method_Private_Void_2`, `ShowButtonList`) : boutons affichés, premier appui = « Commencer la partie ». Chargement du profil et connexion au serveur de classement inchangés. Pas de logo Unity dans cette build ; les ~8 s d'écran noir du début (Proton, MelonLoader) sont avant tout code de mod. Mesuré sur écran virtuel (2026-10-25, depuis le lancement de Proton) : écran titre à ~25-27 s puis appui nécessaire → menu prêt à ~16 s, sans appui (logos ~10,7 s supprimés, attente de la fin de l'animation du titre ~0,6 s ajoutée). Reste : « Connecté » affiché 1,5 s par le jeu, chargement des scènes du menu (~2 s).

**Clavier** (`Tweaks/KeyLayout`, `Tweaks/KeyboardRemap`, `KeyboardLayout` = `auto` par défaut). Rewired lit les touches par leur lettre (code de touche virtuel), pas leur position : en AZERTY, « accélérer » (W) est en bas à gauche [vérifié sous Proton avec `setxkbmap fr` : Z inopérant, W fait rouler]. En `auto`, pour chaque position de lettre QWERTY (scancode), `MapVirtualKeyExW` donne la lettre de la disposition active ; les cartes clavier Rewired du joueur (toutes catégories) sont refaites en mémoire avec ces lettres (suppression puis recréation des éléments déplacés ; A et Q s'échangent). Les cartes rechargées par le jeu sont reprises (scan chaque seconde, cartes déjà faites gardées référencées). Sous Wine, `GetKeyboardLayout` renvoie 0x04090409 même en AZERTY, mais `MapVirtualKeyExW` suit la disposition X : seule la seconde fait foi. Disposition lue une fois par lancement. Vérifié : AZERTY détecté, ZQSD roule et dirige, W inopérant ; QWERTY : rien de modifié. Non vérifié sur un vrai Windows (même mécanisme attendu : codes virtuels). Touches des mods (F1-F4, V, 1/2/3 de LMD-Camera) : `Input.GetKeyDown` d'Unity ; chiffres reconnus en AZERTY sans Maj [vérifié].

**Vue à la première personne** : mod tiers **LMD-Camera 1.1.0** (flo-st, MIT, `docs/distribution.md`), livré tel quel. Comparé (2026-10-25) : DevdudeX/LMD-Alternative-Camera-Mod et son fork faehrmaniac (photo, 3e personne, manette) sans licence (= tous droits réservés, redistribution interdite), et cassé sur MelonLoader ≥ 0.6.2 selon son auteur ; LMD-Camera (MIT, 1re/3e personne, clavier seulement) marche tel quel avec MelonLoader 0.7.3. Il désactive `PlayCamera` et place la caméra sur l'os `head_tip_BindJNT` du vélo local (bras et roue avant visibles, tête coupée par le plan proche) ; `FP_HideBike` cache tout `mergedMesh` (les marionnettes, copies du vélo local, le réactivent avec les cheveux). Ovomium ajoute une bascule (`View/CameraToggle`) : `CameraKey` (V) ou R3 (libre en piste dans les cartes manette du jeu), 1re personne ⇄ vue précédente (normale ou 3e personne), refusée pendant une chute comme les touches du mod, mémorisée par LMD-Camera (`LastViewMode` de `UserData/LMD-Camera.cfg`). Le pont appelle des membres privés de LMD-Camera par réflexion : une autre version peut le désactiver (journalisé), ses touches 1/2/3 restant utilisables. Vérifié sur écran virtuel : pseudos bien placés en vue 1re personne (bot en orbite, écho), course avec un bot (compte à rebours, chrono, panneau), restart, chute (la caméra reste sur le vélo, le ragdoll est vu de là).

**Réalité virtuelle : compatibilité avec LMD-VR** (visual-gh, MIT, 1.0.0 du 2026-10-11 ; installé à part, pas livré). Étudié le 2026-10-25 : OpenXR à la main (`UserLibs/openxr_loader.dll`, D3D11, Windows seulement), aucun patch Harmony, caméras « œil » clonées de `PlayCamera` ; ses touches sont codées en dur (F5, F6 aperçu, F7 capture, F9 VR, F10 recentrer, F11, PageUp/PageDown, =/-, pavé 2/4/5/6/8 ; manette LB+RB+croix, R3, L3+R3, stick droit). Ovomium (`View/LmdVrBridge`) :
- Nos touches par défaut (F1-F4, V) ne croisent pas les siennes, ni nos combinaisons manette (Select + A/B). LMD-VR installé (melon « LMD VR ») : une touche réglée à la main sur l'une des siennes est signalée par un avis au menu (après l'écran titre, 12 s ; liste dans `Core/Keys/KeyChoice`, testée).
- VR démarrée (réflexion : `Mod._vr` → `VrManager._session` → `VrSession.State` hors `Off`/`Failed` ; illisible → considérée démarrée dès que le mod est là) : bascule V / R3 coupée, avec un avis au premier appui.
- Limites connues : notre interface est en IMGUI, donc invisible dans le casque hors caméra « Screen » de LMD-VR ; LMD-Camera n'est pas suspendu par LMD-VR (sa 1re personne passerait en stéréo en caméra « Diorama »). Avec `AutoStart` (défaut), LMD-VR lance OpenXR à chaque démarrage [vérifié : « starting OpenXR » dans le journal].
- Vérifié sur écran virtuel (sans `openxr_loader.dll`, la VR ne démarre pas) : détection de LMD-VR, avis de conflit au menu. **Non vérifié** : la lecture de `State` (session jamais créée ici) et la coupure de la bascule pendant une vraie session VR (à faire avec le casque d'Edia).

## Outils de test

- **Mode écho** (`EchoMode`, touche F4) : nos poses repartent vers `127.0.0.1` et reviennent comme celles d'un pair « (écho) », affiché avec `EchoDelaySeconds` (2 s) de retard. Toute la chaîne capture → encodage → socket → décodage → interpolation → marionnette est exercée sans second PC.
- **Enregistrement** (`RecordPoses`) : les poses émises vont dans `UserData/Ovomium/recordings/<piste>-<date>.lmdrec`, un fichier par passage sur une piste.
- **LmdBot** : `dotnet tools/LmdBot/bin/Release/net8.0/LmdBot.dll help`. Écoute sur 47710 (le jeu garde 47700) et s'annonce aussi sur `127.0.0.1:47700`.
  - Sans `--replay`, il tourne à 4 m autour du premier vrai joueur en copiant sa pose.
  - `--replay latest` rejoue le plus récent enregistrement utilisable (≥ 2 s) du jeu, sur sa piste, sans l'attente du départ (le rejeu commence 1 s avant que le vélo ne bouge).
  - `--count N` simule N joueurs dans un seul processus (ports 47710…, pseudos `Bot-1`…) : orbites de rayons croissants ou rejeux décalés de 1,5 s. Sert aux mesures de performance.
  - Il affiche ce qu'il reçoit (pseudo, piste, ping, poses, crash). Arrêt : Ctrl+C ou `pkill -f LmdBot` (SIGTERM), avec « Bye » dans les deux cas ; `kill -9` simule un jeu fermé brutalement (pas de « Bye »).
  - `--no-poses` : aucun fantôme chez les autres (course seule).
  - **Course** (même logique que le mod, `RaceHost` / `RaceGuest`) : `--race join` accepte les invitations, « charge » `--race-load` s (3), roule `--race-time` s (25, +4 s par bot) en franchissant 7 checkpoints, `--race-dnf` abandonne à mi-course, `--race-decline` refuse. `--race host --race-track ID` lance une course (une fois) 3 s après avoir vu un vrai joueur. `--loss 0.3` jette 30 % des paquets émis. Les bots n'entendent que le jeu (pas les autres bots) : pour une course à plusieurs bots, c'est le jeu qui doit être l'hôte.
- **Écran virtuel** (rendu complet sans toucher au bureau) : `tools/lmd-virtual.sh start|stop|shot` (`LMD_RES=1080` : écran 1920×1080, puis commande `res 1920 1080` de LmdExplorer pour agrandir la fenêtre du jeu, qui applique sinon ses propres réglages ; `stop` remet le registre du préfixe où Unity mémorise la taille), puis `tools/lmd-keys.sh enter` (lance le jeu si besoin et entre dans la piste sélectionnée au clavier), `drive NOM IPS Up:1.3 Up+Left:0.8 …` (roule en filmant, planche d'images dans `/tmp/NOM-montage.png`), `restart`, `menu`, `key`, `shot`. Voir l'en-tête du script pour les touches du jeu.
- **Session bureau** : `tools/desktop-session.sh` (déploie, lance, guide, nettoie ; journal dans `/tmp/lmd-session/`).
- **Manette virtuelle** : `tools/lmd-pad.py ÉTAPE…` (ex. `back:0.4 back+a:0.3 none:0.3`) ou `--serve FICHIER` (garde la manette et joue les étapes écrites dans le fichier). « Xbox 360 » par uinput, sans root (`/dev/uinput` accessible à l'utilisatrice) ; le jeu la voit comme « XInput Gamepad 1 ». ⚠ visible de tout le système (Steam l'ouvre aussi ; sans effet tant que Steam Input est désactivé pour les manettes Xbox, ce qui est le cas ici).
- **Vérification manette sur le bureau** : `tools/pad-check.sh` (mode test, bot qui accepte les courses, mode d'emploi, `i` = recevoir une invitation ; journal et résumé dans `/tmp/lmd-padcheck/`).

## Configuration

Fichier `<jeu>/UserData/Ovomium.cfg` (créé au premier lancement, commenté) : `Port`, `ManualPeers`, `Nickname` (vide = pseudo Steam), touches `RaceKey` F1 (appui et maintien ; `RaceQuitKey` n'existe plus) / `StyleKey` F2 / `PanelKey` F3 / `EchoKey` F4, `CameraKey` V (vue 1re personne) (F1-F4 depuis 0.4.0, choisies libres chez le jeu, LMD-Camera et LMD-VR ; F6-F9 avant), `SkipIntro` (démarrage direct au menu, vrai), `KeyboardLayout` (`auto`, `azerty`, `qwertz`, `qwerty`), `ShowPanel`, `EchoMode`, `EchoDelaySeconds`, `SendRateHz` (25), `InterpolationDelayMs` (100), `GhostStyle`, `RecordPoses`, `DebugLog`, `TestMode` (tests seulement : le jeu n'enregistre aucun résultat, ni classements en ligne ou locaux, ni succès, stats, progression ; voir `game-api.md` § 5 « Arrivées de test » ; **obligatoire pour toute manche scriptée ou téléportée**).

## Interface

- **Couleur par joueur** : attribuée dans l'ordre d'arrivée (teintes espacées du nombre d'or), ambre pour l'écho. Le fantôme, son pseudo et sa pastille dans le panneau ont la même couleur. Locale à chaque PC.
- **Aspect des fantômes** (`GhostStyle`, F2 pour essayer) : `None` (défaut, « normal ») = comme le vélo local ; `Translucent` = semi-transparent, non éclairé, sans ombre, nuancé de la couleur du joueur (shader `Legacy Shaders/Particles/Alpha Blended` × couleurs de sommets du modèle) ; `Tint` = opaque, couleurs multipliées (effet discret). Une config existante garde sa valeur.
- **Course** : panneau en haut à gauche (sous les compteurs du jeu en piste) : piste, état ou chrono, coureurs (pastille = couleur du fantôme, moi en blanc et en gras), aide des touches. Compte à rebours, « GO ! » et « Arrivée ! temps · place » en grand au centre ; invitation en bandeau central ; résultats au milieu de l'écran (le haut est pris par l'écran de résultats du jeu).
- **Pseudos** : au-dessus de la tête ; au-delà de 30 m, ils pâlissent (jusqu'à 55 % à 150 m) sans disparaître et affichent la distance au joueur. Masqués quand le menu pause du jeu est ouvert.
- **Panneau** (F3) : en piste, les joueurs de la piste (8 au plus, puis « + N autres ») et un résumé « N ailleurs » ; au menu, tout le monde avec sa piste. Sous la barre d'onglets du jeu dans les menus, sous le temps total du jeu (coin haut droit) en piste.
- **Tailles** : textes proportionnels à la hauteur d'écran (`GuiKit.Scale` = hauteur / 900, soit 20 px pour un texte courant en 1080p), plancher pour les petites fenêtres (12-13 px en 640×480). Encadrés centraux (invitation, résultats, jauge) presque opaques ; notifications sur fond ajusté au texte, sous le bandeau d'invitation s'il est affiché. Vérifié sur écran virtuel en 640×480 et 1920×1080 (captures de tout le déroulé d'une course, 2026-10-25).

## Performances (mesurées le 2026-10-25)

Écran virtuel 640×480, RTX 2050, `DebugLog` (le mod mesure son propre temps par image), joueur à l'arrêt au départ de Sierra Rivera piste 2, bots en orbite :

| Fantômes | Images/s | Temps du mod par image |
|---|---|---|
| 0 | 130-140 | 0,09-0,13 ms |
| 1 (écho) | 129-151 | 0,16-0,21 ms |
| 2 | 127-145 | 0,25 ms |
| 9 | 125-137 | 0,75 ms |
| 16 | 111-137 | 0,9-1,0 ms |

Images/s très bruitées d'une mesure à l'autre (les bots tournent sur le même PC). Coût du mod : environ 0,06 ms par fantôme visible (les fantômes hors champ ne recopient que leur racine), soit 6 % d'une image à 60 images/s avec 15 joueurs. Aucune pose « figée » (paquet en retard) en local.

## Limites connues

- Les fantômes ont l'apparence (vélo, tenue) du joueur local : l'apparence de chacun n'est pas encore transmise (voir `game-api.md` § 3). Le style translucide le masque en partie.
- Translucide : sans écriture de profondeur, on voit un peu l'intérieur du modèle (bras derrière le corps…). Acceptable pour un fantôme.
- Windows : l'installeur crée la règle de pare-feu (UAC) ; sans elle, au premier lancement, le pare-feu demande d'autoriser LMD sur le réseau (à accepter, au moins pour les réseaux privés), question souvent cachée sous le jeu plein écran (`docs/distribution.md`).
- Non testé : vrai LAN à deux PC, Windows, Wi-Fi ; affichage au-delà de 1080p.
- Course : un seul hôte coordonne ; s'il quitte avant le départ, la course est annulée ; après, chacun la finit seul avec un classement figé (voir « Départs et coupures »).
- Manette : boutons lus par leurs identifiants Xbox (XInput, ce que Proton présente pour la plupart des manettes) ; une manette reconnue autrement par Rewired (ex. DualShock en natif) pourrait avoir d'autres numéros. Manettes vues journalisées au démarrage (« manettes : … »). Vérifiée avec une manette virtuelle seulement (`tools/lmd-pad.py`) ; vérification avec une vraie manette : `tools/pad-check.sh`.
