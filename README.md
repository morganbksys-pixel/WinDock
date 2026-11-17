# WinDock

Vos applications et dossiers favoris, à portée de clic dans un dock discret pour Windows.

![Aperçu du dock par défaut avec navigateur, messagerie, dossiers et corbeille Windows](docs/images/dock.png)

**[Télécharger WinDock](https://github.com/morganbksys-pixel/WinDock/releases/latest)**

Pour commencer, choisissez **WinDock.exe**, l’EXE autonome prêt à lancer. Vous préférez une archive ? Le **ZIP portable** est disponible sur la même page : décompressez-le et gardez tous ses fichiers ensemble.

## Vos raccourcis, à votre façon

- **Ajoutez vos favoris** depuis le catalogue des applications classiques et Microsoft Store, ou déposez vos raccourcis et dossiers depuis l’Explorateur.
- **Organisez le dock** par glisser-déposer : utilisez des séparateurs ou glissez une icône sur une autre pour créer un groupe dépliable. Double-cliquez sur son titre pour le renommer.
- **Gardez votre écran dégagé** avec le masquage automatique ; survolez la petite barre pour retrouver le dock, ou choisissez de le garder visible.
- **Choisissez sa place** sur un bord ou dans un coin de l’écran, avec une ouverture horizontale ou verticale depuis ce coin.
- **Repérez les applications ouvertes** grâce au point vert et demandez la fermeture de leurs fenêtres depuis le clic droit.

Au premier lancement, WinDock prépare l’Explorateur, votre navigateur, votre messagerie, votre dossier personnel, Téléchargements, Documents et la corbeille, selon leur disponibilité. Les configurations existantes sont conservées. **Réglages → Général → Restaurer le dock par défaut…** ouvre immédiatement une confirmation et prépare la sélection en arrière-plan, en gardant votre apparence et votre emplacement. La messagerie utilise l’application associée à mailto lorsqu’elle est identifiable, sans ouvrir de nouveau message.

![Exemple de groupe déplié avec des applications](docs/images/group.png)

## Démarrer en trois étapes

1. **Téléchargez et lancez** `WinDock.exe` depuis un dossier permanent.
2. **Ajoutez vos applications** avec **Clic droit → Ajouter…**, ou glissez vos raccourcis sur le dock.
3. **Personnalisez-le** avec **Clic droit → Réglages** : bord ou coin, orientation, écran, opacité, masquage automatique et corbeille.

En mode **Coin**, un petit repère en L arrondi marque le coin physique choisi. Survolez-le pendant **300 ms** pour retrouver le dock ; sa première icône reste près du coin et les suivantes se déploient vers l’intérieur. Le dock ouvert évite la barre des tâches, et le passage entre le repère et les icônes le maintient visible. Les placements existants sont conservés lors d’une mise à jour.

Près des boutons de fenêtre, activez **Réglages → Emplacement → Coin → Exiger le coin exact** : seule l’extrémité du coin révèle le dock, après les mêmes 300 ms. Survoler le reste du repère ne l’ouvre plus.

Pour retourner la rangée en mode **Coin**, choisissez **Clic droit → Inverser l’ordre des icônes**, ou activez le même interrupteur dans **Réglages → Emplacement → Coin**. Les séparateurs, les tuiles de groupes et la corbeille changent aussi de côté ; l’intérieur des groupes garde son ordre. Ce choix est mémorisé pour tous les coins et orientations, sans modifier l’ordre du mode Bord.

Les réglages sont répartis en trois onglets : **Emplacement** pour l’écran, les bords et les coins ; **Apparence** pour le masquage, la corbeille et l’opacité ; **Général** pour le démarrage et la restauration du dock. La fenêtre est redimensionnable, avec un bouton Terminer toujours accessible.

[Aperçu des nouveaux réglages](docs/images/settings.png)

Un clic lance une application ou retrouve sa fenêtre ouverte ; si plusieurs fenêtres sont reconnues, choisissez celle à afficher. Le clic droit permet aussi de **Lancer à nouveau**. Faites glisser une icône pour la déplacer ; pour la retirer, sortez-la du dock jusqu’à voir la corbeille rouge. Vos fichiers d’origine sont conservés.

## Pour aller plus loin

- [Guide d’utilisation](docs/UTILISATION.md) : raccourcis, réglages, mises à jour et limites.
- [Développement et publication](docs/DEVELOPPEMENT.md) : compiler, tester et produire les livrables.

WinDock fonctionne sur **Windows 10/11 x64**, sans installation de .NET ni droits administrateur. L’exécutable n’est pas signé. Vos réglages et raccourcis sont conservés séparément dans `%LocalAppData%\WinDock`, y compris lors d’une mise à jour.
