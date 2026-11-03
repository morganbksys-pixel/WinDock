# WinDock

Prototype de launcher natif pour Windows 10/11 x64, écrit en C# / WPF (.NET 10).

## Essayer

Ouvrez `artifacts/WinDock-win-x64/WinDock.exe`. Conservez tout le dossier à côté de l’exécutable : le runtime .NET est inclus, aucune installation n’est nécessaire.

Le dépôt Git contient les sources, pas l’exécutable ni le runtime. Après un clonage, utilisez les commandes de développement ci-dessous pour lancer le projet ou produire le dossier portable.

Le dock apparaît initialement en bas de l’écran principal. Après masquage, une **fine barre arrondie** indique sa position, juste au-dessus de la barre des tâches si celle-ci est visible. Elle est discrète au repos, devient plus opaque à l’approche du pointeur et révèle le dock après 150 ms de survol. Sa zone de détection est plus large que la barre pour faciliter le geste. Le dock disparaît 600 ms après le départ du pointeur. L’indicateur s’oriente verticalement sur les bords gauche et droit et disparaît en plein écran.

- Déposez des raccourcis `.lnk`, des exécutables `.exe` ou des dossiers depuis l’Explorateur. Les doublons de chemin sont ignorés.
- Le bouton **+** ouvre **Ajouter au dock** : recherchez une application (sans distinction de casse ou d’accents), cochez plusieurs éléments et cliquez sur **Ajouter la sélection**. Les applications déjà présentes sont signalées. **Actualiser** recharge la liste.
- Le dernier catalogue et ses icônes sont conservés entre les démarrages. La fenêtre affiche ce cache puis actualise les applications en arrière-plan, en conservant recherche et sélection. Vous pouvez continuer à ajouter des applications pendant l’actualisation. Fermer la fenêtre laisse cette actualisation terminer.
- Le catalogue regroupe les applications classiques et Microsoft Store connues de Windows (`AppsFolder`), complétées par les raccourcis du menu Démarrer. Il ne recense pas tous les exécutables du disque. Les boutons **Ajout manuel…**, **Ajouter un dossier…** et **Ajouter un séparateur** restent accessibles.
- **Ajouter un séparateur**, depuis **+** ou le clic droit, crée une ligne entre les groupes. Depuis une icône, elle est ajoutée juste après celle-ci. Faites glisser les séparateurs pour les déplacer ; le clic droit permet de les retirer.
- Cliquez sur une icône pour lancer sa cible ; faites-la glisser pour changer son ordre. Les raccourcis `.lnk` sont copiés dans les données de WinDock avec leurs propriétés : vous pouvez supprimer le raccourci du bureau après l’ajout.
- **Clic droit → Modifier / réparer…** permet de changer le nom, la cible, les arguments et le dossier de travail. Les paramètres sont préremplis ; les arguments saisis remplacent les anciens. Un remplacement par un autre `.lnk` reprend ses paramètres. Annuler ne change rien, et enregistrer conserve la position dans le dock.
- Un point orange indique une cible introuvable. Un clic propose **Choisir une nouvelle cible**. Les raccourcis spéciaux non éditables sont conservés tels quels ; choisissez un exécutable ou un autre raccourci pour modifier leurs paramètres.
- Un petit point vert en bas à droite indique qu’un processus correspondant à l’exécutable ou à l’identité Windows de l’application est lancé, même en arrière-plan. L’état est actualisé environ toutes les 1,5 secondes. Les dossiers et séparateurs n’ont pas d’indicateur. Certains lanceurs intermédiaires ou processus dont Windows interdit la lecture peuvent ne pas être reconnus. Explorer seul ne déclenche jamais le point vert d’une application Store.
- Pendant un glisser-déposer, l’élément déplacé s’atténue et une ligne bleue montre l’emplacement d’insertion. Le dépôt de nouveaux raccourcis respecte aussi ce repère.
- Pour retirer une application, un dossier ou un séparateur, faites-le glisser à l’extérieur du dock : au-delà d’une marge de 24 pixels WPF, une corbeille rouge **Retirer du dock** apparaît près du pointeur. Relâchez pour retirer l’entrée. Revenez dans le dock pour réordonner, ou appuyez sur Échap pour annuler. Relâcher dans la petite marge extérieure annule également le déplacement. Les fichiers d’origine ne sont jamais supprimés.
- Le clic droit propose **Retirer du dock**, **Réglages** et **Quitter WinDock**. Retirer une entrée ne supprime pas le fichier.
- Les réglages permettent de choisir un bord et un écran. Si cet écran est débranché, le dock revient sur l’écran principal ; il retrouve l’écran choisi lorsque celui-ci revient.
- Dans **Réglages**, le curseur **Opacité du fond** va de 10 % à 100 %, avec aperçu immédiat. Les icônes gardent leur opacité. La valeur est enregistrée à la fermeture des réglages.
- Les menus se ferment au clic extérieur, lors d’un changement de fenêtre ou avec Échap.
- Le dock grandit et rétrécit avec son contenu, bouton **+** compris. À la limite de l’écran, les icônes se réduisent proportionnellement pour rester toutes visibles, sans défilement.

