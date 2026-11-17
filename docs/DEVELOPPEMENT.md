# Développer et publier WinDock

[← Retour à l’accueil](../README.md) · [Guide d’utilisation](UTILISATION.md)

WinDock est une application native C# / WPF (.NET 10). Le dépôt contient les sources, pas l’exécutable ni le runtime. Les livrables sont disponibles sur la [page de téléchargement](https://github.com/morganbksys-pixel/WinDock/releases/latest).

- [Prérequis, compilation et tests](#prérequis-compilation-et-tests)
- [Produire une livraison](#produire-une-livraison)
- [Recette interactive](#recette-interactive)

## Prérequis, compilation et tests

Exécutez toutes les commandes depuis la racine du dépôt, même si ce guide se trouve dans `docs/`.

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

Ils vérifient aussi les transitions du mode fixe et du plein écran, la superposition des menus, la restauration des réglages au premier plan, leur défilement, les interrupteurs persistés et le placement des libellés. Les états et actions de la corbeille sont testés avec un service simulé ; le démarrage automatique utilise un dossier temporaire. Aucun test ne vide la corbeille réelle ni ne modifie le démarrage automatique du compte. Le mode `--preview` produit les aperçus du dock, des réglages et d’un libellé ; il lit également l’état et l’icône système de la corbeille réelle sans en modifier le contenu.

Le harnais couvre également l’activation des fenêtres ouvertes, les groupes à deux membres minimum, leur dissolution et leurs sauvegardes atomiques, le renommage sur place et les transferts entre surfaces. Le dock de départ utilise des environnements simulés pour ses associations HTTPS/mailto et ses dossiers redirigés ; la confirmation de restauration est testée avec une préparation bloquée, des annulations, des résultats tardifs et une nouvelle tentative après erreur.

Les aperçus `default-dock.png` et `group-native.png` utilisent les applications détectées et l’icône système de la corbeille vide, dans une configuration temporaire, sans lancer leurs cibles ni interroger le contenu de la corbeille. `default-restore-loading.png`, `default-restore-error.png` et `default-restore-confirmation.png` illustrent les états de la restauration. L’ouverture mesurée par le harnais est une mesure instrumentée ; elle ne remplace pas un essai manuel sur le poste cible.

Les tests WPF et Shell doivent s’exécuter dans une session Windows interactive. Les sorties `bin`, `obj`, `artifacts` et les préférences des IDE sont exclues du dépôt. Les données utilisateur restent dans `%LocalAppData%\WinDock`.

Le workflow GitHub Actions `.NET` utilise un runner Windows avec le SDK .NET 10. Il compile en Release puis lance ce même harnais avec `dotnet run --project tests/WinDock.Tests -c Release --no-build -- --preview artifacts/previews`, sans passer par `dotnet test`. Les aperçus produits sont disponibles dans l’artefact `windock-previews` pendant sept jours. Cette vérification ne remplace pas la recette matérielle multi-écrans et DPI mixtes.

Pour générer les aperçus de l’interface réelle :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --preview artifacts/previews
```

Le harnais complet ouvre des fenêtres, menus et dialogues et vérifie leur activation. Exécutez-le lorsque le bureau est disponible. Pour travailler en parallèle sur le poste, utilisez les contrôles du mode Coin **hors écran** : aucune fenêtre n’est affichée ou activée, aucun handle de fenêtre du dock n’est créé.

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --background --preview artifacts/previews-background
```

Ce mode couvre la compatibilité des réglages, les huit combinaisons coin/orientation, les zones physiques d’activation et le passage depuis le coin, les coordonnées négatives, les échelles de 100 à 200 %, le délai de survol, le rendu et le réordonnancement des entrées avec persistance. Il vérifie aussi l’activation au coin exact : 2 × 2 pixels physiques, exclusion des pixels voisins, annulation du délai et persistance du choix. Les réglages et leurs libellés sont rendus hors écran, ainsi que les transitions interrompues. Les fichiers `corner-settings-*.png` et `corner-*.png` sont produits uniquement à partir de contrôles mesurés hors écran.

Les contrôles de menus hors écran simulent le positionnement natif pour vérifier la conservation de la superposition et du focus, puis injectent les changements de fenêtre, Échap et clics extérieurs, sans hook global. Le test interactif de superposition appelle trois fois le même repositionnement que le minuteur et distingue explicitement un menu fermé d’un menu ouvert passé derrière le dock. Une attente sur le bureau réel confondait auparavant ces cas, puisque l’utilisation d’une autre application ferme normalement le menu. Ces contrôles simulés ne remplacent pas la validation de l’empilement réel des fenêtres et du focus sur un bureau disponible, ni les essais matériels à DPI mixtes.

Pour vérifier la superposition **réelle** pendant que le bureau est utilisé :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --isolated-desktop artifacts/isolated-desktop-previews
```

Le lanceur crée un bureau Win32 distinct et démarre un processus sans console avec ce bureau dans `STARTUPINFO.lpDesktop`. Il n’appelle jamais `SwitchDesktop`. Le processus vérifie son bureau et celui de ses fenêtres avant et pendant les essais ; toute absence d’isolation interrompt les tests, sans repli sur le bureau utilisateur. Le processus dispose de deux minutes au maximum et les handles sont libérés à sa sortie. `isolated-tests.log` contient les résultats ; `settings-Emplacement.png`, `settings-Apparence.png` et `settings-Général.png` montrent les véritables contrôles rendus sur ce bureau.

Cette suite vérifie les HWND et leur ordre natif pour quatre bords et huit combinaisons coin/orientation, avec deux repositionnements espacés d’une seconde par cas, les groupes et les libellés. Elle valide aussi les trois onglets des réglages, l’application et la persistance des options, et la disponibilité du bouton Terminer dans une fenêtre de 300 unités de hauteur. Les entrées physiques et le bureau d’entrée de l’utilisateur ne sont pas pilotés. Le focus réel sur le bureau actif et les configurations matérielles multi-écrans à DPI mixtes restent des essais manuels distincts.

Ce mode vérifie aussi la résolution de 256 × 256 pixels, le gel et le cache des deux icônes système de la corbeille. Il produit les comparaisons `recycle-empty-100.png`, `recycle-full-100.png` et leurs variantes `150` et `200` : l’icône standard est à gauche, la haute définition à droite, à une taille d’affichage de 48 pixels WPF. Les rendus à 100 %, 150 % et 200 % ne remplacent pas les essais matériels à DPI mixtes. Ces comparaisons chargent les icônes vide et pleine sans modifier le contenu de la corbeille.

Pour tester le catalogue réel, la persistance des raccourcis et l’identité des processus (ce test ouvre la Calculatrice) :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --catalog-smoke artifacts/previews
```

## Produire une livraison

Après compilation, tests et revue des aperçus, publiez dans des dossiers neufs. La version est lue depuis le projet ; gardez le bandeau de `QUICKSTART.txt` aligné avant de publier. La première publication peut nécessiter le téléchargement des packs runtime Microsoft.

Pour produire le dossier portable et archiver son contenu complet :

```powershell
$releaseVersion = ([xml](Get-Content src/WinDock/WinDock.csproj -Raw)).Project.PropertyGroup.Version
$portableDirectory = "artifacts/WinDock-$releaseVersion-win-x64"
$portableArchive = "$portableDirectory.zip"
if ((Test-Path $portableDirectory) -or (Test-Path $portableArchive)) { throw "Cette livraison existe déjà. Utilisez une nouvelle version ou un dossier de sortie neuf." }
dotnet publish src/WinDock/WinDock.csproj -c Release -r win-x64 --self-contained true -o $portableDirectory -m:1 -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication portable." }
Compress-Archive -Path "$portableDirectory/*" -DestinationPath $portableArchive
```

Pour produire également l’EXE autonome dans un dossier distinct :

```powershell
$releaseVersion = ([xml](Get-Content src/WinDock/WinDock.csproj -Raw)).Project.PropertyGroup.Version
$singleFileDirectory = "artifacts/WinDock-$releaseVersion-win-x64-single-file"
if (Test-Path $singleFileDirectory) { throw "Utilisez un dossier de sortie neuf." }
dotnet publish src/WinDock/WinDock.csproj -c Release -r win-x64 --self-contained true -o $singleFileDirectory -m:1 -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de l’EXE autonome." }
```

Distribuez le ZIP complet, ou le `WinDock.exe` du dossier `single-file`. Les instructions sont dans le README et le guide d’utilisation ; aucun fichier `LIRE-MOI.txt` n’est publié. Ne distribuez pas seul l’exécutable du dossier portable classique. Les fichiers générés restent dans `artifacts/`, hors Git. Indiquez dans les notes de livraison les vérifications réalisées, les essais matériels encore à effectuer et l’absence de signature de l’exécutable.

Après validation d’une nouvelle livraison, vous pouvez supprimer les anciennes builds, leurs ZIP et les anciens dossiers d’aperçus dans `artifacts/`. Conservez les deux formats de la livraison courante et `artifacts/previews`. Les dossiers `dotnet-home` et `nuget` sont des caches de développement ; les conserver évite de retélécharger les packs lors des prochaines publications.

## Recette interactive

À vérifier sur le poste d’utilisation ; les tests de géométrie ne remplacent pas ces essais matériels :

- Tester les quatre bords et les huit combinaisons coin/orientation, avec la barre des tâches visible et masquée automatiquement, placée sur chacun des quatre côtés. Déplacer la barre pendant que WinDock fonctionne et vérifier le nouveau placement ainsi que le passage depuis le repère physique.
- Déplacer le dock entre des écrans à 100 %, 150 % et 200 %, puis débrancher et rebrancher l’écran sélectionné.
- Saisir du texte dans une autre application, révéler le dock et vérifier que la saisie garde son focus.
- Vérifier le masquage en plein écran, la temporisation et le maintien pendant les menus, les sélecteurs et les glisser-déposer.
- Ajouter des applications et dossiers, changer l’ordre, relancer WinDock et vérifier leur conservation.
- En mode Coin, inverser l’ordre depuis les réglages : vérifier les huit placements, la corbeille à l’autre extrémité, les groupes inchangés, le glisser-déposer et le retour Bord → Coin. Vérifier la persistance et la navigation au clavier. Vérifier les menus propres à chaque élément et l’accès aux commandes générales sur le fond du dock ; masquer la corbeille avec les réglages déjà ouverts doit synchroniser son interrupteur.
- Supprimer ou déplacer une cible hors du dock, puis vérifier le message au lancement.
- Lancer deux fois WinDock et vérifier qu’un seul dock demeure.
- Vérifier le mode fixe, le masquage en plein écran et le retour automatique, avec les écrans à DPI mixtes.
- Vérifier le positionnement des libellés aux quatre bords, leur fermeture pendant les menus et les déplacements, et le défilement des réglages à fort DPI.
- Pour tester la corbeille, utiliser uniquement un fichier jetable créé pour cet essai : contrôler les états vide/plein et l’annulation de la confirmation Windows. Ne confirmer le vidage que si tout le contenu de la corbeille peut être supprimé. Les tests automatisés ne vident jamais la corbeille réelle.

Le calcul de placement et le stockage sont dans `WinDock.Core`. L’interface et les appels Windows sont dans `WinDock`.

## Identité des applications et redémarrage de l’Explorateur

`ApplicationIdentityResolver` partage une résolution entre les indicateurs et les actions de fenêtres. Les métadonnées AppsFolder sont lues sur un STA et mises en cache pendant 30 secondes, y compris une résolution indisponible. Les liens classiques sont invalidés selon leur date de modification. Les applications empaquetées ne doivent jamais être associées par le seul chemin d’un hôte partagé. Le point vert correspond à un processus actif ; celui de l’Explorateur Windows est volontairement absent.

Pour diagnostiquer une entrée existante sans modifier ses réglages, lancer ni fermer une application :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --running-entry-smoke "$env:LOCALAPPDATA/WinDock/settings.json" "<GUID de l’entrée>"
```

Ce diagnostic lit directement le JSON et n’utilise pas le chargement avec récupération de SettingsStore. Il affiche le chemin résolu, l’indicateur et le nombre de fenêtres reconnues. Aucun argument de lancement ni titre de fenêtre n’est journalisé.

Le redémarrage de l’Explorateur passe par `IExplorerRestarter` ; les tests doivent toujours injecter un faux service ou de fausses opérations natives. Ne jamais déclencher un redémarrage du véritable bureau pendant les tests ou les aperçus. La cible native est obtenue par GetShellWindow et revalidée avec son processus, sa session, son chemin et sa date de démarrage ; aucun arrêt par nom global ni arrêt récursif des processus enfants. La récupération du bureau se poursuit après l’arrêt même si WinDock se ferme. Le redémarrage réel, le retour de la barre des tâches et le focus sont à valider manuellement dans une session de test dédiée, après enregistrement du travail.
