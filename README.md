# WinDock

Vos applications et dossiers favoris, à portée de clic dans un dock discret pour Windows.

**[Tester la bêta 0.0.15 — ZIP portable Windows x64](https://github.com/morganbksys-pixel/WinDock/releases/download/v0.0.15/WinDock-0.0.15-win-x64.zip)**. Quittez l’ancienne instance, décompressez le ZIP dans un nouveau dossier et lancez `WinDock.exe`.

![Aperçu du dock par défaut avec navigateur, messagerie, dossiers et corbeille Windows](docs/images/dock.png)

**[Télécharger WinDock 0.0.12](https://github.com/morganbksys-pixel/WinDock/releases/tag/v0.0.12)**

- **[EXE autonome — WinDock.exe](https://github.com/morganbksys-pixel/WinDock/releases/download/v0.0.12/WinDock.exe)** : prêt à lancer depuis un dossier permanent, avec le runtime .NET inclus.
- **[ZIP portable — WinDock-0.0.12-win-x64.zip](https://github.com/morganbksys-pixel/WinDock/releases/download/v0.0.12/WinDock-0.0.12-win-x64.zip)** : décompressez-le et gardez tous ses fichiers ensemble.

## Vos raccourcis, à votre façon

- **Ajoutez vos favoris** depuis le catalogue des applications classiques et Microsoft Store, ou déposez vos raccourcis et dossiers depuis l’Explorateur.
- **Organisez le dock** par glisser-déposer : utilisez des séparateurs ou glissez une icône sur une autre pour créer un groupe dépliable. Double-cliquez sur son titre pour le renommer.
- **Gardez votre écran dégagé** avec le masquage automatique ; survolez la petite barre pour retrouver le dock, ou choisissez de le garder visible.
- **Choisissez sa place** sur un bord ou dans un coin de l’écran, avec une ouverture horizontale ou verticale depuis ce coin.
- **Repérez les applications actives** grâce au point vert, même sans fenêtre ouverte, et demandez la fermeture de leurs fenêtres depuis le clic droit. L’Explorateur Windows n’affiche pas ce point ; son menu propose **Redémarrer l’Explorateur Windows…**, avec confirmation.

Au premier lancement, WinDock prépare l’Explorateur, votre navigateur, votre messagerie, votre dossier personnel, Téléchargements, Documents et la corbeille, selon leur disponibilité. Les configurations existantes sont conservées. **Réglages → Général → Restaurer le dock par défaut…** ouvre immédiatement une confirmation et prépare la sélection en arrière-plan, en gardant votre apparence et votre emplacement. La messagerie utilise l’application associée à mailto lorsqu’elle est identifiable, sans ouvrir de nouveau message.

![Exemple de groupe déplié avec des applications](docs/images/group.png)

## Démarrer en trois étapes

1. **Téléchargez et lancez** `WinDock.exe` depuis un dossier permanent.
2. **Ajoutez vos applications** avec **Clic droit sur le fond du dock → Ajouter une application…**, ou glissez vos raccourcis sur le dock.
3. **Personnalisez-le** avec le bouton engrenage **Réglages** dans le dock : bord ou coin, orientation, écran, opacité, masquage automatique et corbeille.

Même quand le dock est masqué, **Ctrl + Alt + D** bascule le masquage automatique et **Ctrl + Alt + P** ouvre les réglages. Les deux combinaisons peuvent être modifiées ou désactivées dans **Réglages → Clavier**.

En mode **Coin**, un petit repère en L arrondi marque le coin physique choisi. Survolez-le pendant **300 ms** pour retrouver le dock ; sa première icône reste près du coin et les suivantes se déploient vers l’intérieur. Le dock ouvert évite la barre des tâches, et le passage entre le repère et les icônes le maintient visible. Les placements existants sont conservés lors d’une mise à jour.

Près des boutons de fenêtre, activez **Réglages → Emplacement → Coin → Exiger le coin exact** : seule l’extrémité du coin révèle le dock, après les mêmes 300 ms. Survoler le reste du repère ne l’ouvre plus.

Pour retourner la rangée en mode **Coin**, activez **Réglages → Emplacement → Coin → Inverser l’ordre des icônes**. Les séparateurs, les tuiles de groupes et la corbeille changent aussi de côté ; l’intérieur des groupes garde son ordre. Ce choix est mémorisé pour tous les coins et orientations, sans modifier l’ordre du mode Bord.

Les réglages sont répartis en cinq onglets : **Emplacement** pour l’écran, les bords et les coins ; **Apparence** pour le masquage, la corbeille et l’opacité ; **Général** pour le démarrage et la restauration du dock ; **Clavier** pour les raccourcis, et **Menus** pour choisir les commandes du clic droit. La fenêtre est redimensionnable, avec un bouton Terminer toujours accessible.

[Aperçu des nouveaux réglages](docs/images/settings.png)

Un clic lance une application ou retrouve sa fenêtre ouverte ; si plusieurs fenêtres sont reconnues, choisissez celle à afficher. Le clic droit permet aussi de **Lancer à nouveau**. Faites glisser une icône pour la déplacer ; pour la retirer, sortez-la du dock jusqu’à voir la corbeille rouge. Vos fichiers d’origine sont conservés.

Les applications classiques ajoutées depuis le catalogue Windows, comme Firefox, sont reconnues par leur exécutable réel lorsque leurs processus ne fournissent pas d’identifiant Windows. Les entrées existantes en bénéficient sans nouvel ajout.

Un clic droit sur le fond du dock ou sur l’engrenage ouvre le menu général. Pour créer une copie à côté d’un séparateur, faites un clic droit dessus et choisissez **Dupliquer le séparateur**. Le menu de la corbeille propose **Vider la corbeille…** (grisé si elle est vide) et **Masquer du dock**. Pour la réafficher, activez **Réglages → Apparence → Afficher la corbeille**.

## Pour aller plus loin

- [Guide d’utilisation](docs/UTILISATION.md) : raccourcis, réglages, mises à jour et limites.
- [Développement et publication](docs/DEVELOPPEMENT.md) : compiler, tester et produire les livrables.

WinDock fonctionne sur **Windows 10/11 x64**, sans installation de .NET ni droits administrateur. L’exécutable n’est pas signé. Vos réglages et raccourcis sont conservés séparément dans `%LocalAppData%\WinDock`, y compris lors d’une mise à jour.