Le dock n’apparaît pas devant une fenêtre au premier plan couvrant tout l’écran. Son apparition ne prend pas le focus clavier. Les réglages et sélecteurs de fichiers sont des fenêtres interactives normales.

## Données et limites

Les données sont enregistrées dans `%LocalAppData%\WinDock\settings.json`, indépendamment du dossier portable. Les copies de raccourcis sont dans le sous-dossier `Shortcuts`. Les anciennes entrées `.lnk` encore accessibles sont copiées au démarrage ; celles déjà supprimées restent réparables. Une configuration invalide est copiée dans un fichier `settings.json.invalid-*` avant le retour aux valeurs par défaut. La modification ou la suppression d’une entrée conserve l’ancienne version si la sauvegarde échoue. Les raccourcis d’origine ne sont jamais modifiés par WinDock.

Les applications du catalogue utilisent une copie du raccourci classique lorsqu’il est identifiable, avec ses arguments et son dossier de travail. Sinon, WinDock crée un raccourci vers leur cible Shell Windows, sans lancer un exécutable interne à WindowsApps. Ces cibles spéciales permettent de modifier le nom ou de remplacer la cible, mais pas les arguments. Une application absente d’un catalogue lu avec succès est signalée comme manquante ; une erreur de lecture du catalogue ne suffit pas à la déclarer supprimée.

Le cache est stocké séparément dans `%LocalAppData%\WinDock\CatalogCache\catalog.json`, avec ses icônes PNG intégrées. Une lecture Windows incomplète ou indisponible conserve le dernier catalogue complet. Un cache illisible est reconstruit ; il n’est jamais utilisé comme preuve de désinstallation. Le tout premier chargement nécessite toujours l’interrogation de Windows.

Ce prototype ne modifie ni le bureau ni la barre des tâches, ne se lance pas automatiquement avec Windows et n’exige pas de droits administrateur. Une seconde instance quitte immédiatement. Il ne gère pas les fenêtres ouvertes ni les icônes de notification. L’exécutable n’est pas signé.

## Développement

Prérequis : Windows x64 et SDK .NET 10. Aucun paquet applicatif tiers.

Depuis la racine du dépôt, pour lancer l’application depuis les sources :

```powershell
dotnet run --project src/WinDock -c Release
```

Pour compiler la solution et exécuter les tests :

```powershell
dotnet build WinDock.slnx -c Release -m:1
dotnet run --project tests/WinDock.Tests -c Release -p:BuildInParallel=false
```

Les tests sont un exécutable autonome sans dépendance de framework de test. Ils retournent un code non nul en cas d’échec. Ils couvrent la persistance, les configurations invalides, le réordonnancement, les formats acceptés, le placement sur les quatre bords, les coordonnées négatives, la mise à l’échelle, le débordement, l’initialisation WPF et le lancement effectif d’un raccourci avec arguments et répertoire de travail contenant des espaces et accents. Le test de raccourci lance brièvement un processus de test qui écrit uniquement dans son dossier temporaire.

Les tests WPF et Shell doivent s’exécuter dans une session Windows interactive. Les sorties `bin`, `obj`, `artifacts` et les préférences des IDE sont exclues du dépôt. Les données utilisateur restent dans `%LocalAppData%\WinDock`.

Pour générer les aperçus de l’interface réelle :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --preview artifacts/previews
```

Pour tester le catalogue réel, la persistance des raccourcis et l’identité des processus (ce test ouvre la Calculatrice) :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --catalog-smoke artifacts/previews
```

Pour produire le dossier portable (le premier lancement peut nécessiter le téléchargement des packs runtime Microsoft) :

```powershell
dotnet publish src/WinDock/WinDock.csproj -c Release -r win-x64 --self-contained true -o artifacts/WinDock-win-x64 -m:1 -p:DebugType=None -p:DebugSymbols=false
```

## Recette interactive

À vérifier sur le poste d’utilisation ; les tests de géométrie ne remplacent pas ces essais matériels :

- Tester les quatre bords avec la barre des tâches visible et masquée automatiquement.
- Déplacer le dock entre des écrans à 100 %, 150 % et 200 %, puis débrancher et rebrancher l’écran sélectionné.
- Saisir du texte dans une autre application, révéler le dock et vérifier que la saisie garde son focus.
- Vérifier le masquage en plein écran, la temporisation et le maintien pendant les menus, les sélecteurs et les glisser-déposer.
- Ajouter des applications et dossiers, changer l’ordre, relancer WinDock et vérifier leur conservation.
- Supprimer ou déplacer une cible hors du dock, puis vérifier le message au lancement.
- Lancer deux fois WinDock et vérifier qu’un seul dock demeure.

Le calcul de placement et le stockage sont dans `WinDock.Core`. L’interface et les appels Windows sont dans `WinDock`.
