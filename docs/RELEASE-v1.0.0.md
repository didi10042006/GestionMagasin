# Gestion-Magasin 1.0.0 — notes de publication

## Fichier à joindre à la GitHub Release

- `Gestion-Magasin-Setup.exe` — installateur Windows x64 autonome, généré par Inno Setup 7.1.0 (environ 36,4 Mo dans cette construction).

## Contenu

- Application Windows Forms x64 autonome ciblant .NET 10, sans runtime préalable requis sur le poste cible.
- Assistant d'installation en français, logo Gestion-Magasin, version 1.0.0, raccourci menu Démarrer et raccourci bureau facultatif.
- Lecture des conditions d'utilisation et acceptation obligatoire par case à cocher; le mode silencieux est refusé.
- Bases SQLite séparées pour le mode réel et le mode démonstration. La désinstallation et les mises à jour préservent les fichiers de données sous `%LOCALAPPDATA%\GestionMagasin`.
- Documents inclus : conditions d'utilisation, licence MIT et notices tierces.

## Vérifications locales

- Build Release x64 : réussi.
- Compilation du setup par Inno Setup 7.1.0 : réussie.
- Suite automatisée exécutée : 25 réussis, 0 échec.
- Recette fonctionnelle T01–T18 : consulter `RECETTE-FINALE-T01-T18.md`. Elle reste partielle pour les scénarios identifiés et ne doit pas être présentée comme entièrement acceptée.

Pour régénérer les fichiers : `scripts/Build-WindowsInstaller.ps1`. Le binaire n'est pas suivi par Git; joindre le setup généré localement comme asset de la release.
