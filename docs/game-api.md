# Carte de l'API du jeu (LMD 1.6.0, MelonLoader 0.7.3, interop Il2Cpp)

Points d'accès au jeu utiles au mod multijoueur. Chemins C# valables avec les assemblies interop générées par MelonLoader 0.7.3 pour **cette** build : les noms obfusqués (`Method_*`, `field_Private_*`, `prop_*`, `ObjectPublic…Unique`) changent si le jeu est mis à jour.

Certitude :
- **[vérifié]** : lu ou appelé en jeu par le mod explorateur (`src/LmdExplorer`), le 2026-10-25, en `-batchmode` (voir « Limites des tests »).
- **[signatures]** : lu dans les signatures interop seulement.
- **[déduit]** : rôle déduit (nom de lambda, signature, mod tiers), pas encore observé.

Namespaces : `Il2Cpp` (types obfusqués), `Il2CppMegagon.Downhill.*`, `Il2CppSteamworks` (Facepunch).

## 1. Vélo du joueur local

| Quoi | Accès | Certitude |
|---|---|---|
| Composant racine | `Object.FindObjectsOfType<BikeLocomotion>()[0]` (`Vehicle.Controller`) ; une seule instance, même objet que `GameObject.Find("Bike(Clone)")` | [vérifié] |
| GameObject | `Bike(Clone)`, racine de la scène `gameplay` ; existe déjà au menu (vélo du décor) et persiste en piste | [vérifié] |
| Composants de `Bike(Clone)` | `BikeLocomotion`, `BikeAnimation`, `Players.Stamina`, `VehicleLights`, `Rigidbody`, `Players.PlayerCameraTarget` | [vérifié] |
| Corps physique | racine séparée `Collision_RigidBody` (`Rigidbody` + `VehicleRigidbody`) = `bike.m_collisionRigidbodyTransform` | [vérifié] (que ce soit le corps simulé : [déduit]) |
| Position/rotation à envoyer | `bike.transform.position/rotation` | [vérifié] |
| Vitesse | `bike.GetComponent<Rigidbody>().velocity` (mod LMD-VR) | [vérifié] lecture ; valeur en mouvement non testée |
| Repères | `m_wheelCenterTransformFront/Rear`, `m_headJoint` (= `head_tip_BindJNT`) | [vérifié] |

## 2. Rider : pose et crash

Hiérarchie vérifiée sous `Bike(Clone)` :

- `bike` : `Animator` (avatar `bikeAvatar`, non humanoïde), `MeshMerger`, `IKEventHandler`.
  - `Bind_JNT/root_BindJNT/cog_BindJNT/…` : squelette commun vélo + rider.
  - `mergedMesh` : `SkinnedMeshRenderer` unique (vélo + corps fusionnés), `rootBone = root_BindJNT`, 31 os.
- Os du squelette :
  - vélo : `root, cog, back_suspension, wheel_back_axis, wheel_back_spin, handlebar, wheel_front_axis, wheel_front_spin, pedal`, suffixe `_BindJNT` ;
  - rider : `spine_1/2/3, neck, head, head_tip, hip/knee/ankle/toe _L/_R, shoulder/elbow/hand/finger _L/_R`, suffixe `_BindJNT`.
  - cibles IK : `hand_IK_Target_*`, `ElbowIK_Effektor_*`.
- `BikeAnimation` : `m_bikeRootTransform = cog_BindJNT`, `m_riderRootTransform = spine_1_BindJNT` [vérifié].
- Cheveux et casque : `rider_hair_…(Clone)` (`RiderAttachment`), contraints par `ParentConstraint` sur les os ; mèches animées par `SoxAtkJiggleBone`.
- Paramètres de l'Animator : illisibles via l'interop (`Animator.parameters` et `GetParameter` donnent « Method unstripping failed »). Ce n'est pas gênant : on copie les os, pas l'Animator.

