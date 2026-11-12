# Utiliser WinDock

[← Retour à l’accueil](../README.md) · [Développement](DEVELOPPEMENT.md)

Le guide détaillé de WinDock 0.0.1 pour Windows 10/11 x64 : ajout d’applications, organisation, réglages et mises à jour.

- [Essayer](#essayer)
- [Mettre à jour](#mettre-à-jour)
- [Utilisation](#utilisation)
- [Démarrage automatique](#démarrage-automatique)
- [Données et limites](#données-et-limites)

## Essayer

Deux formats portables sont disponibles pour Windows x64, sans installation de .NET ni droits administrateur :

- **EXE autonome** : placez `WinDock.exe` dans un dossier permanent puis lancez-le. Conservez ce nom pour le démarrage automatique. Le runtime .NET est inclus ; ses bibliothèques natives sont extraites au premier lancement.
- **ZIP portable** (`WinDock-0.0.1-win-x64.zip`) : décompressez l’archive dans un dossier permanent puis lancez `WinDock.exe`. Conservez tous les fichiers extraits ensemble : ils comprennent le runtime .NET.

Le numéro de version est affiché en bas de **Réglages**. Pour produire les deux formats depuis les sources, consultez le [guide de développement](DEVELOPPEMENT.md#produire-une-livraison).

## Mettre à jour

Quittez WinDock via son menu contextuel. Pour l’EXE autonome, remplacez `WinDock.exe` par la nouvelle version. Pour le ZIP portable, remplacez l’ensemble des fichiers du dossier par ceux de la nouvelle archive. Les réglages et raccourcis sont conservés séparément dans `%LocalAppData%\WinDock` : ne supprimez pas ce dossier lors de la mise à jour. Relancez `WinDock.exe` et vérifiez la version dans **Réglages**. Si vous changez l’emplacement du dossier portable, désactivez puis réactivez le démarrage automatique depuis le nouvel emplacement.

## Utilisation

Le dock apparaît initialement en bas de l’écran principal. Après masquage, une **fine barre arrondie** indique sa position, juste au-dessus de la barre des tâches si celle-ci est visible. Elle est discrète au repos, devient plus opaque à l’approche du pointeur et révèle le dock après 150 ms de survol. Sa zone de détection est plus large que la barre pour faciliter le geste. Le dock disparaît 600 ms après le départ du pointeur. L’indicateur s’oriente verticalement sur les bords gauche et droit et disparaît en plein écran.

- Déposez des raccourcis `.lnk`, des exécutables `.exe` ou des dossiers depuis l’Explorateur. Les doublons de chemin sont ignorés.
- **Clic droit → Ajouter…** ouvre **Ajouter au dock** : recherchez une application (sans distinction de casse ou d’accents), cochez plusieurs éléments et cliquez sur **Ajouter la sélection**. Les applications déjà présentes sont signalées. **Actualiser** recharge la liste. Lorsque le dock est vide, le texte **Clic droit pour ajouter** indique où ouvrir ce menu ; le glisser-déposer reste disponible.
- Le dernier catalogue et ses icônes sont conservés entre les démarrages. La fenêtre affiche ce cache puis actualise les applications en arrière-plan, en conservant recherche et sélection. Vous pouvez continuer à ajouter des applications pendant l’actualisation. Fermer la fenêtre laisse cette actualisation terminer.
- Le catalogue regroupe les applications classiques et Microsoft Store connues de Windows (`AppsFolder`), complétées par les raccourcis du menu Démarrer. Il ne recense pas tous les exécutables du disque. Les boutons **Ajout manuel…**, **Ajouter un dossier…** et **Ajouter un séparateur** restent accessibles.
- **Ajouter un séparateur**, depuis le catalogue ou le clic droit, crée une ligne entre les groupes. Depuis une icône, elle est ajoutée juste après celle-ci. Faites glisser les séparateurs pour les déplacer ; le clic droit permet de les retirer.
- Cliquez sur une icône pour lancer sa cible ; faites-la glisser pour changer son ordre. Les raccourcis `.lnk` sont copiés dans les données de WinDock avec leurs propriétés : vous pouvez supprimer le raccourci du bureau après l’ajout.
- **Clic droit → Modifier / réparer…** permet de changer le nom, la cible, les arguments et le dossier de travail. Les paramètres sont préremplis ; les arguments saisis remplacent les anciens. Un remplacement par un autre `.lnk` reprend ses paramètres. Annuler ne change rien, et enregistrer conserve la position dans le dock.
- Un point orange indique une cible introuvable. Un clic propose **Choisir une nouvelle cible**. Les raccourcis spéciaux non éditables sont conservés tels quels ; choisissez un exécutable ou un autre raccourci pour modifier leurs paramètres.
- Un petit point vert en bas à droite indique qu’un processus correspondant à l’exécutable ou à l’identité Windows de l’application est lancé, même en arrière-plan. L’état est actualisé environ toutes les 1,5 secondes. Les dossiers et séparateurs n’ont pas d’indicateur. Certains lanceurs intermédiaires ou processus dont Windows interdit la lecture peuvent ne pas être reconnus. Explorer seul ne déclenche jamais le point vert d’une application Store.
- Pendant un glisser-déposer, l’élément déplacé s’atténue et une ligne bleue montre l’emplacement d’insertion. Le dépôt de nouveaux raccourcis respecte aussi ce repère.
- Pour retirer une application, un dossier ou un séparateur, faites-le glisser à l’extérieur du dock : au-delà d’une marge de 24 pixels WPF, une corbeille rouge **Retirer du dock** apparaît près du pointeur. Relâchez pour retirer l’entrée. Revenez dans le dock pour réordonner, ou appuyez sur Échap pour annuler. Relâcher dans la petite marge extérieure annule également le déplacement. Les fichiers d’origine ne sont jamais supprimés.
- Le clic droit propose **Retirer du dock**, **Réglages** et **Quitter WinDock**. Retirer une entrée ne supprime pas le fichier.
- Les réglages permettent de choisir un bord et un écran. Si cet écran est débranché, le dock revient sur l’écran principal ; il retrouve l’écran choisi lorsque celui-ci revient.
- Dans **Réglages**, le curseur **Opacité du fond** va de 10 % à 100 %, avec aperçu immédiat. Les icônes gardent leur opacité. La valeur est enregistrée à la fermeture des réglages.
- **Masquage automatique**, activé par défaut, peut être désactivé dans les réglages : le dock reste alors visible au-dessus des fenêtres ordinaires, sans réserver d’espace. Il disparaît en plein écran et réapparaît à sa fermeture. L’indicateur de bord est désactivé dans ce mode.
- **Afficher la corbeille**, désactivé par défaut, ajoute la corbeille Windows en fin de dock. Son icône vide ou pleine est actualisée toutes les deux secondes. Cliquez pour l’ouvrir ; **Vider la corbeille…** au clic droit utilise la confirmation Windows. La corbeille ne se déplace pas et n’accepte pas les dépôts ; retirez-la avec l’interrupteur des réglages. Une lecture indisponible conserve le dernier état connu.
- Au survol, les libellés s’affichent centrés sous l’icône sur un fond opaque arrondi, ou au-dessus lorsque l’espace manque. Les noms longs reviennent à la ligne.
- Les réglages s’ouvrent au premier plan et restent défilables sur les petits écrans ; ils ne restent pas devant les autres applications lorsque vous changez de fenêtre.
- Les menus se ferment au clic extérieur, lors d’un changement de fenêtre ou avec Échap.
- Le dock grandit et rétrécit avec ses entrées, sans bouton d’ajout. À la limite de l’écran, les icônes se réduisent proportionnellement pour rester toutes visibles, sans défilement.

Le dock n’apparaît pas devant une fenêtre au premier plan couvrant tout l’écran. Son apparition ne prend pas le focus clavier. Les réglages et sélecteurs de fichiers sont des fenêtres interactives normales.

## Démarrage automatique

Dans **Réglages**, cochez **Lancer WinDock au démarrage de Windows** pour lancer le dock à l’ouverture de votre session. Cette option est désactivée par défaut et prend effet immédiatement. Elle crée un raccourci `WinDock.lnk` dans le dossier Démarrage de votre compte, sans droits administrateur. Décochez-la avant de supprimer la version portable. Après un déplacement du dossier, désactivez puis réactivez l’option depuis le nouvel emplacement. Si Windows a désactivé cette entrée dans ses applications de démarrage, réactivez-la également dans les paramètres Windows.

## Données et limites

La détection des applications ouvertes reconnaît aussi les lanceurs Squirrel (par exemple Claude Desktop) dont le processus utilise un sous-dossier `app-<version>` de la même installation. Un exécutable homonyme situé ailleurs, comme Claude en ligne de commande, reste distinct.

Un clic droit sur une application ouverte propose **Fermer l’application**, ou **Fermer toutes les fenêtres** si plusieurs fenêtres sont reconnues, y compris minimisées. L’application peut demander un enregistrement, annuler ou ignorer la fermeture ; aucun processus n’est forcé à quitter. Sans fenêtre identifiable, cette commande est absente. Plusieurs raccourcis vers le même exécutable partagent cette action, quels que soient leurs arguments. Le point vert reste présent tant que le processus est actif.

Les données sont enregistrées dans `%LocalAppData%\WinDock\settings.json`, indépendamment du dossier portable. Les copies de raccourcis sont dans le sous-dossier `Shortcuts`. Les anciennes entrées `.lnk` encore accessibles sont copiées au démarrage ; celles déjà supprimées restent réparables. Une configuration invalide est copiée dans un fichier `settings.json.invalid-*` avant le retour aux valeurs par défaut. La modification ou la suppression d’une entrée conserve l’ancienne version si la sauvegarde échoue. Les raccourcis d’origine ne sont jamais modifiés par WinDock.

Les applications du catalogue utilisent une copie du raccourci classique lorsqu’il est identifiable, avec ses arguments et son dossier de travail. Sinon, WinDock crée un raccourci vers leur cible Shell Windows, sans lancer un exécutable interne à WindowsApps. Ces cibles spéciales permettent de modifier le nom ou de remplacer la cible, mais pas les arguments. Une application absente d’un catalogue lu avec succès est signalée comme manquante ; une erreur de lecture du catalogue ne suffit pas à la déclarer supprimée.

Le cache est stocké séparément dans `%LocalAppData%\WinDock\CatalogCache\catalog.json`, avec ses icônes PNG intégrées. Une lecture Windows incomplète ou indisponible conserve le dernier catalogue complet. Un cache illisible est reconstruit ; il n’est jamais utilisé comme preuve de désinstallation. Le tout premier chargement nécessite toujours l’interrogation de Windows.

WinDock ne modifie ni le bureau ni la barre des tâches et n’exige pas de droits administrateur. Le démarrage automatique est facultatif. Une seconde instance quitte immédiatement. Elle ne gère pas les icônes de notification. L’exécutable n’est pas signé.
