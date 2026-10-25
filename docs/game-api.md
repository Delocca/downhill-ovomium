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
- Ce masque n'est **pas sauvegardé** : `ProgressionDB.Method_Public_Void_0()` le recalcule au chargement du profil depuis la progression (`PlayerProfileParameter.ProgressionSettings`). Le forcer en mémoire est donc temporaire ; LmdMulti le remet comme avant une fois revenu au menu (installé depuis 2 s) [vérifié : « verrou remis »].
- **Piège : retour au menu avec une montagne verrouillée sélectionnée** [vérifié 2026-10-25, reproduit 6 fois sur écran virtuel, avec ou sans LmdExplorer, téléportation ou fantômes] : course sur Mount Riley (`RO_01_A1`/`RO_01_A4`, montagne pas encore atteinte par Edia), retour au menu (pause « Quitter vers le menu », écran de résultats « Continuer vers le menu », ou `GameStateManager.Method_Public_Static_Void_1()`), puis chargement d'une autre piste (Sierra Rivera) : **écran de chargement figé**, `NullReferenceException` à chaque image dans `Transform.get_position/forward` ← `BikeLocomotion` ← `PlayerCameraTarget` ← `DynamicCamera` ← `PlayCamera` (mise en place asynchrone de `PlayManager` interrompue ; `Player.log` du préfixe). Le menu n'a pas rangé le vélo : au menu, `BikeLocomotion.field_Private_InputMethod_0` et `field_Private_Transform_1` (= `RagDoll(Clone)`) restent remplis au lieu de `null` (commande `refs` de LmdExplorer). Même course sur une piste verrouillée d'une montagne débloquée (`CA_01_A4`) : pas de problème. **Contournement de LmdMulti** : une fois la piste forcée chargée, la sélection du menu redevient celle d'avant la course (montagne débloquée) ; vérifié sur les trois chemins de retour, plus « recommencer la tentative » qui garde bien la piste en cours (la sélection ne sert qu'au lancement depuis le menu).
- `PlayManager.Method_Private_Void_PDM_3` (lancement de manche) **refuse** une piste verrouillée (exception) : contourner seulement les menus ne suffit pas [asm].
- **Par sous-montagne** : `SubMountain.IsPlayable` (faux tant que le tutoriel n'est pas fini, setter disponible), `IsOwned` (DLC). Les « Piste 5 » (`*_02_A1`, sous-montagne `02`) ne sont pas possédées ici : `RO_02_A1` refusée (« montagne non possédée ») [vérifié] ; ne pas forcer (contenu sans doute absent).
- **Progression** : à l'arrivée, le tracker appelle `ProgressionDB.Method_Public_Void_String_GameMode_Int32_EnumPublicSealedva5v2_Single_Int32_PDM_0(id, mode, véhicule, tod, temps, chutes)`, qui valide les objectifs (et peut débloquer d'autres pistes pour de bon). LmdMulti l'annule par un prefix sur une piste forcée [vérifié : appel intercepté]. L'écran de résultats du jeu affiche quand même les défis de la manche.
- **Temps sur une piste forcée** : non enregistrés non plus (ni en ligne ni record local), par les mêmes prefix que le mode test (§ 5, `TestMode.AllowResult`) ; succès et stats Steam restent comptés. Une piste reste « forcée » tant qu'on y est (course finie comprise) ; journal : « résultat non enregistré : piste forcée (…) », et rappel dans le panneau de course.
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
| Section franchie | patch de `tracker.Method_Public_Void_Int32_Single_PDM_0(int section, float temps)` : section = index du checkpoint − 1 (0 au premier après le départ, la ligne d'arrivée = la dernière), temps de la section en s ; c'est aussi « SubmitTimeForSection » qui **envoie le temps de section aux classements** ; précédé de `TrackCheckpoint.Method_Public_Virtual_Void_Int32_Boolean_0` | [vérifié] |
| Arrivée | patch de `tracker.Method_Public_Void_PDM_0()`, appelé une fois au franchissement de la ligne (après la dernière section) | [vérifié] |
| Porte d'un checkpoint | `cp.transform` porte un `BoxCollider` trigger d'environ 9 × 7 × 11 m ; y téléporter le vélo (après le premier appui) déclenche section ou arrivée. Le `m_spawnTransform` est à côté de la porte : y téléporter ne déclenche rien et affiche « point de contrôle manqué » au suivant | [vérifié] |
| Chrono affiché | `FindObjectOfType<PlayScreen>().m_timePanelText.text` (« 0.00 ») | [vérifié] |

Détail du tracker de manche :
- Au départ : `field_Private_Int32_0` = sections passées, vaut −1 ; `field_Private_List_1_UInt32_0[0]` = temps total en ms, vaut 999998976 (non défini) ; `prop_Boolean_0` = fini.
- **Piège** : ces champs gardent les valeurs de la manche précédente (fini = vrai, sections = 8) jusqu'au premier appui du joueur sur la manche suivante, même après un changement de piste [vérifié : fausse arrivée à 2 s]. Utiliser les patchs section/arrivée, pas les champs.
- Le chrono du jeu (et sa manche) ne démarre qu'au premier appui du joueur.

Choix de LmdMulti : chrono et classement calculés par le mod (départ commun = fin de son compte à rebours) ; sections et arrivée par patchs du tracker.

### Arrivées de test : ce que le jeu enregistre, et comment l'empêcher

Chaîne trouvée par désassemblage (2026-10-25 ; [asm] sauf mention) :
- **À chaque checkpoint** (pas seulement à l'arrivée), `PlayManager.Method_Private_Void_PDM_25` appelle `tracker.Method_Public_Void_Int32_Single_PDM_0` (« SubmitTimeForSection » : temps de section vers les classements), `StatisticManager.Method_Public_Static_Void_PDM_1` (stats) et `AnalyticsReporter.Method_Public_Static_Void_PDM_0` (GameAnalytics). À l'arrivée, en plus : `HighscoreManager.Method_Public_Static_Void_PDM_0` → tracker « Finish » (temps total vers les classements, `ProgressionDB` → objectifs), puis `AchievementManager.Method_Public_Static_Void_PDM_0` (succès Steam via `Il2CppSteamworks.Data.Achievement.Trigger`).
- **Classements** : pas ceux de Steam (absents de la build) mais le serveur HTTP de Megagon (`Il2CppMegagon.Platforms.LeaderboardRequests.ScoreRequests.SetScores`, réponse `newHighscore`), plus les records locaux du profil. Tous les temps (total et sections, en ligne et local) passent par **une seule méthode** : `Il2Cpp.ObjectPublicAbstractStInObBoObStInObInStUnique.Method_Public_Virtual_Final_New_Void_UInt32_Int32_Int32_Boolean_String_Action_1_ValueTypePublicSealedBoBoBoUnique_0(uint ms, …)` (Il2CppMegagon.Framework).
- Validations du jeu : total envoyé en ligne seulement s'il dépasse 20 s et qu'aucune section ne manque ; un total < 25,3 s ou une section < 1,58 s marque la manche « suspecte » (total remplacé par 10-90 ms !) ; section envoyée en ligne si > 1,8 s ; record local écrit seulement s'il bat l'ancien.
- Identifiant de classement : `HighscoreManager.Method_Public_Static_String_String_Int32_GameMode_EnumPublicSealedva5v2_0(piste, section, MountainManager.prop_GameMode_0, MountainManager.prop_EnumPublicSealedva5v2_0)` → `RO_01_A4_00_N_D` (section 00 = total, N = mode normal, D = jour) [vérifié]. Record local : `Manager<LeaderboardManager>.prop_T_0.Method_Private_UInt32_String_PDM_0(id)` en ms, 999998976 = aucun [vérifié : commande `record` de LmdExplorer].

**Serveur de classement** : `https://pc.leaderboard.lonelymountains.com`, codé en dur (`BASE_URL` de `Megagon.PlatformIntegration.Standalone.StandaloneLeaderboardConnection`, PlatformStandalone, créée sans condition par `StandalonePlatform.Initialize`) [asm ; chaîne vérifiée dans global-metadata]. `StreamingAssets/leaderboardServer.txt` (`http://192.168.1.18:3000`) **n'est pas lu** : `LoadServerURL()` est vide dans cette build et la chaîne « leaderboardServer » n'existe ni dans les métadonnées ni dans GameAssembly.dll [asm]. Méthode (Cpp2IL de MelonLoader : jeton interop → RVA → objdump) et scripts : perdus avec /tmp, à refaire au besoin.

**Lire les classements en ligne** (lecture seule ; commande `online <piste>` de LmdExplorer, `src/LmdExplorer/OnlineScores.cs`) [vérifié 2026-10-25 : `online AL_01_A1` rend rangs et temps, total 157 886 ms = record local] :
- Session : le jeu s'authentifie seul au démarrage ; `Manager<LeaderboardManager>.prop_T_0.field_Private_EnumNPublicSealedva3vUnique_0` vaut 0 une fois connecté (1 à l'écran titre). Ne jamais appeler l'authentification soi-même (`SignupAsync` peut créer un compte).
- Objet classement : `HighscoreManager.Method_Private_Static_InterfacePublicAbstractStObVoStObAc1UIObVoUnique_String_Int32_GameMode_EnumPublicSealedva5v2_EnumPublicSealedva22Unique_0(piste, section, mode, heure, filtre)` puis `.TryCast<ObjectPublicAbstractStInObBoObStInObInStUnique>()` ; filtre (`EnumPublicSealedva22Unique`) = période (1 jour, 2 semaine, 4 mois, 0/7 tous temps) | portée << 3 (0x08 moi, 0x10 amis, 0x38 global) : 0x3F = global tous temps (défaut de l'écran du jeu), 0xF = moi.
- Mon rang : `lb.Method_Public_Virtual_Final_New_Void_EnumPublicSealedva22Unique_Action_1_ValueTypePublicSealedBoUIVoBoUI1_0(0x3F, rappel)` → `{ bool succès, uint rang }`, rang 0 = aucun score. Un `System.Action<…>` C# passe tel quel (struct blittable).
- Ma ligne : l'API `ObjectPublicStInObLeCoObObUnique.prop_ObjectPublicStInObLeCoObObUnique_0.Method_Public_Task_1_ValueTypePublicSealedBoLi1ObUnique_String_ValueTypePublicSealedInObInInUnique_EnumPublicSealedva22Unique_EnumPublicSealedva22Unique_Int32_Func_1_List_1_UInt32_0(id, plage (0,0), 0xF, 0x3F, -1, null)` renvoie une `Task` à surveiller (`IsCompleted`, `Result`) ; entrées : `prop_UInt32_0` rang, `prop_UInt32_1` temps ms, `prop_UInt32_2` joueur, `prop_Int32_0` véhicule, `prop_Int32_1` chutes. Le rappel de liste du jeu (`Action<ValueTypePublicSealedBoLi1ObUnique>`, struct non blittable) n'est pas convertible par Il2CppInterop, et relire la ligne en cache depuis le rappel de rang lève une NullReferenceException.
- **Écritures à ne jamais appeler** : `ScoreRequests.SetScores`, `ScoreRequestSet`, la méthode d'envoi unique ci-dessus et ses implémentations, l'envoi de l'API (`Method_Public_Task_1_ValueTypePublicSealedBoBoBoUnique_UInt32_Int32_String_…`), `UserRequests.Create/Auth/Update/DeleteUser`, `StandaloneLeaderboardConnection.Connect/Authenticate/SignupAsync`, `SendRequestAsync`, les méthodes de profil du service en ligne (`…_Action_1_Boolean_0`).

**Incident du 2026-10-25 vers 17:05** (arrivée téléportée sur `RO_01_A4` avant le mode test) : vérifié à 19:05 avec `online RO_01_A4` et `record RO_01_A4` : aucun score en ligne d'Edia (total et sections 01-09, mode normal, jour, tous temps) et aucun record local. Rien à nettoyer.

**Piste forcée** (verrou contourné pour une course, § 4) : temps (méthode d'envoi unique et filets ci-dessous) et progression bloqués, le reste compte. La règle est testée avant le mode test : en mode test, le journal dit laquelle aurait joué (« résultat non enregistré : piste forcée » ou « [mode test] bloqué … (piste débloquée : hors mode test, enregistré normalement) »).

**Mode test de LmdMulti** (`TestMode = true` dans `LmdMulti.cfg`, rappelé en rouge dans le panneau) : prefix `return false` sur la méthode d'envoi unique ci-dessus, les succès (`AchievementManager.PDM_0`), les stats Steam (`StatisticManager.Method_Private_Void_StatisticsMetric_Single_2` / `_Int32_PDM_3`), les analytics et la progression ; plus des filets journalisés (« FILET ») sur l'envoi en ligne, l'écriture locale et `LeaderboardManager.Method_Private_Void_String_UInt32_PDM_0`. **Vérifié** le 2026-10-25 : franchissement de checkpoints puis arrivée par téléportation sur `AU_01_A4`, tous les temps (sections, total 7985 ms), la progression et les succès bloqués au premier niveau, aucun filet déclenché. Toute manche scriptée ou téléportée doit se faire en mode test. L'écran du jeu affiche quand même « record global » pour la section (affichage seul).

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

## 9. Manette (Rewired)

Le jeu lit clavier et manettes par **Rewired** (`Il2CppRewired_Core.dll`). Lu dans l'objet `Rewired.InputManager` du bundle `StreamingAssets/AssetBundles/resources/generic` (UnityPy, 2026-10-25) ; comportements marqués [vérifié] observés avec une manette virtuelle Xbox 360 (`tools/lmd-pad.py`) sur l'écran virtuel.

- Joueur `ReInput.players.GetPlayer(0)` (« Player0 »). Catégories de cartes : 0 Menu (toujours active), 2 GamePlay ; dispositions GamePlay 6 / 8 / 9 selon le préréglage manette du jeu. Pas de remappage utilisateur sauvegardé (préréglages seulement, dans `data_settings.sav`) : modifier les cartes en mémoire est sans lendemain.
- Identifiants d'éléments Xbox 360/One : boutons 6 A, 7 B, 8 X, 9 Y, 10 LB, 11 RB, 12 Back/View, 13 Start, 14 L3, 15 R3, 16-19 croix, 22 Guide ; axes 0-3 sticks, 4 LT, 5 RT. Proton présente la manette comme « XInput Gamepad 1 » (GUID d74a350e…, 360) [vérifié].
- Rôles (lus) : A = Boost + Sprint en piste, UI_Submit au menu (aussi « recommencer » après une chute [vérifié]) ; B = Suicide (crash volontaire, raison `ExternalTrigger` [vérifié]) en piste, UI_Cancel au menu ; Back = UI_Cancel (pause) en piste, rien au menu ; Start = UI_Cancel ; X/Y = UI_Action_1/2 ; LB frein (disposition 6) ou capture ; L3/R3 pages des classements. Écran de résultats : A continuer, B « continuer à rouler », Y recommencer.
- API utilisée par LmdMulti (`Race/PadReader`) : `ReInput.isReady` ; `player.controllers.Joysticks` / `joystickCount` ; `joy.GetButton(joy.GetButtonIndexById(id))` (`GetButtonById` et les gabarits `IGamepadTemplate` sont absents de l'interop) ; `player.controllers.GetLastActiveController().type` (`ControllerType.Joystick` = 2) pour savoir quel périphérique a servi en dernier [vérifié] ; `player.controllers.maps.GetAllMaps(Il2CppSystem…List<ControllerMap>)`, `map.ButtonMaps` (`ActionElementMap.elementIdentifierId`, `.id`), `map.DeleteElementMap(id)` et `map.enabled` [vérifié : Back retiré, cartes coupées puis rétablies]. Le jeu recharge ses cartes plusieurs fois au démarrage (profil, menus) : le retrait est refait chaque seconde.
- Input legacy d'Unity (`Input.GetKey(KeyCode.JoystickButtonN)`) : actif aussi, mais numérotation dépendante du backend : non utilisé.
- **Toutes les cartes** (commande `maps` de LmdExplorer → `UserData/LmdExplorer/maps.txt`, 2026-10-25, manette virtuelle branchée) :
  - Clavier GamePlay (disposition 0 active ; 1 et 2 pour les autres préréglages) : W/S et Haut/Bas `Acceleration_Axis`, A/D et Gauche/Droite direction, Maj gauche/droite `Boost`, Espace `Jump` + `UI_Action_1`, Ctrl `UI_Action_2`, Échap `UI_Cancel`, **F10 `Screenshot`**, Retour arrière `Suicide`, pavé numérique 0-3. Disposition 2 : Q/E `UI_Action_1/2`.
  - Clavier Default (menus) : Entrée, Échap, flèches et W/A/S/D navigation, Q/E onglets, F/G, R/T, X/Y/Z actions secondaires, Espace, Ctrl, Tab.
  - Manette GamePlay (disposition 8 active) : A `Boost`+`Sprint`, B `Suicide`, Start `UI_Cancel`, X/Y `UI_Action_1/2`, LB `Screenshot` (`Mouse_Brake` en disposition 6), stick gauche direction, gâchettes accélérer/freiner. **Libres en piste : RB, L3, R3, croix, stick droit.** Manette Default (menus) : croix et stick gauche navigation, LB/RB onglets, L3/R3 actions secondaires, gâchettes, stick droit.
- **Clavier et disposition** : les touches sont lues par leur lettre (code de touche virtuel), pas leur position [vérifié sous Proton : avec `setxkbmap -display :98 fr`, la touche Z (position du W) ne fait rien, W (en bas à gauche) fait rouler]. LmdMulti adapte les lettres en mémoire (`Tweaks/KeyboardRemap`) : suppression des éléments (`DeleteElementMap`) puis `CreateElementMap(actionId, pôle, KeyCode, ModifierKeyFlags, out élément)` ; `ActionElementMap.enabled` est en lecture seule. Le jeu ne sauvegarde pas les cartes : rien ne persiste.

## 10. Démarrage : logos, profil, écran titre

Relevé par la sonde de LmdExplorer (fichier drapeau `introprobe` : espionne les méthodes `void` d'instance de `SplashScreen`, `ProfileSelectionScreen`, `MainMenuScreen`, journalise écrans actifs et drapeaux de l'écran titre ; espionner aussi les méthodes statiques, `Task`/`IEnumerator` et `Game` a fait planter le jeu au démarrage), écran virtuel, 2026-10-25. Temps depuis le lancement de Proton :
- 0-8 s : écran noir (Proton, Unity, MelonLoader ; aucun logo Unity dans cette build), mods prêts à ~8 s, scène `gameplay` chargée.
- ~8-11 s : « Chargement… » (`Game.m_loadingLabel` ; `Game.m_preSplashScreenDelay` = 0,5, `m_preSplashScreen` nul).
- ~11-21,6 s : logos (`Il2CppMegagon.Downhill.UI.Screens.SplashScreen`, `Method_Protected_Virtual_Void_0` = mise en place, appelée deux fois) : Megagon, Thunderful, partenaires. Réglages lus : `m_preDelay` 1, `m_betweenDelay` 0,5, `m_waitTimeLogo` 2, `m_minWaitTimeLogo` 0,25, fondus 0,3/0,4, `m_scaleDuration` 0,3, `m_splashScreenWaitMultiplicator` [1,1 ; 1 ; 0,9], `m_splashScreenWaitForKeypress` tous faux, `m_splashScreenList` = 3 `CanvasGroup`. Le chargement du profil ne commence qu'après (`GameState` 0 → 1).
- `ProfileSelectionScreen` : « Chargement du profil… », « Connexion au serveur du classement… » (~1,4 s), « Connecté » affiché ~1,5 s, `OnProfileSelectionComplete`, `GameState` 1 → 10, scènes `Menu_*` chargées (~1,4 s).
- `MainMenuScreen` (~27 s) : invite « Appuyez sur … pour continuer ». Un appui : `field_Private_Boolean_0` (« attend un appui ») passe à faux, `Method_Private_Void_2` puis `ShowButtonList` ; l'invite disparaît, les boutons (Commencer la partie, Options…) prennent la main. `prop_Boolean_0` est vrai pendant les animations de l'écran : un appui simulé pendant ce temps affiche les boutons mais laisse l'invite, et l'appui suivant est perdu.
- Mettre les durées de `SplashScreen` à zéro dans un prefix de sa mise en place les supprime sans rien casser (profil, Steam, connexion au classement vérifiés) : logos en ~0,3 s.

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
- Liste : `dump`, `status`, `puppet`, `screens`, `mainstart`, `start`, `menu`, `catalogue`, `sel`, `select <ID>` (⚠ change aussi la piste active du profil : remettre `select CA_01_A2`, valeur d'Edia, après les essais), `restart`, `restartcp`, `lock`, `unlock`, `tostart`, `tocp [N|gN|f]` (respawn du checkpoint N, porte N, ligne d'arrivée ; enchaîner `g1`, `g2`… : sauter des portes fait chuter ou « point de contrôle manqué »), `timer`, `record <piste>` (records locaux), `online <piste>` (mes scores en ligne, lecture seule, § 5), `refs [Type]` (références Unity et valeurs simples des champs d'un composant, défaut `BikeLocomotion` et `PlayerCameraTarget` : nulle, détruite ou vivante), `res L H` (taille de la fenêtre pour la session, tests d'affichage), `call <Type> <Méthode> [args]`, `maps` (cartes Rewired, § 9).
- Touches : F11 dump, F12 marionnette de test (F6-F9 sont à LmdMulti, F10 à la capture d'écran du jeu).
- Fichiers drapeaux : `autostart` lance la piste sélectionnée depuis le menu ; `autodump` fait dump + marionnette de test à la première apparition du vélo ; `introprobe` sonde du démarrage (§ 10).
- ⚠ Une arrivée obtenue par téléportation peut être enregistrée par le jeu (records, classements en ligne, succès) : voir § 5 « Arrivées de test » avant tout essai : toujours avec LmdMulti en `TestMode`.
