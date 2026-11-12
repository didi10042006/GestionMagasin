# Procès-verbal de recette — Gestion-Magasin

**Date :** 12 novembre 2026<br>
**Version visée :** 1.0.0<br>
**Plateforme de validation :** Windows x64, SDK/runtime .NET 10.0.15, SQLite<br>
**Installateur compilé :** `Gestion-Magasin-Setup.exe` (Inno Setup 7.1.0)

## Résultat de l'exécution automatisée

Commande exécutée : `dotnet test GestionMagasin\Tests\GestionMagasin.Tests.csproj -c Release -p:Platform=x64 --no-restore -p:NuGetAudit=false`, avec `GESTIONMAGASIN_INSTALLED_EXE` pointant vers le binaire autonome publié.

**Résultat : 25 tests passés, 0 échoué.** Cela atteste les assertions de la suite exécutée. Cela ne suffit pas à déclarer conformes les scénarios de recette dont le scénario précis n'est pas automatisé. Les scénarios incomplets sont identifiés ci-dessous.

| Cas | Résultat | Preuve / écart constaté |
|---|---|---|
| T01 | Réussi | Création produit, stock initial de 10 et mouvement initial unique vérifiés. |
| T02 | Réussi | Référence déjà utilisée refusée avec message clair. |
| T03 | Partiel | Le test `T03_PriceChangeRequiresSecondValidationWithoutPartialSale` vérifie actualisation du prix, total rafraîchi, nouvelle validation et absence d'écriture partielle. Il ne reproduit pas exactement l'exemple de 3 unités à 2 000 XAF. |
| T04 | Réussi | Panier multi-produit avec stock insuffisant : vente et mouvements annulés globalement, produit fautif nommé. Les quantités d'exemple diffèrent du cahier. |
| T05 | Partiel | Refus d'une sortie dépassant le stock couvert par `SaleOutputCannotOversellAndCancellationRestoresStockOnce`; le scénario précis « vendre les 7 restantes, stock à zéro puis nouvelle vente bloquée » n'est pas exécuté tel quel. |
| T06 | Réussi | Réapprovisionnement de 5 au seuil 5 et statut Stock faible vérifiés. |
| T07 | Réussi (règle métier) | Le test T04 vérifie dans un panier multi-produit qu'une ligne invalide par insuffisance de stock n'enregistre ni vente ni mouvement. |
| T08 | Partiel | Appels concurrents portant le même jeton ne créent qu'une vente; un test séparé vérifie le mouvement négatif atomique. Le double-clic réel sur le bouton WinForms n'a pas été automatisé. |
| T09 | Réussi | Annulation administrateur avec motif, vente originale conservée, compensation unique et seconde annulation refusée. |
| T10 | Partiel | La revalidation après changement de prix est testée (T03), mais l'immutabilité d'un ancien ticket après modification du nom et du prix d'un produit vendu n'est pas vérifiée. |
| T11 | Non démontré | Un test couvre les filtres de l'historique. Aucun test ne compare les totaux de rapports entre journées ni l'exclusion d'une vente annulée du jour d'origine. |
| T12 | Réussi | Paiement espèces insuffisant bloqué sans écriture (T12); paiement suffisant et monnaie exacte vérifiés dans le test de vente T08. |
| T13 | Partiel | L'exécutable autonome publié réouvre le schéma et les données commerciales survivent au redémarrage du processus. La réimpression du ticket sans nouvelle vente et l'installation effective via l'assistant n'ont pas été vérifiées dans cette recette. |
| T14 | Réussi | Les autorisations d'administration sont refusées au vendeur dans les services métier. |
| T15 | Réussi | Restauration, copie de sécurité, rejet d'une sauvegarde invalide et conservation de la base active vérifiés. |
| T16 | Non démontré | Les refus métier et le rollback sur stock insuffisant sont testés; une panne injectée de base de données au milieu de la validation n'a pas été simulée. |
| T17 | Partiel | Les calculs d'ajustement de dialogue à 100 %, 125 % et 150 % pour une aire 1366×768 sont testés. Les contrôles visuels complets des écrans et les objectifs de performance sur 5 000 produits / 50 000 ventes n'ont pas été mesurés. |
| T18 | Réussi | Export UTF-8 avec accents, séparateurs, protection contre formules CSV et restriction administrateur vérifiés. |

## Validation de compilation et du paquet

- `dotnet build GestionMagasin.slnx -c Release --no-restore -p:NuGetAudit=false` : réussi, 0 erreur. Le SDK a affiché l'avertissement `NU1900` parce que la source NuGet de vérification de vulnérabilités n'était pas joignable.
- Publication autonome `win-x64`, mono-fichier et autoportée : générée.
- Compilation Inno Setup 7.1.0 : réussie; l'exécutable du setup porte le logo et la version 1.0.0.
- T13 a été lancé contre le binaire **publié**, dans une base temporaire supprimée à la fin du test. Le programme a été relancé deux fois et les enregistrements sont restés présents.
- L'assistant a été ouvert, mais deux expirations de capture de l'automatisation Windows ont empêché d'observer et de terminer son parcours. L'installation et la désinstallation réelles ne sont donc pas certifiées ici.

## Conclusion

**Recette automatisée partiellement concluante; livraison publique non signable comme recette T01–T18 complète.** Les 25 tests automatisés exécutés ont réussi. Les écarts T03, T05, T08, T10, T11, T13, T16 et T17 demandent les scénarios complémentaires indiqués dans le tableau avant d'attester la réception complète du cahier des charges. Aucun parcours de recette manuel complet sur poste cible n'est attesté.
