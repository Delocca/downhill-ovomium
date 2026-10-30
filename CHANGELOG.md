# Changelog d'Ovomium

## Non publié

- Corrigé : les fantômes des joueurs qui n'ont jamais changé de tenue (tenue de base) prenaient votre propre tenue et votre vélo. Ils ont maintenant la leur.
- Un fantôme est reconstruit si son joueur change d'apparence.
- Vue première et troisième personne : le flou de focale du jeu est coupé, l'image est nette ; il revient en vue normale. Pour le garder : `DepthOfFieldInCustomViews = true` dans `Ovomium.cfg`.
- Journal plus précis sur l'apparence de chaque joueur, pour comprendre un fantôme mal habillé.
- Première mise à jour qui s'installe toute seule depuis la 0.5.0. Protocole réseau inchangé : 0.5.0 et 0.5.1 se voient.

## 0.5.0 — 2026-10-29

- **Mise à jour automatique** : les prochaines versions se téléchargent toutes seules à l'écran titre et s'installent au lancement suivant ; chaque version est signée et toute version modifiée est refusée. Cette 0.5.0 s'installe encore avec l'installeur.
- **Apparence propre à chaque joueur** : chaque fantôme porte la tenue et le vélo de son joueur ; sans le DLC correspondant, on voit son propre vélo à la place. Les fantômes translucides le sont maintenant entièrement (casque, cheveux, sac compris).
- ⚠ Protocole réseau v3 : tout le monde doit passer en 0.5.0 pour se voir.
- Touches par défaut F1 course, F2 aspect, F3 panneau, F4 écho, compatibles avec le mod LMD-VR.
- V / R3 : cycle des trois vues de LMD-Camera (normale, 1re personne, 3e personne) ; FOV de la 1re personne à 70.
- Corrigé : chargement figé après Mount Riley.
- Linux : avertissement si le pare-feu ufw bloque le jeu en réseau local.