**Pose à transmettre** : `localRotation` des ~30 `*_BindJNT` sous `bike/Bind_JNT`, et `localPosition` des quelques os qui translatent (suspension, `cog`), plus la racine en monde. Pour les cheveux, laisser les contraintes de la marionnette suivre ses os.

**Crash**
- `bike.prop_Boolean_2` : vrai pendant le ragdoll [vérifié].
- `bike.prop_CrashReason_0` : raison du crash, ex. `FallTime` [vérifié].
- `RagDoll(Clone)` (racine de scène) devient actif pendant le crash [vérifié].
- Ce ragdoll contient :
  - `bike_ragDoll_guy` : corps physiques `Rigidbody`/`Joint` sur `spine_1`, `hip`, `knee`, `neck`, `shoulder`, `elbow`, `hand` ;
  - `bike_ragDoll_bike` : `bike_front` et `bike_rear` ;
  - `bike/Bind_JNT/…` : même arborescence et mêmes noms d'os que le vélo, pilotée par des `ParentConstraint` (Megagon.Framework).
- Pendant un crash, envoyer donc les os de `RagDoll(Clone)/bike/Bind_JNT` (même format) : fait par LmdMulti, les 31 os sont trouvés dans les deux arbres et `RagDoll(Clone).activeInHierarchy` bascule bien au crash [vérifié, batchmode].
- Os qui translatent hors crash (amplitude des positions locales, relevé LmdMulti `DebugLog` en conduite réelle sur 1600 poses) : `cog` 35 cm, `spine_1` 84 cm, `hand_L` 20 cm, `shoulder_L` 7 cm, `spine_3` 6 cm, `spine_2` 5 cm, `wheel_front_axis` 3 cm, `hand_R` 3 cm, `shoulder_R` 1 cm [vérifié]. Suspension arrière et pédalier : aucun mouvement relevé.
- Enum `CrashReason` : NoCrash=0, LostControl=1, OutOfBounds=10, ObstacleHit=11, ExternalTrigger=12, Water=13, ObstacleRock=14, ObstacleTree=15, ObstacleCactus=16, Lava=19, FallHeight=20, FallTime=21, ExtremSlope=22, BottomOut=23, BackwardMovement=24, FallImpact=25, HardSlide=26, Unknown=99 [signatures].

## 3. Marionnette (fantôme visuel)

Méthode vérifiée, rendu compris (écran virtuel, 2026-10-25 : marionnettes visibles, pose conforme en roulant et pendant un crash) :
1. Créer un parent inactif, y faire `Object.Instantiate(bikeGO, parentInactif)`. Ainsi, aucun `Awake`/`OnEnable` des scripts copiés ne s'exécute.
2. `DestroyImmediate`, dans cet ordre : `Joint`, `MonoBehaviour`, `Collider`, `Rigidbody`.
3. Désactiver les `Animator`, détacher la copie du parent, puis l'activer.

