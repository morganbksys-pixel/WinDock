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

Les tests WPF et Shell doivent s’exécuter dans une session Windows interactive. Les sorties `bin`, `obj`, `artifacts` et les préférences des IDE sont exclues du dépôt. Les données utilisateur restent dans `%LocalAppData%\WinDock`.

Le workflow GitHub Actions `.NET` utilise un runner Windows avec le SDK .NET 10. Il compile en Release puis lance ce même harnais avec `dotnet run --project tests/WinDock.Tests -c Release --no-build -- --preview artifacts/previews`, sans passer par `dotnet test`. Les aperçus produits sont disponibles dans l’artefact `windock-previews` pendant sept jours. Cette vérification ne remplace pas la recette matérielle multi-écrans et DPI mixtes.

Pour générer les aperçus de l’interface réelle :

```powershell
dotnet run --project tests/WinDock.Tests -c Release --no-build -- --preview artifacts/previews
```

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

- Tester les quatre bords avec la barre des tâches visible et masquée automatiquement.
- Déplacer le dock entre des écrans à 100 %, 150 % et 200 %, puis débrancher et rebrancher l’écran sélectionné.
- Saisir du texte dans une autre application, révéler le dock et vérifier que la saisie garde son focus.
- Vérifier le masquage en plein écran, la temporisation et le maintien pendant les menus, les sélecteurs et les glisser-déposer.
- Ajouter des applications et dossiers, changer l’ordre, relancer WinDock et vérifier leur conservation.
- Supprimer ou déplacer une cible hors du dock, puis vérifier le message au lancement.
- Lancer deux fois WinDock et vérifier qu’un seul dock demeure.
- Vérifier le mode fixe, le masquage en plein écran et le retour automatique, avec les écrans à DPI mixtes.
- Vérifier le positionnement des libellés aux quatre bords, leur fermeture pendant les menus et les déplacements, et le défilement des réglages à fort DPI.
- Pour tester la corbeille, utiliser uniquement un fichier jetable créé pour cet essai : contrôler les états vide/plein et l’annulation de la confirmation Windows. Ne confirmer le vidage que si tout le contenu de la corbeille peut être supprimé. Les tests automatisés ne vident jamais la corbeille réelle.

Le calcul de placement et le stockage sont dans `WinDock.Core`. L’interface et les appels Windows sont dans `WinDock`.
