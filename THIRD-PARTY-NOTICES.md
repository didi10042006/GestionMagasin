# Notices des composants tiers

Versions relevées dans les fichiers `project.assets.json` restaurés de la
solution Gestion-Magasin. Les paquets sous « Application » sont des dépendances
de l'application ; ceux sous « Développement et tests » servent à construire ou
tester le projet et ne sont pas des dépendances de l'application publiée.
Les dépendances transitives sont également indiquées.

## Application

| Composant | Version | Licence |
| --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.15 | MIT |
| Microsoft.Data.Sqlite.Core | 10.0.15 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.13 | Apache-2.0 |
| SQLitePCLRaw.core | 2.1.13 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.13 | Apache-2.0 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Apache-2.0 pour l'emballage SQLitePCLRaw ; moteur SQLite natif placé dans le domaine public |

SQLitePCLRaw est un projet sous Apache License 2.0. Le code du moteur SQLite
fourni par `e_sqlite3` est consacré au domaine public par ses auteurs. Les
informations du projet SQLitePCLRaw sont disponibles sur [son dépôt officiel](https://github.com/ericsink/SQLitePCL.raw)
et celles du moteur sur la [page officielle de copyright de SQLite](https://www.sqlite.org/copyright.html).

## Développement et tests

| Composant | Version | Licence / avis |
| --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.8.0 | MIT |
| Microsoft.CodeCoverage | 17.8.0 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.8.0 | MIT |
| Microsoft.TestPlatform.TestHost | 17.8.0 | MIT |
| Microsoft.NETCore.Platforms | 1.1.0 | Microsoft .NET Library License Terms ; les composants .NET Core listés dans l'avis du paquet sont sous MIT |
| NETStandard.Library | 1.6.1 | Microsoft .NET Library License Terms ; les composants .NET Core listés dans l'avis du paquet sont sous MIT |
| Newtonsoft.Json | 13.0.1 | MIT |
| NuGet.Frameworks | 6.5.0 | Apache-2.0 |
| xunit | 2.5.3 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | Apache-2.0 |
| xunit.analyzers | 1.4.0 | Apache-2.0 |
| xunit.assert | 2.5.3 | Apache-2.0 |
| xunit.core | 2.5.3 | Apache-2.0 |
| xunit.extensibility.core | 2.5.3 | Apache-2.0 |
| xunit.extensibility.execution | 2.5.3 | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.3 | Apache-2.0 |

Les deux paquets Microsoft .NET Library historiques incluent un fichier
`dotnet_library_license.txt` et un `ThirdPartyNotices.txt` dans leur contenu
NuGet. Consultez ces fichiers avec le paquet exact lors de toute redistribution
des outils de développement correspondants.

## Références de licence

- [Licence MIT — Open Source Initiative](https://opensource.org/license/mit)
- [Microsoft.Data.Sqlite 10.0.15 — NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15)
- [SQLitePCLRaw — dépôt et licence](https://github.com/ericsink/SQLitePCL.raw)
- [SQLite — avis de domaine public](https://www.sqlite.org/copyright.html)
- [xUnit — licence Apache 2.0 et informations de paquet](https://www.nuget.org/packages/xunit/2.5.3)
- [NuGet.Frameworks — informations de paquet](https://www.nuget.org/packages/NuGet.Frameworks/6.5.0)
- [Newtonsoft.Json — informations de paquet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.1)
- [Microsoft.NET.Test.Sdk — informations de paquet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.8.0)

Cette notice est un inventaire des dépendances restaurées du dépôt et ne
remplace pas les textes de licence distribués avec les paquets tiers.