- Ce qui part : `BikeLocomotion`, `BikeAnimation`, `Stamina`, `VehicleLights`, `PlayerCameraTarget`, `TrackRegionChecker`, `BikeSound`, `MeshMerger`, `IKSolver`×2, `RiderAttachment`, `SoxAtk*`, 3 `SphereCollider`, 2 `Rigidbody`, etc.
- Ce qui reste : `mergedMesh` (`SkinnedMeshRenderer`), `ParentConstraint`, `MeshRenderer`, `Light`.
- 121 transforms appariés par position dans l'arbre, ce qui permet de recopier la pose.
- Code : `src/LmdExplorer/PuppetTest.cs`.
- Limite : la copie a l'apparence (skin) du joueur local. Pour l'apparence de chaque joueur, transmettre son profil (voir § 6 `GameProperties.ActiveRider*`) et le reconstruire. La fabrique existe (`ObjectPublicLi1CoHaFaHeBaSiReSiUnique`, méthodes `GameObject …(VehicleConfiguration, List<ColorizedMesh>…, HairType, …)`) [signatures], mais elle n'a pas été testée.
- Lumières : la copie garde les `Light` des phares (groupe `lights`, inactif de jour). LmdMulti désactive ce groupe sur ses marionnettes.
- Matériaux : vélo + rider = un seul `SkinnedMeshRenderer` `mergedMesh`, matériau `bike (Instance)`, shader `Megagon/Standard VertexColor Simple` (propriétés `_Color`, `_MainTex`, `_Glossiness`, `_Metallic`, `_UV_GLOSSY`, `_EMISSIVE`, `_EmissiveColor`, `_EmissiveTex`) ; cheveux idem. En batchmode, `Material.HasProperty("_Color")` répond faux alors que `shader.FindPropertyIndex("_Color")` le trouve [vérifié].
- Les couleurs du modèle (tenue, vélo personnalisés) sont dans les **couleurs de sommets** du mesh ; `_Color` ne fait que les moduler, d'où une teinte peu visible. `_EMISSIVE`/`_EmissiveColor` posés par `MaterialPropertyBlock` : sans effet visible [vérifié].
- Transparence : `Legacy Shaders/Particles/Alpha Blended` (présent dans la build, `Shader.Find` le trouve) rend le modèle avec ses couleurs de sommets × `_TintColor`, translucide et non éclairé [vérifié]. Autres shaders chargés utilisables : `Mobile/Particles/Alpha Blended`, `Sprites/Default`, `Unlit/Color`, `Standard` (variante Fade non vérifiée).
- LmdMulti pilote ses marionnettes ainsi (racine + `Bind_JNT` en monde, os en local) ; la position relue dans Unity correspond à la pose visée, crash compris [vérifié] ; rendu vu sur écran virtuel [vérifié].

## 4. Pistes : identification et chargement automatique

| Quoi | Accès | Certitude |
|---|---|---|
| ID complet de piste | `"AL_01_A1"` = montagne `AL` + sous-montagne `01` + route `A1` | [vérifié] |
| Piste en cours | `PlayManager.prop_TrackRoute_0` (`TrackRoute`, `TrackElements`) → `.m_trackRouteID` (`A2`) et `.m_trackRouteInfo.m_fullTrackRouteID` (`CA_01_A2`) ; `null` au menu | [vérifié] |
| Sélection courante (celle que « jouer » lance) | `MountainManager.Method_Public_Static_ObjectPublicInm_StObm_InStUnique_0().m_fullTrackRouteID` | [vérifié] |
| Sous-montagne / scène | `MountainManager.Method_Public_Static_SubMountain_0()` → `.m_SubID`, `.m_sceneName` (`Canyon_01`) | [vérifié] |
| Nom affiché | `MountainManager.Method_Public_Static_String_String_0(id)` → « Sierra Rivera \| Piste 2 » (localisé) | [vérifié] |
| Catalogue | `Manager<MountainManager>.prop_T_0.field_Private_List_1_Mountain_0[m]` → `.m_mountainID`, `.m_subMountains[].m_trackRouteInfos[].m_fullTrackRouteID`, `.m_checkpointCount`, `IsPlayable`, `IsOwned` | [vérifié] |
| Index d'une piste dans sa montagne | `mountain.Method_Public_Virtual_Final_New_Int32_String_Boolean_0(id, false)` (≠ ordre des ID : `AL_01_A3` = 1, `AL_01_A2` = 2) | [vérifié] |
| État global | `GameStateManager.prop_EnumNPublicSealedva5vUnique_0` : 0 démarrage, 1 profil, 10 menus, 100 en piste | [vérifié] |

**Chargement auto : faisable, vérifié** (Alps, Autumn, Canyon) :
1. Si on est en piste : `GameStateManager.Method_Public_Static_Void_1()`, retour au menu de jeu (`GameMenu`) en 1 à 2 s.
2. `var mm = Manager<MountainManager>.prop_T_0;` puis :
   - `mm.Method_Private_Void_Int32_0(indexMontagne)` (index dans `field_Private_List_1_Mountain_0`) ;
   - `mm.Method_Private_Void_Int32_1(indexPiste)` (index ci-dessus).

   Le jeu met aussi à jour `profil.GameProperties.ActiveFullTrackRouteID`. Implémenté dans `TrackProbe.Select(id)`.
