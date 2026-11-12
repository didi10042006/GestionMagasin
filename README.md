# Gestion-Magasin

Application Windows de gestion locale de catalogue, stock et ventes pour un magasin. Les données sont stockées dans des bases SQLite locales; aucune passerelle de paiement ni aucun service cloud n'est requis.

## Installation Windows

1. Téléchargez `Gestion-Magasin-Setup.exe` depuis la section **Releases** du dépôt.
2. Lancez l'installateur x64. Lisez les conditions d'utilisation et cochez **J'accepte les conditions d'utilisation** pour activer « Suivant ». Refuser ou fermer l'assistant annule l'installation.
3. Choisissez le dossier de destination. L'entrée du menu Démarrer est créée; le raccourci bureau est facultatif.
4. Lancez **Gestion-Magasin** depuis le menu Démarrer.

Le paquet est autonome x64 et inclut le runtime .NET nécessaire. Visual Studio et une installation préalable du runtime .NET ne sont pas requis sur le PC cible. Windows x64 est requis.

## Première utilisation

Au démarrage, choisissez **Base réelle** ou **Base de démonstration**. Ces modes sont isolés dans des fichiers distincts.

- **Base réelle** : vide de produits fictifs. Lors de la première utilisation, créez un mot de passe pour le premier administrateur. Aucun compte ni mot de passe par défaut n'est fourni. Ce compte permet ensuite de créer les comptes administrateur et vendeur.
- **Base de démonstration** : contient huit produits de référence et des stocks illustrant les statuts Disponible, Stock faible et En rupture. Un caissier interne sans identifiant de connexion connu est créé avec un mot de passe aléatoire uniquement haché; cela permet d'essayer l'ajout au panier et les transactions de démonstration sans toucher aux données réelles.

Ne stockez pas les bases dans le dossier d'installation. L'application les place sous le profil Windows de l'utilisateur :

```text
%LOCALAPPDATA%\GestionMagasin\gestion-magasin.db
%LOCALAPPDATA%\GestionMagasin\Demo\gestion-magasin-demo.db
```

Les sauvegardes automatiques sont rangées dans le sous-dossier `Backups` de la base concernée; les sept plus récentes sont conservées. L'administrateur peut aussi créer une sauvegarde manuelle et restaurer une copie après contrôle d'intégrité. La désinstallation conserve les bases et sauvegardes. Une mise à jour remplace les fichiers du programme, sans supprimer les données sous `%LOCALAPPDATA%`.

## Fonctions

- Catalogue de produits et catégories, recherche, filtres et archivage.
- Mouvements signés de stock, réapprovisionnement, correction d'inventaire et interdiction du stock négatif.
- Panier et validation transactionnelle des ventes, paiements espèces, Mobile Money et carte (description saisie localement), historique, ticket réimprimable et annulation par un administrateur.
- Tableau de bord selon le rôle, rapports par période, exports CSV protégés contre les formules malveillantes.
- Administration des comptes, paramètres magasin, sauvegardes et journal d'audit.

La devise gérée est **FCFA XAF**. Les mots de passe sont hachés avec PBKDF2; les mots de passe en clair ne sont pas conservés dans le journal d'audit.

## Construction depuis les sources

Prérequis développeur : Windows x64, SDK .NET 10, Git et Inno Setup 7 pour produire le setup. Les versions NuGet sont épinglées dans les fichiers projet.

Compiler la solution :

```powershell
dotnet restore GestionMagasin.slnx
dotnet build GestionMagasin.slnx -c Release
```

Exécuter les tests automatisés :

```powershell
dotnet test GestionMagasin\Tests\GestionMagasin.Tests.csproj -c Release -p:Platform=x64
```

Publier le binaire x64 autonome et générer `Gestion-Magasin-Setup.exe` avec Inno Setup 7 :

```powershell
.\scripts\Build-WindowsInstaller.ps1
```

Les sorties générées apparaissent sous `artifacts\publish\win-x64` et `artifacts\installer`; elles sont ignorées par Git. Le script produit un exécutable mono-fichier autonome et l'installateur avec ses documents légaux. L'assistant exige une acceptation explicite et refuse l'installation silencieuse.

Le test d'intégration T13 lance l'exécutable publié/ installé sur une base temporaire, redémarre le processus et vérifie la persistance. Pour le lancer explicitement :

```powershell
.\scripts\Test-InstalledPersistence.ps1 -InstalledExecutable "C:\chemin\vers\GestionMagasin.exe"
```

## Structure du dépôt

```text
GestionMagasin/              Application WinForms, données, modèles, services et tests
Gestion-Magasin.iss          Assistant et définition de l'installateur Inno Setup
Installer/                   Ressources graphiques de l'assistant d'installation
scripts/                     Publication x64 et test d'intégration T13
CONDITIONS-UTILISATION.txt   Conditions présentées pendant l'installation
LICENSE                      Licence MIT
THIRD-PARTY-NOTICES.md       Dépendances, versions et licences
docs/                        Procès-verbal de recette et notes de version
```

## Sécurité et données

Les bases et leurs sauvegardes contiennent les données commerciales et ne sont pas chiffrées par l'application. Protégez l'accès au compte Windows et conservez des sauvegardes hors du poste. Ne publiez jamais une base réelle, un fichier `.env`, un mot de passe ou une clé. Le `.gitignore` exclut les bases SQLite, sauvegardes, artefacts de build et fichiers de secrets usuels.

Consultez [CONDITIONS-UTILISATION.txt](CONDITIONS-UTILISATION.txt), [LICENSE](LICENSE) et [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
