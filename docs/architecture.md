# Architecture de LmdMulti (v0 : fantômes réseau)

Mod MelonLoader pour Lonely Mountains: Downhill 1.6.0. Chaque joueur simule son propre vélo ; les autres apparaissent comme des marionnettes sans collision, avec leur pseudo au-dessus. Pas encore de course (prévue en v1).

## Dossiers

| Dossier | Rôle |
|---|---|
| `src/LmdMulti.Core/` | Cœur sans Unity, partagé par le mod, le bot et les tests : protocole, réseau, interpolation, enregistrements |
| `src/LmdMulti.Core/Protocol/` | Format binaire des messages (`Messages`, `PacketIO`, `QuatCodec`, `ProtocolInfo` = version, squelette transmis) |
| `src/LmdMulti.Core/Net/` | `UdpNode` (socket UDP + fil réseau), `PeerTable` (pairs connus), `Endpoints` (broadcast, IP manuelles) |
| `src/LmdMulti.Core/Poses/` | `PoseFrame` (une pose), `SnapshotBuffer` (interpolation retardée), `PoseRecorder` (fichiers .lmdrec) |
| `src/LmdMulti/` | Le mod : `LmdMultiMod` (point d'entrée, relie tout), `Config` (MelonPreferences), `Log` |
| `src/LmdMulti/Game/` | Lecture de l'état du jeu : piste en cours, nom de piste, pseudo Steam |
| `src/LmdMulti/Ghosts/` | Capture de la pose locale (`PoseCapture`, `LocalSender`), marionnettes (`Puppet`, `GhostManager`), squelette (`BoneRig`) |
| `src/LmdMulti/UI/` | OnGUI : panneau des joueurs, pseudos au-dessus des fantômes, notifications |
| `src/LmdMulti/Diagnostics/` | Capture d'écran (F10), statistiques et relevé des os en mode `DebugLog` |
| `tools/LmdBot/` | Faux joueur en console (Linux) : orbite autour du joueur ou rejeu d'un .lmdrec |
| `tests/LmdMulti.Tests/` | Tests xUnit du cœur (`dotnet test tests/LmdMulti.Tests`) |
| `src/LmdExplorer/` | Outil de dev séparé (exploration de l'API du jeu), pas livré aux joueurs |

Le mod compile les sources du cœur directement (fichiers liés dans `LmdMulti.csproj`) : **une seule DLL**, `LmdMulti.dll`, à copier dans `<jeu>/Mods/`. Aucune dépendance externe (UDP maison : LiteNetLib n'apporterait que la fiabilité, inutile pour des poses envoyées en continu).

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

## Protocole (version 1)

Petit-boutiste. En-tête de 12 octets **figé pour toutes les versions** : `'L' 'M'`, version (u8), type (u8), id d'instance de l'émetteur (u64, aléatoire à chaque lancement). Un paquet d'une autre version n'est jamais décodé : le pair est listé « version incompatible » et une notification s'affiche.

| Type | Contenu après l'en-tête |
|---|---|
| 1 Announce | SteamId u64, pseudo (u8 longueur + UTF-8, ≤ 48 o), piste (≤ 32 o, vide au menu), en piste u8, version du mod (≤ 16 o) |
| 2 Pose | temps émetteur ms u32, drapeaux u8 (1 = ragdoll, 2 = os présents), racine : position 3×f32 + rotation u32 ; si os : 31 rotations u32, masque de positions u32, puis 3×half par os du masque |
| 3 Ping / 4 Pong | temps de l'émetteur du ping u32 (renvoyé tel quel) |
| 5 Bye | rien : départ propre, le fantôme disparaît tout de suite |

- **Racine** = monde de `bike/Bind_JNT` (vélo normal) ou de `RagDoll(Clone)/bike/Bind_JNT` (pendant un crash) : même arbre, mêmes noms d'os.
- **Rotations** : « smallest three » sur 32 bits (erreur < 0,25°).
- **Positions locales** : seulement pour les os qui translatent (`Skeleton.DefaultPositionMask` : cog, spine_1, mains, suspensions…). En `DebugLog`, toutes, pour relever lesquels bougent vraiment.
- Taille : environ 230 octets par pose (≈ 350 en DebugLog). À 25 Hz et 15 joueurs : ≈ 80 Ko/s reçus par joueur.

## Interpolation (`SnapshotBuffer`)

Style Gaffer On Games : on affiche chaque pair tel qu'il était il y a `InterpolationDelayMs` (100 ms). L'horloge de l'émetteur est recalée sur la nôtre par le décalage minimal (réception − émission) vu sur la fenêtre gardée. On interpole entre les deux poses qui encadrent l'instant visé (positions linéaires, rotations nlerp). Sans pose plus récente, on fige la dernière. Un saut de plus de 15 m (respawn) n'est pas interpolé. Si l'horloge de l'émetteur repart en arrière (redémarrage), le tampon est vidé.

## Outils de test

- **Mode écho** (`EchoMode`, touche F9) : nos poses repartent vers `127.0.0.1` et reviennent comme celles d'un pair « (écho) », affiché avec `EchoDelaySeconds` (2 s) de retard. Toute la chaîne capture → encodage → socket → décodage → interpolation → marionnette est exercée sans second PC.
- **Enregistrement** (`RecordPoses`) : les poses émises vont dans `UserData/LmdMulti/recordings/<piste>-<date>.lmdrec`, un fichier par passage sur une piste.
- **LmdBot** : `dotnet tools/LmdBot/bin/Debug/net8.0/LmdBot.dll help`. Écoute sur 47710 (le jeu garde 47700) et s'annonce aussi sur `127.0.0.1:47700`. Sans `--replay`, il tourne à 4 m autour du premier vrai joueur en copiant sa pose ; avec `--replay latest`, il rejoue le dernier enregistrement du jeu, sur sa piste. Il affiche ce qu'il reçoit (pseudo, piste, ping, poses, crash).
- **Session bureau** : `tools/desktop-session.sh` (déploie, lance, guide, nettoie ; journal dans `/tmp/lmd-session/`).

## Configuration

Fichier `<jeu>/UserData/LmdMulti.cfg` (créé au premier lancement, commenté) : `Port`, `ManualPeers`, `Nickname` (vide = pseudo Steam), touches `PanelKey` F8 / `EchoKey` F9 / `CaptureKey` F10, `ShowPanel`, `EchoMode`, `EchoDelaySeconds`, `SendRateHz` (25), `InterpolationDelayMs` (100), `GhostTint`, `RecordPoses`, `DebugLog`.

## Limites connues (v0)

- Les fantômes ont l'apparence (vélo, tenue) du joueur local : l'apparence de chacun n'est pas encore transmise (voir `game-api.md` § 3).
- Pas de transparence : un shader transparent demanderait de remplacer les matériaux. La teinte bleutée multiplie `_Color` par un `MaterialPropertyBlock`, sans toucher aux matériaux partagés.
- Windows : au premier lancement, le pare-feu demande d'autoriser LMD sur le réseau (à accepter, au moins pour les réseaux privés).