3. `Object.FindObjectOfType<GameMenu>().Method_Public_Void_1()` (`UI.Screens`) charge la piste.
   - Depuis l'écran titre : d'abord `FindObjectOfType<MainMenuScreen>().OnStartButton()`.
   - La sélection de profil a été passée automatiquement lors des essais.

Pièges :
- Écrire seulement `ActiveFullTrackRouteID` dans le profil ne change pas la sélection en cours de session. Le profil n'est relu qu'au lancement du jeu.
- Le temps de chargement varie : un essai sur `RO_01_A3` n'avait pas fini de charger au bout de 13 s ; en course, 3 s depuis le menu ou l'écran titre (`CA_01_A2`, `RO_01_A4`, `CA_01_A3`). **L'état « en piste » (100, route posée) arrive avant la fin de l'écran de chargement** : attendre que `LoadingScreenOverlay` (UI.Screens, `field_Private_CanvasGroup_0.alpha`) ait disparu [vérifié].
- Pendant un chargement, `PlayManager.prop_TrackRoute_0` peut encore donner l'ancienne piste un instant [vérifié].

**Verrous** (trouvés par désassemblage de `GameAssembly.dll`, contournement vérifié en jeu le 2026-10-25 sur `RO_01_A4`, verrouillée dans la sauvegarde d'Edia) :
- **Par piste** : masque d'heures du jour débloquées dans l'info de piste (`ObjectPublicInm_StObm_InStUnique`) : `info.field_Private_ValueTypePublicSealedObUnique_0.field_Public_EnumPublicSealedva5v2_0` (flags : 1 jour, 2 nuit [déduit]). `info.Method_Public_Boolean_EnumPublicSealedva5v2_0(tod)` = débloquée pour cette heure (et sous-montagne jouable) ; `Method_Public_Void_Boolean_0(b)` = masque à 3 ou 0 ; `Method_Public_Void_Boolean_EnumPublicSealedva5v2_0(b, tod)` = ajoute/retire des bits. Heure courante : `MountainManager.prop_EnumPublicSealedva5v2_0`.
- Ce masque n'est **pas sauvegardé** : `ProgressionDB.Method_Public_Void_0()` le recalcule au chargement du profil depuis la progression (`PlayerProfileParameter.ProgressionSettings`). Le forcer en mémoire est donc temporaire ; LmdMulti le remet comme avant dès qu'on a quitté la piste [vérifié : « verrou remis »].
- `PlayManager.Method_Private_Void_PDM_3` (lancement de manche) **refuse** une piste verrouillée (exception) : contourner seulement les menus ne suffit pas [asm].
- **Par sous-montagne** : `SubMountain.IsPlayable` (faux tant que le tutoriel n'est pas fini, setter disponible), `IsOwned` (DLC). Les « Piste 5 » (`*_02_A1`, sous-montagne `02`) ne sont pas possédées ici : `RO_02_A1` refusée (« montagne non possédée ») [vérifié] ; ne pas forcer (contenu sans doute absent).
- **Progression** : à l'arrivée, le tracker appelle `ProgressionDB.Method_Public_Void_String_GameMode_Int32_EnumPublicSealedva5v2_Single_Int32_PDM_0(id, mode, véhicule, tod, temps, chutes)`, qui valide les objectifs (et peut débloquer d'autres pistes pour de bon). LmdMulti l'annule par un prefix sur une piste forcée [vérifié : appel intercepté]. L'écran de résultats du jeu affiche quand même les défis de la manche.
- `GameConfigSettings.unlockEverything` existe mais n'est lu nulle part [asm] : inutilisable.
- Non utilisés par le jeu lors des essais : `PlaySceneManager.Method_Public_Void_String_String_…_PDM_0` et `PlayManager.Method_Public_Static_Void_Int32_0`. Ne pas s'en servir.

## 5. Course : départ, checkpoints, arrivée, chrono, restart

| Quoi | Accès | Certitude |
|---|---|---|
| Checkpoints | `route.m_checkpoints[]` (`TrackCheckpoint`, `Tracks`) : `[0]` = départ (`GameObject_Start`/`GameObject_Checkpoint (4)`…), dernier = `GameObject_FinishLine` ; `.m_hasSpawnPoint`, `.m_spawnTransform` | [vérifié] |
| Mise en place d'une manche | séquence observée au lancement et au restart : `PlayManager.Method_Private_Void_TrackRoute_PDM_0(route)`, puis `PlayerManager.Method_Public_Static_Void_Transform_0(spawn)` ×2, puis `PlayerManager.Method_Public_Static_Void_Boolean_0(true)` | [vérifié] |
| Téléporter au départ | `PlayerManager.Method_Public_Static_Void_Transform_0(route.m_checkpoints[0].m_spawnTransform)` (« SetToTransform », d'après le nom de lambda) | [vérifié] (appel accepté, effet visuel non vu) |
| Bloquer / débloquer le vélo | `PlayerManager.Method_Public_Static_Void_Boolean_0(bool)` (« LockMovement ») : `true` à la mise en place ; `false` libère le vélo. **Le jeu n'appelle `false` qu'au premier appui du joueur** (via `PlayManager.Method_Private_Void_PDM_5`, qui met aussi `field_Private_Boolean_0`) : un prefix qui l'annule garde le vélo au départ [vérifié] | [vérifié] |
| Restart complet | `Resources.FindObjectsOfTypeAll<PauseScreen>()[0].OnRestartRun()` (re-spawn + lock) ; `OnRestartCheckpoint()` existe aussi | [vérifié] / [signatures] |
| Vélo contrôlable | `Manager<PlayManager>.prop_T_0.field_Private_Boolean_0` (vrai après l'intro, faux en crash ; d'après LMD-Custom-Rides) | [déduit] |
| Tracker de manche | `HighscoreManager.prop_ObjectPublicBoGaObInSiStObDi2LiUnique_0` (voir détail ci-dessous) | lecture [vérifié] |
| Section franchie | patch de `tracker.Method_Public_Void_Int32_Single_PDM_0(int section, float temps)` : section = 1, 2… (la ligne d'arrivée compte pour la dernière), temps de la section en s ; précédé de `TrackCheckpoint.Method_Public_Virtual_Void_Int32_Boolean_0` | [vérifié] |
| Arrivée | patch de `tracker.Method_Public_Void_PDM_0()`, appelé une fois au franchissement de la ligne (après la dernière section) | [vérifié] |
| Porte d'un checkpoint | `cp.transform` porte un `BoxCollider` trigger d'environ 9 × 7 × 11 m ; y téléporter le vélo (après le premier appui) déclenche section ou arrivée. Le `m_spawnTransform` est à côté de la porte : y téléporter ne déclenche rien et affiche « point de contrôle manqué » au suivant | [vérifié] |
| Chrono affiché | `FindObjectOfType<PlayScreen>().m_timePanelText.text` (« 0.00 ») | [vérifié] |

Détail du tracker de manche :
- Au départ : `field_Private_Int32_0` = sections passées, vaut −1 ; `field_Private_List_1_UInt32_0[0]` = temps total en ms, vaut 999998976 (non défini) ; `prop_Boolean_0` = fini.
- **Piège** : ces champs gardent les valeurs de la manche précédente (fini = vrai, sections = 8) jusqu'au premier appui du joueur sur la manche suivante, même après un changement de piste [vérifié : fausse arrivée à 2 s]. Utiliser les patchs section/arrivée, pas les champs.
- Le chrono du jeu (et sa manche) ne démarre qu'au premier appui du joueur.

Choix de LmdMulti : chrono et classement calculés par le mod (départ commun = fin de son compte à rebours) ; sections et arrivée par patchs du tracker.

## 6. Steam et profil

| Quoi | Accès | Certitude |
|---|---|---|
| Pseudo Steam | `Il2CppSteamworks.SteamClient.Name` (`'°v°'`), `SteamClient.SteamId.Value`, `SteamClient.IsValid` | [vérifié] |
| Profil (sauvegarde) | `ObjectPublicOb1SePl1BoInInSePlUnique.field_Private_Static_ObjectPublicOb1SePl1BoInInSePlUnique_0.field_Private_Object1PublicStObBo1UIObBoObBoObUnique_1_PlayerProfileParameter_0.field_Private_T_0` → `PlayerProfileParameter` | [vérifié] |
| Préférences lisibles | `.GameProperties` : `ActiveFullTrackRouteID`, `ActiveVehicle` (4000…), `ActiveGameModeID`, `ActiveTimeOfDay`, `ActiveBodyType`, `ActiveRiderSkinID/Color`, `ActiveRiderHelmet`, `ActiveRiderHair`… | [vérifié] |

⚠ Écrire dans le profil modifie la sauvegarde d'Edia. Les essais l'ont remise sur `CA_01_A2`, sa valeur d'origine.

## 7. Second vélo piloté par le jeu ?

`PlayerManager` gère une liste de joueurs :
- `field_Private_List_1_InterfacePublicAbstractInBoObInVoBoObVoInBoUnique_0` ;
- joueur par index : `Method_Public_Static_…_Int32_0(int)`.

La fabrique `ObjectPublicLi1CoHaFaHeBaSiReSiUnique.Method_Public_InterfacePublicAbstractObTrObVoTrBoPlObTrVeUnique_Int32_0(int index)` crée un vélo par index de joueur [signatures]. Elle subsiste sans doute d'un mode multi-joueurs prévu ou console. Elle n'est pas testée, et un second vélo serait physique, avec sa caméra et ses entrées.

Pour la v1 : marionnettes sans physique (§ 3), ce qui est plus simple et plus sûr.

## 8. Pièges de l'interop (cette build) et interface du jeu

- **`Camera.allCameras` : plantage natif immédiat du jeu** (aucune exception, le processus meurt) [vérifié]. Utiliser `Camera.main`, sinon `Object.FindObjectsOfType<Camera>()` (avec parcimonie).
- `ColorUtility.ToHtmlStringRGB` : `NotSupportedException: Method unstripping failed` [vérifié]. Écrire l'hexadécimal soi-même.
- Pause : `Time.timeScale` **ne passe pas** à 0 dans le menu pause. Détection fiable : l'écran `PauseScreen` de la scène (pas le prefab : `gameObject.scene.IsValid()`) a `isActiveAndEnabled` vrai [vérifié]. L'état `GameStateManager` reste à 100.
- OnGUI est appelé plusieurs fois par image (Layout, Repaint…) : ne dessiner qu'à `EventType.Repaint`.
- Touches clavier du jeu (écran Contrôles) : Haut accélérer, Bas freiner, Gauche/Droite diriger, Maj sprinter, Retour arrière redémarrer au point de contrôle ; Entrée valide les menus et recommence après un crash ; Échap = pause (curseur sur « Continuer » à l'ouverture ; « Recommencer la tentative » = 3e entrée, « Quitter vers le menu » = 6e). Échap pendant un crash n'ouvre pas la pause.
- Menus depuis l'écran titre : six fois Entrée (titre, Commencer la partie, montagne, piste, défi, vélo) lancent la piste sélectionnée. La caméra et la direction du clavier suivent le vélo.
- Recette de conduite sur Sierra Rivera piste 2 (`CA_01_A2`), départ : Haut 1,3 s, Haut+Gauche 0,8 s, Haut 1,5 s tient la piste environ 5 s avant une chute (`tools/lmd-keys.sh drive …`).

## Limites des tests et besoins

- **Solution retenue (2026-10-25) : rendu complet sur écran virtuel Xorg « dummy »**. Commandes : `tools/lmd-virtual.sh start|stop|shot` (`:98`, DXVK sur la NVIDIA, config `tools/xdummy.conf`, paquet `xserver-xorg-video-dummy`). Le dummy annonce 60 Hz, ce qui évite le plantage de DXVK. Rendu, menus et conduite au clavier vérifiés (`tools/lmd-keys.sh`), 110-150 images/s en 640×480 (pas de synchro verticale). Pour voir en direct : `tools/watch-virtual.sh`. Ce qui suit est l'historique avant cette solution.
- **Rendu sous Xvfb impossible** : le jeu reste bloqué à la 1re frame (« Loading Addons... », écran noir).
  - Observé avec et sans MelonLoader, dans et hors du bac à sable, et avec `runInBackground` forcé.
  - Selon winedbg, le fil principal tourne en boucle dans la player loop.
- **Essai DXVK + bureau virtuel Wine** (2026-10-25, abandonné après 15 min) : `proton run explorer /desktop=LMD,1024x768 Z:\…\LMD_Win_x64.exe` sans `PROTON_USE_WINED3D`.
  - Passe l'étape qui plantait (division par zéro dans dxgi) : `Renderer: NVIDIA GeForce RTX 2050`, Enlighten initialisé, mods chargés.
  - Puis le jeu meurt sans message (Player.log coupé) : `WINEDEBUG=+seh` montre un `EXCEPTION_ACCESS_VIOLATION` (lecture en 0xFFFFFFFFFFFFFFFF) pendant le déroulement d'exceptions C++ ; cause non trouvée.
  - Xvfb annonce 0 Hz et refuse d'ajouter un mode (`xrandr --newmode` accepté, `--addmode` : « cannot find mode »).
  - Pièges : un `wineserver` resté d'un lancement précédent (Xvfb arrêté entre-temps) fait échouer le suivant (« no driver could be loaded ») : `WINEPREFIX=<pfx> "<Proton>/files/bin/wineserver" -k` avant de relancer.
- **Ancien contournement** : `LMD_SCREEN=xvfb LMD_ARGS="-batchmode -nographics" tools/lmd-virtual.sh start`.
  - Menus, profil, chargement de pistes, vélo et ragdoll fonctionnent : tout ce qui est marqué [vérifié] l'a été ainsi.
  - En revanche, le sol n'a pas de collision dans ce mode : au déblocage, le vélo tombe et crashe (`FallTime`).
- **Encore à vérifier en roulant** (désormais faisable sur l'écran virtuel, avec `tools/lmd-keys.sh`) ; le rendu des marionnettes l'a été, sections et arrivée aussi (par téléportation dans les portes, 2026-10-25) :
  - arrivée en roulant vraiment jusqu'au bout (une descente complète au clavier par script est irréaliste : chutes) ;
  - restart au checkpoint ;
  - effet visuel de `tostart`.
- **Pilote auto** : un patch Harmony sur `InputMethod_AdaptiveSwitch.Method_Public_Virtual_Void_byref_Single_byref_Single_0` (paramètres `out float`) lève des `NullReferenceException` dans le trampoline Il2CppInterop. Il a été retiré.

Mod explorateur : `src/LmdExplorer`.
- Commandes : une par ligne dans `UserData/LmdExplorer/cmd.txt` ou `/tmp/lmdexplorer-cmd.txt`.
- Liste : `dump`, `status`, `puppet`, `screens`, `mainstart`, `start`, `menu`, `catalogue`, `sel`, `select <ID>`, `restart`, `restartcp`, `lock`, `unlock`, `tostart`, `tocp [N|gN|f]` (respawn du checkpoint N, porte N, ligne d'arrivée), `timer`, `call <Type> <Méthode> [args]`.
- Touches : F11 dump, F12 marionnette de test (F5-F10 sont à LmdMulti).
- Fichiers drapeaux : `autostart` lance la piste sélectionnée depuis le menu ; `autodump` fait dump + marionnette de test à la première apparition du vélo.
- ⚠ Une arrivée obtenue par téléportation peut être enregistrée par le jeu (records, classements en ligne, succès) : voir § 5 « Arrivées de test » avant tout essai.
