# Utiliser WinDock

[← Retour à l’accueil](../README.md) · [Développement](DEVELOPPEMENT.md)

Le guide détaillé de WinDock 0.0.9 pour Windows 10/11 x64 : ajout d’applications, organisation, réglages et mises à jour.

- [Essayer](#essayer)
- [Mettre à jour](#mettre-à-jour)
- [Utilisation](#utilisation)
- [Mode Coin](#mode-coin)
- [Démarrage automatique](#démarrage-automatique)
- [Données et limites](#données-et-limites)

## Essayer

Deux formats portables sont disponibles pour Windows x64, sans installation de .NET ni droits administrateur :

- **EXE autonome** : placez `WinDock.exe` dans un dossier permanent puis lancez-le. Conservez ce nom pour le démarrage automatique. Le runtime .NET est inclus ; ses bibliothèques natives sont extraites au premier lancement.
- **ZIP portable** (`WinDock-0.0.9-win-x64.zip`) : décompressez l’archive dans un dossier permanent puis lancez `WinDock.exe`. Conservez tous les fichiers extraits ensemble : ils comprennent le runtime .NET.

Le numéro de version est affiché en bas de **Réglages**. Pour produire les deux formats depuis les sources, consultez le [guide de développement](DEVELOPPEMENT.md#produire-une-livraison).

## Mettre à jour

Quittez WinDock via son menu contextuel. Pour l’EXE autonome, remplacez `WinDock.exe` par la nouvelle version. Pour le ZIP portable, remplacez l’ensemble des fichiers du dossier par ceux de la nouvelle archive. Les réglages et raccourcis sont conservés séparément dans `%LocalAppData%\WinDock` : ne supprimez pas ce dossier lors de la mise à jour. Relancez `WinDock.exe` et vérifiez la version dans **Réglages**. Si vous changez l’emplacement du dossier portable, désactivez puis réactivez le démarrage automatique depuis le nouvel emplacement.

## Dock de départ et restauration

Uniquement lorsque `settings.json` est absent, WinDock prépare cette sélection :

**Explorateur · Navigateur par défaut · Messagerie │ Dossier personnel · Téléchargements · Documents │ Corbeille**

Le navigateur et la messagerie affichent leurs vrais noms et leurs icônes. Ils sont résolus à partir des associations Windows **HTTPS** et **mailto**, sans reconstruire le catalogue complet ni charger toutes ses icônes. Le clic ouvre l’application normalement : aucune URL, aucun destinataire et aucune rédaction de message ne sont imposés. Sans messagerie identifiable, cette entrée est omise. Un gestionnaire mail renvoyant au même navigateur n’ajoute pas de doublon. Les dossiers utilisent leurs emplacements Windows réels, y compris les redirections vers OneDrive ou un autre disque. Les cibles indisponibles et les doublons sont omis ; aucun dossier utilisateur n’est créé.

La sélection est enregistrée immédiatement. Un dock existant, y compris vidé volontairement, n’est pas rempli à nouveau au redémarrage. La récupération d’un fichier de configuration corrompu ne déclenche pas non plus cette initialisation. Masquage automatique et apparence conservent leurs valeurs habituelles.

**Réglages → Général → Restaurer le dock par défaut…** ouvre immédiatement la confirmation avec **Préparation de la sélection…** et un indicateur d’activité. **Annuler** reste disponible pendant les réponses de Windows ou d’un dossier distant. La liste exacte remplace le chargement lorsqu’elle est prête, puis **Restaurer** devient disponible. En cas d’erreur, **Réessayer** relance la préparation dans cette fenêtre. La confirmation précise que vos raccourcis et groupes seront remplacés ; leurs fichiers d’origine sont conservés. **Annuler** nettoie les copies préparées sans modifier le dock, y compris si un résultat arrive après la fermeture. **Restaurer** enregistre le nouveau contenu et réactive la corbeille en une seule sauvegarde, avant de retirer les anciennes copies gérées inutilisées.

L’écran, le bord, l’opacité, le masquage automatique et le démarrage automatique sont conservés. Une erreur de préparation ou d’enregistrement garde l’ancien contenu et l’ancien état de la corbeille. Le navigateur et la messagerie sont résolus à nouveau lors de chaque restauration ; il n’existe pas de suivi automatique de vos changements d’association entre-temps. Les configurations existantes sont conservées lors d’une mise à jour : un ancien raccourci Paramètres n’est pas retiré automatiquement. Fermer les réglages pendant la préparation annule la restauration.

## Utilisation

Le dock apparaît initialement en bas de l’écran principal. Après masquage, une **fine barre arrondie** indique sa position, juste au-dessus de la barre des tâches si celle-ci est visible. Elle est discrète au repos, devient plus opaque à l’approche du pointeur et révèle le dock après 150 ms de survol. Sa zone de détection est plus large que la barre pour faciliter le geste. Le dock disparaît 600 ms après le départ du pointeur. L’indicateur s’oriente verticalement sur les bords gauche et droit et disparaît en plein écran.

- Déposez des raccourcis `.lnk`, des exécutables `.exe` ou des dossiers depuis l’Explorateur. Les doublons de chemin sont ignorés.
- **Clic droit sur le fond du dock → Ajouter…** ouvre **Ajouter au dock** : recherchez une application (sans distinction de casse ou d’accents), cochez plusieurs éléments et cliquez sur **Ajouter la sélection**. Les applications déjà présentes sont signalées. **Actualiser** recharge la liste. Lorsque le dock est vide, le texte **Clic droit pour ajouter** indique où ouvrir ce menu ; le glisser-déposer reste disponible.
- Le dernier catalogue et ses icônes sont conservés entre les démarrages. La fenêtre affiche ce cache puis actualise les applications en arrière-plan, en conservant recherche et sélection. Vous pouvez continuer à ajouter des applications pendant l’actualisation. Fermer la fenêtre laisse cette actualisation terminer.
- Le catalogue regroupe les applications classiques et Microsoft Store connues de Windows (`AppsFolder`), complétées par les raccourcis du menu Démarrer. Il ne recense pas tous les exécutables du disque. Les boutons **Ajout manuel…**, **Ajouter un dossier…** et **Ajouter un séparateur** restent accessibles.
- **Ajouter un séparateur**, depuis le catalogue ou le clic droit sur le fond du dock, ajoute une ligne en fin de dock. Faites glisser les séparateurs pour les déplacer ; le clic droit permet de les retirer.
- Cliquez sur une icône pour lancer sa cible si aucune fenêtre n’est reconnue. Si une fenêtre est déjà ouverte, WinDock la restaure si elle est minimisée et demande son passage au premier plan. Si plusieurs fenêtres sont reconnues, un menu près de l’icône permet de choisir par titre (« Fenêtre sans titre » si nécessaire). **Lancer à nouveau**, dans ce menu ou au clic droit, lance le raccourci avec ses arguments et son dossier de travail habituels ; l’application décide si elle ouvre une nouvelle instance. Faites glisser l’icône pour changer son ordre. Les raccourcis `.lnk` sont copiés dans les données de WinDock avec leurs propriétés : vous pouvez supprimer le raccourci du bureau après l’ajout.
- **Clic droit → Modifier / réparer…** permet de changer le nom, la cible, les arguments et le dossier de travail. Les paramètres sont préremplis ; les arguments saisis remplacent les anciens. Un remplacement par un autre `.lnk` reprend ses paramètres. Annuler ne change rien, et enregistrer conserve la position dans le dock.
- Un point orange indique une cible introuvable. Un clic propose **Choisir une nouvelle cible**. Les raccourcis spéciaux non éditables sont conservés tels quels ; choisissez un exécutable ou un autre raccourci pour modifier leurs paramètres.
- Un petit point vert en bas à droite indique qu’un processus correspondant à l’exécutable ou à l’identité Windows de l’application est lancé, même en arrière-plan. L’état est actualisé environ toutes les 1,5 secondes. Les dossiers et séparateurs n’ont pas d’indicateur. Certains lanceurs intermédiaires ou processus dont Windows interdit la lecture peuvent ne pas être reconnus. Explorer seul ne déclenche jamais le point vert d’une application Store.
- Pendant un glisser-déposer, l’élément déplacé s’atténue et une ligne bleue montre l’emplacement d’insertion. Le dépôt de nouveaux raccourcis respecte aussi ce repère.
- Pour retirer une application, un dossier ou un séparateur, faites-le glisser à l’extérieur du dock : au-delà d’une marge de 24 pixels WPF, une corbeille rouge **Retirer du dock** apparaît près du pointeur. Relâchez pour retirer l’entrée. Revenez dans le dock pour réordonner, ou appuyez sur Échap pour annuler. Relâcher dans la petite marge extérieure annule également le déplacement. Les fichiers d’origine ne sont jamais supprimés.
- Le clic droit sur une entrée propose ses propres actions, dont **Retirer du dock**. **Ajouter…**, **Ajouter un séparateur**, **Réglages…** et **Quitter WinDock** sont accessibles uniquement par clic droit sur le fond du dock. Retirer une entrée ne supprime pas le fichier.
- Les réglages permettent de choisir un bord ou un coin et un écran. Si cet écran est débranché, le dock revient sur l’écran principal ; il retrouve l’écran choisi lorsque celui-ci revient.
- Dans **Réglages → Apparence**, le curseur **Opacité du fond** va de 10 % à 100 %, avec aperçu immédiat. Les icônes gardent leur opacité. La valeur est enregistrée à la fermeture des réglages.
- **Masquage automatique**, activé par défaut, peut être désactivé dans les réglages : le dock reste alors visible au-dessus des fenêtres ordinaires, sans réserver d’espace. Il disparaît en plein écran et réapparaît à sa fermeture. L’indicateur de bord est désactivé dans ce mode.
- **Afficher la corbeille**, désactivé par défaut, ajoute la corbeille Windows en fin de dock. Son icône vide ou pleine est actualisée toutes les deux secondes. Les deux états utilisent les icônes Windows en haute définition (256 × 256 pixels), redimensionnées à la taille du dock ; si leur chargement échoue, WinDock utilise l’icône standard. Cliquez pour l’ouvrir ; **Vider la corbeille…** au clic droit utilise la confirmation Windows. Le menu contient uniquement **Vider la corbeille…** et **Masquer du dock**. Vider est grisé si elle est vide ou occupée ; un état inconnu laisse l’action accessible. **Masquer du dock** mémorise le masquage sans confirmation ; réactivez **Réglages → Apparence → Afficher la corbeille** pour la retrouver. La corbeille ne se déplace pas et n’accepte pas les dépôts. Une lecture indisponible conserve le dernier état connu.
- Au survol, les libellés s’affichent centrés sous l’icône sur un fond opaque arrondi, ou au-dessus lorsque l’espace manque. Les noms longs reviennent à la ligne.
- Les réglages s’ouvrent au premier plan et restent défilables sur les petits écrans ; ils ne restent pas devant les autres applications lorsque vous changez de fenêtre.
- Les menus se ferment au clic extérieur, lors d’un changement de fenêtre ou avec Échap.
- Le dock grandit et rétrécit avec ses entrées, sans bouton d’ajout. À la limite de l’écran, les icônes se réduisent proportionnellement pour rester toutes visibles, sans défilement.

Le dock n’apparaît pas devant une fenêtre au premier plan couvrant tout l’écran. Son apparition ne prend pas le focus clavier. Les réglages et sélecteurs de fichiers sont des fenêtres interactives normales.

## Réglages organisés

La fenêtre **Réglages** est redimensionnable et démarre dans un format compact de 640 × 640 unités WPF, réduit si nécessaire à la zone disponible. Elle conserve son en-tête, le numéro de version et le bouton **Terminer** visibles ; seul le contenu de l’onglet défile sur un petit écran.

- **Emplacement** : choix du bord ou du coin, orientation, activation au coin exact et écran. Sur une fenêtre assez large, le schéma et les options du coin se présentent côte à côte ; ils s’empilent lorsque l’espace manque.
- **Apparence** : masquage automatique, corbeille et opacité du fond, avec aperçu immédiat.
- **Général** : lancement au démarrage Windows et restauration du dock par défaut.

Les changements sont appliqués immédiatement, et enregistrés selon le fonctionnement existant. L’opacité est mémorisée en fermant la fenêtre. La navigation par Tab et les touches habituelles des onglets reste disponible. Sans masquage automatique, le réglage du coin exact est désactivé et un rappel indique que le dock reste visible, sauf en plein écran.

## Mode Coin

Dans **Réglages → Emplacement → Coin**, sélectionnez un coin sur le schéma d’écran puis **Horizontal** ou **Vertical**. L’aperçu et le résumé indiquent le sens d’ouverture. La première sélection utilise le bas à droite, à l’horizontale, sur l’écran déjà choisi. Les mises à jour conservent les placements existants et le retour au mode **Bord** retrouve le bord précédemment choisi.

Le repère en **L arrondi** se place dans le coin physique de l’écran, y compris au niveau de la barre des tâches. Il s’accentue à l’approche ; un survol continu de **300 ms** révèle le dock. Un passage plus rapide ou une sortie de cette petite zone annule l’ouverture. Le reste du bord ne déclenche rien. Le repère laisse traverser les clics ; les propres comportements de survol de Windows restent actifs.

L’option **Exiger le coin exact**, proposée dans les réglages du mode Coin, limite le déclenchement aux **2 × 2 pixels physiques à l’extrémité du coin**, quelle que soit l’échelle Windows. Elle permet de viser les boutons de fenêtre voisins sans ouvrir le dock. Approcher le repère le rend toujours plus visible, mais ne lance pas le délai d’ouverture. Les **300 ms** commencent uniquement dans ce petit coin et repartent de zéro si la souris en sort. Le maintien après ouverture conserve son passage confortable vers les icônes. L’option est désactivée par défaut et les configurations existantes conservent leur comportement ; elle est mémorisée au retour entre Bord et Coin. Placer volontairement la souris tout au bout du bouton Fermer pendant 300 ms peut donc encore ouvrir le dock si ce bouton touche exactement le coin physique.

Le dock apparaît dans la zone disponible fournie par Windows, avec une marge, pour éviter la barre des tâches réservée en haut, en bas, à gauche ou à droite. Déplacer cette barre recalcule le placement sans changer le coin physique du repère. Une barre Windows en masquage automatique peut temporairement se superposer au dock lorsqu’elle se révèle, car Windows ne réserve alors presque aucun espace. La première entrée est près du coin et les suivantes s’en éloignent : vers la gauche depuis un coin droit en mode horizontal, vers le haut depuis un coin bas en mode vertical. La corbeille reste à la fin de ce parcours. Changer de coin ne modifie pas l’ordre enregistré des entrées.

**Réglages → Emplacement → Coin → Inverser l’ordre des icônes** retourne immédiatement toute la rangée ; l’aperçu affiche le nouvel ordre. Les séparateurs, les tuiles de groupes et la corbeille sont inclus : avec l’inversion, la corbeille se retrouve près du coin. L’ordre à l’intérieur des groupes reste identique, et le glisser-déposer suit le sens affiché. Désactiver l’option retrouve le sens initial. Ce choix, désactivé par défaut, est enregistré pour tous les coins et orientations ; il est conservé mais sans effet en mode Bord.

Un passage entre le repère et l’icône la plus proche du coin maintient le dock ouvert pendant le déplacement de la souris. Il se masque après **600 ms** hors du dock et de ce passage. Les menus et groupes s’ouvrent vers l’intérieur de l’écran. L’apparition utilise un fondu, sans déplacer les cibles ; les animations sont désactivées lorsque Windows le demande. Le mode fixe conserve le même ancrage sans repère et le plein écran conserve son comportement habituel.

## Groupes dépliables

Glissez un raccourci au **centre d’un autre raccourci du dock**. Le halo et « Maintenir pour grouper » indiquent la cible. Après **500 ms**, le contour se renforce, un aperçu des deux icônes apparaît et le message devient **Relâcher pour créer un groupe**. Au lâcher, WinDock crée et ouvre **Nouveau groupe**, avec la cible puis le raccourci déplacé. Aucun dialogue n’est nécessaire. Un dépôt plus rapide ou dans les marges conserve le réordonnancement ; Échap annule sans changement.

La source peut être un membre d’un autre groupe, mais la cible doit être directement dans le dock. Séparateurs, corbeille, groupes déplacés, dépôt sur soi-même et dépôts depuis l’Explorateur ne créent pas de groupe.

- Un groupe contient **au moins deux raccourcis**, applications ou dossiers, même si une cible est manquante. Après un retrait ou un transfert, un groupe à un seul membre est automatiquement dissous et ce membre reprend sa position dans le dock. Un groupe vide disparaît. Création, transfert et dissolutions sont enregistrés ensemble ; une sauvegarde échouée conserve l’état précédent.
- Les anciens groupes de 0.0.3 contenant zéro ou un membre sont normalisés au démarrage sans considérer la configuration comme corrompue.
- Un clic ouvre une grille de quatre colonnes maximum avec noms et défilement. Le panneau reprend le fond, l’opacité et les arrondis du dock ; les textes et icônes restent opaques. Un second clic, Échap hors édition, un clic extérieur, un changement d’application ou le plein écran le referment.
- **Double-cliquez sur le titre** pour le renommer sur place. Entrée enregistre le nom sans espaces aux extrémités ; Échap annule. Quitter le champ enregistre un nom valide, sinon restaure l’ancien. **Renommer…** au clic droit ouvre la même édition. Le panneau prend le focus clavier uniquement pour cette action explicite.
- Le bouton **+** (« Ajouter au groupe » au survol) ouvre le catalogue pour ce groupe ; ajout manuel et dossiers gardent cette destination. Les dépôts depuis l’Explorateur et la détection globale des doublons restent disponibles.
- Pour ajouter à un groupe existant, son halo affiche **Ajouter à « Nom »**. Après 500 ms de survol, sa grille s’ouvre. Déposez au repère bleu, ou directement sur la tuile pour ajouter en fin de groupe. Ses bords haut et bas permettent de défiler pendant le déplacement.
- **Déplacer vers…** propose les groupes existants et le dock. **Dissoudre le groupe** remet tous ses membres à sa place, dans leur ordre actuel. Les raccourcis conservent activation des fenêtres, relancement, réparation et commandes de fermeture. Les fichiers d’origine restent conservés.

Les groupes ne contiennent ni sous-groupes ni séparateurs et ne lancent pas toutes leurs applications simultanément. Glisser un groupe hors du dock annule son déplacement.

Avant la première sauvegarde introduisant des groupes, WinDock conserve une copie datée **settings.json.before-groups-*** dans `%LocalAppData%\WinDock`. Si la copie échoue, la création n’est pas enregistrée. Les anciennes configurations se chargent directement dans 0.0.4. Pour revenir à une version antérieure à 0.0.3, quittez WinDock, conservez une copie du `settings.json` actuel et restaurez la sauvegarde précédant les groupes sous le nom `settings.json`. Les raccourcis modifiés ou retirés depuis cette sauvegarde peuvent nécessiter une réparation.

## Démarrage automatique

Dans **Réglages → Général**, cochez **Lancer WinDock au démarrage de Windows** pour lancer le dock à l’ouverture de votre session. Cette option est désactivée par défaut et prend effet immédiatement. Elle crée un raccourci `WinDock.lnk` dans le dossier Démarrage de votre compte, sans droits administrateur. Décochez-la avant de supprimer la version portable. Après un déplacement du dossier, désactivez puis réactivez l’option depuis le nouvel emplacement. Si Windows a désactivé cette entrée dans ses applications de démarrage, réactivez-la également dans les paramètres Windows.

## Données et limites

La détection des applications ouvertes reconnaît aussi les lanceurs Squirrel (par exemple Claude Desktop) dont le processus utilise un sous-dossier `app-<version>` de la même installation. Un exécutable homonyme situé ailleurs, comme Claude en ligne de commande, reste distinct.

Un clic droit sur une application ouverte propose **Fermer l’application**, ou **Fermer toutes les fenêtres** si plusieurs fenêtres sont reconnues, y compris minimisées. L’application peut demander un enregistrement, annuler ou ignorer la fermeture ; aucun processus n’est forcé à quitter. Sans fenêtre identifiable, cette commande est absente. Plusieurs raccourcis vers le même exécutable partagent les fenêtres reconnues pour l’activation et la fermeture, quels que soient leurs arguments. Si Windows refuse une activation ou si la fenêtre a disparu, WinDock affiche un message sans lancer une autre instance. Si la recherche échoue, un menu propose explicitement **Lancer à nouveau**. Le point vert reste présent tant que le processus est actif, même sans fenêtre. Les applications classiques ajoutées via le catalogue Windows sont reconnues par le chemin réel communiqué par Windows, même si leurs processus ne fournissent pas l’identifiant du catalogue ; les installations différentes portant le même nom restent distinctes. Les applications empaquetées conservent leur reconnaissance par identité Windows.

L’Explorateur Windows n’affiche pas de point vert, car son processus fait aussi fonctionner le bureau et la barre des tâches. Sur son icône, **Redémarrer l’Explorateur Windows…** remplace **Lancer à nouveau**. Une confirmation prévient que des fenêtres de dossiers et des copies peuvent être interrompues ; **Non** est sélectionné par défaut. Le redémarrage forcé cible uniquement le processus du bureau de la session courante, sans arrêter ses applications enfants. Si Windows le relance automatiquement, WinDock ne lance pas une seconde instance. Les fenêtres de dossiers hébergées dans des processus distincts ne sont pas arrêtées. Les entrées de dossiers conservent leur comportement habituel.

Les données sont enregistrées dans `%LocalAppData%\WinDock\settings.json`, indépendamment du dossier portable. Les copies de raccourcis sont dans le sous-dossier `Shortcuts`. Les anciennes entrées `.lnk` encore accessibles sont copiées au démarrage ; celles déjà supprimées restent réparables. Une configuration invalide est copiée dans un fichier `settings.json.invalid-*` avant le retour aux valeurs par défaut. La modification ou la suppression d’une entrée conserve l’ancienne version si la sauvegarde échoue. Les raccourcis d’origine ne sont jamais modifiés par WinDock.

Les applications du catalogue utilisent une copie du raccourci classique lorsqu’il est identifiable, avec ses arguments et son dossier de travail. Sinon, WinDock crée un raccourci vers leur cible Shell Windows, sans lancer un exécutable interne à WindowsApps. Ces cibles spéciales permettent de modifier le nom ou de remplacer la cible, mais pas les arguments. Une application absente d’un catalogue lu avec succès est signalée comme manquante ; une erreur de lecture du catalogue ne suffit pas à la déclarer supprimée.

Le cache est stocké séparément dans `%LocalAppData%\WinDock\CatalogCache\catalog.json`, avec ses icônes PNG intégrées. Une lecture Windows incomplète ou indisponible conserve le dernier catalogue complet. Un cache illisible est reconstruit ; il n’est jamais utilisé comme preuve de désinstallation. Le tout premier chargement nécessite toujours l’interrogation de Windows.

WinDock ne modifie ni le bureau ni la barre des tâches et n’exige pas de droits administrateur. Le démarrage automatique est facultatif. Une seconde instance quitte immédiatement. Elle ne gère pas les icônes de notification. L’exécutable n’est pas signé.
