namespace GestionMagasin.Data;

internal static class DatabaseMigrations
{
    public static IReadOnlyList<DatabaseMigration> All { get; } =
    [
        new DatabaseMigration(1, "Schéma initial de GestionMagasin", InitialSchema),
        new DatabaseMigration(2, "Rôle Vendeur et compatibilité des comptes existants", VendorRoleSchema, true),
        new DatabaseMigration(3, "Interdire la vente des produits archivés ou sans prix positif", SellableProductTriggers),
        new DatabaseMigration(4, "Journal signé des mouvements et solde de stock non négatif", StockLedgerSchema)
    ];

    private const string InitialSchema = """
        CREATE TABLE IF NOT EXISTS Utilisateur (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            NomUtilisateur TEXT NOT NULL COLLATE NOCASE UNIQUE,
            NomComplet TEXT NOT NULL,
            MotDePasseHash TEXT NOT NULL,
            Role TEXT NOT NULL CHECK (Role IN ('Administrateur', 'Caissier')),
            Actif INTEGER NOT NULL DEFAULT 1 CHECK (Actif IN (0, 1)),
            CreeLeUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Categorie (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Nom TEXT NOT NULL COLLATE NOCASE UNIQUE,
            Description TEXT,
            Actif INTEGER NOT NULL DEFAULT 1 CHECK (Actif IN (0, 1))
        );

        CREATE TABLE IF NOT EXISTS Produit (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CategorieId INTEGER NOT NULL REFERENCES Categorie(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            Code TEXT NOT NULL COLLATE NOCASE UNIQUE,
            Nom TEXT NOT NULL,
            Description TEXT,
            PrixAchatXaf INTEGER NOT NULL CHECK (PrixAchatXaf >= 0),
            PrixVenteXaf INTEGER NOT NULL CHECK (PrixVenteXaf >= 0),
            QuantiteStock INTEGER NOT NULL DEFAULT 0 CHECK (QuantiteStock >= 0),
            SeuilAlerte INTEGER NOT NULL DEFAULT 0 CHECK (SeuilAlerte >= 0),
            Actif INTEGER NOT NULL DEFAULT 1 CHECK (Actif IN (0, 1)),
            CreeLeUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Vente (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Numero TEXT NOT NULL UNIQUE,
            UtilisateurId INTEGER NOT NULL REFERENCES Utilisateur(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            DateVenteUtc TEXT NOT NULL,
            MontantTotalXaf INTEGER NOT NULL CHECK (MontantTotalXaf >= 0),
            MontantPayeXaf INTEGER NOT NULL CHECK (MontantPayeXaf >= 0),
            MonnaieRendueXaf INTEGER NOT NULL CHECK (MonnaieRendueXaf >= 0),
            ModePaiement TEXT NOT NULL,
            Statut TEXT NOT NULL CHECK (Statut IN ('Validee', 'Annulee'))
        );

        CREATE TABLE IF NOT EXISTS LigneVente (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            VenteId INTEGER NOT NULL REFERENCES Vente(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            ProduitId INTEGER NOT NULL REFERENCES Produit(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            Quantite INTEGER NOT NULL CHECK (Quantite > 0),
            PrixUnitaireXaf INTEGER NOT NULL CHECK (PrixUnitaireXaf >= 0),
            SousTotalXaf INTEGER NOT NULL CHECK (SousTotalXaf >= 0)
        );

        CREATE TABLE IF NOT EXISTS AnnulationVente (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            VenteId INTEGER NOT NULL UNIQUE REFERENCES Vente(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            UtilisateurId INTEGER NOT NULL REFERENCES Utilisateur(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            Motif TEXT NOT NULL,
            AnnuleeLeUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS MouvementStock (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProduitId INTEGER NOT NULL REFERENCES Produit(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            UtilisateurId INTEGER REFERENCES Utilisateur(Id) ON UPDATE CASCADE ON DELETE SET NULL,
            Quantite INTEGER NOT NULL CHECK (Quantite <> 0),
            TypeMouvement TEXT NOT NULL CHECK (TypeMouvement IN ('Entree', 'Sortie', 'AnnulationVente', 'Ajustement')),
            Reference TEXT,
            Motif TEXT,
            CreeLeUtc TEXT NOT NULL,
            CHECK (TypeMouvement <> 'StockInitial' OR Quantite > 0),
            CHECK (TypeMouvement <> 'Reapprovisionnement' OR
                   (Quantite > 0 AND Motif IS NOT NULL AND length(trim(Motif)) > 0)),
            CHECK (TypeMouvement <> 'SortieVente' OR Quantite < 0),
            CHECK (TypeMouvement <> 'AnnulationVente' OR Quantite > 0),
            CHECK (TypeMouvement <> 'CorrectionInventaire' OR
                   (Motif IS NOT NULL AND length(trim(Motif)) > 0))
        );

        CREATE TABLE IF NOT EXISTS JournalAudit (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UtilisateurId INTEGER REFERENCES Utilisateur(Id) ON UPDATE CASCADE ON DELETE SET NULL,
            Action TEXT NOT NULL,
            Entite TEXT NOT NULL,
            EntiteId INTEGER,
            Details TEXT,
            CreeLeUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Parametre (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Cle TEXT NOT NULL COLLATE NOCASE UNIQUE,
            Valeur TEXT NOT NULL,
            ModifieLeUtc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS VersionSchema (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Version INTEGER NOT NULL UNIQUE,
            Description TEXT NOT NULL,
            AppliqueeLeUtc TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_Produit_CategorieId ON Produit(CategorieId);
        CREATE INDEX IF NOT EXISTS IX_Vente_DateVenteUtc ON Vente(DateVenteUtc);
        CREATE INDEX IF NOT EXISTS IX_Vente_UtilisateurId ON Vente(UtilisateurId);
        CREATE INDEX IF NOT EXISTS IX_LigneVente_VenteId ON LigneVente(VenteId);
        CREATE INDEX IF NOT EXISTS IX_LigneVente_ProduitId ON LigneVente(ProduitId);
        CREATE INDEX IF NOT EXISTS IX_MouvementStock_ProduitId_CreeLeUtc ON MouvementStock(ProduitId, CreeLeUtc);
        CREATE INDEX IF NOT EXISTS IX_JournalAudit_CreeLeUtc ON JournalAudit(CreeLeUtc);
        """;

    private const string VendorRoleSchema = """
        CREATE TABLE Utilisateur_nouveau (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            NomUtilisateur TEXT NOT NULL COLLATE NOCASE UNIQUE,
            NomComplet TEXT NOT NULL,
            MotDePasseHash TEXT NOT NULL,
            Role TEXT NOT NULL CHECK (Role IN ('Administrateur', 'Vendeur')),
            Actif INTEGER NOT NULL DEFAULT 1 CHECK (Actif IN (0, 1)),
            CreeLeUtc TEXT NOT NULL
        );

        INSERT INTO Utilisateur_nouveau (Id, NomUtilisateur, NomComplet, MotDePasseHash, Role, Actif, CreeLeUtc)
        SELECT Id, NomUtilisateur, NomComplet, MotDePasseHash,
               CASE Role WHEN 'Caissier' THEN 'Vendeur' ELSE Role END,
               Actif, CreeLeUtc
        FROM Utilisateur;

        DROP TABLE Utilisateur;
        ALTER TABLE Utilisateur_nouveau RENAME TO Utilisateur;
        """;

    private const string SellableProductTriggers = """
        CREATE TRIGGER IF NOT EXISTS TR_LigneVente_ProduitActif_Insert
        BEFORE INSERT ON LigneVente
        FOR EACH ROW
        WHEN NOT EXISTS (
            SELECT 1 FROM Produit
            WHERE Id = NEW.ProduitId AND Actif = 1 AND PrixVenteXaf > 0
        )
        BEGIN
            SELECT RAISE(ABORT, 'Le produit est archivé ou son prix de vente est invalide.');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_LigneVente_ProduitActif_Update
        BEFORE UPDATE OF ProduitId ON LigneVente
        FOR EACH ROW
        WHEN NOT EXISTS (
            SELECT 1 FROM Produit
            WHERE Id = NEW.ProduitId AND Actif = 1 AND PrixVenteXaf > 0
        )
        BEGIN
            SELECT RAISE(ABORT, 'Le produit est archivé ou son prix de vente est invalide.');
        END;
        """;

    private const string StockLedgerSchema = """
        CREATE TABLE MouvementStock_nouveau (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProduitId INTEGER NOT NULL REFERENCES Produit(Id) ON UPDATE CASCADE ON DELETE RESTRICT,
            UtilisateurId INTEGER REFERENCES Utilisateur(Id) ON UPDATE CASCADE ON DELETE SET NULL,
            Quantite INTEGER NOT NULL CHECK (Quantite <> 0),
            TypeMouvement TEXT NOT NULL CHECK (TypeMouvement IN (
                'StockInitial', 'Reapprovisionnement', 'SortieVente', 'AnnulationVente',
                'CorrectionInventaire', 'Entree', 'Sortie', 'Ajustement')),
            Reference TEXT,
            Motif TEXT,
            CreeLeUtc TEXT NOT NULL
        );

        INSERT INTO MouvementStock_nouveau
            (Id, ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
        SELECT Id, ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc
        FROM MouvementStock;

        DROP TABLE MouvementStock;
        ALTER TABLE MouvementStock_nouveau RENAME TO MouvementStock;

        CREATE INDEX IX_MouvementStock_ProduitId_CreeLeUtc ON MouvementStock(ProduitId, CreeLeUtc);
        CREATE UNIQUE INDEX UX_MouvementStock_StockInitial
            ON MouvementStock(ProduitId) WHERE TypeMouvement = 'StockInitial';
        CREATE UNIQUE INDEX UX_MouvementStock_SortieVente
            ON MouvementStock(ProduitId, Reference) WHERE TypeMouvement = 'SortieVente' AND Reference IS NOT NULL;
        CREATE UNIQUE INDEX UX_MouvementStock_AnnulationVente
            ON MouvementStock(ProduitId, Reference) WHERE TypeMouvement = 'AnnulationVente' AND Reference IS NOT NULL;
        CREATE UNIQUE INDEX UX_LigneVente_VenteId_ProduitId ON LigneVente(VenteId, ProduitId);

        INSERT INTO MouvementStock (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
        SELECT p.Id, NULL, p.QuantiteStock, 'StockInitial', 'INITIAL', 'Reprise du stock historique', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
        FROM Produit p
        WHERE p.QuantiteStock > 0
          AND NOT EXISTS (SELECT 1 FROM MouvementStock m WHERE m.ProduitId = p.Id);

        CREATE TRIGGER TR_MouvementStock_NonNegatif_Insert
        BEFORE INSERT ON MouvementStock
        FOR EACH ROW
        WHEN COALESCE((SELECT SUM(Quantite) FROM MouvementStock WHERE ProduitId = NEW.ProduitId), 0) + NEW.Quantite < 0
        BEGIN
            SELECT RAISE(ABORT, 'Stock insuffisant : le solde ne peut pas être négatif.');
        END;

        CREATE TRIGGER TR_MouvementStock_NonNegatif_Update
        BEFORE UPDATE OF ProduitId, Quantite ON MouvementStock
        FOR EACH ROW
        WHEN COALESCE((SELECT SUM(Quantite) FROM MouvementStock WHERE ProduitId = NEW.ProduitId AND Id <> OLD.Id), 0) + NEW.Quantite < 0
        BEGIN
            SELECT RAISE(ABORT, 'Stock insuffisant : le solde ne peut pas être négatif.');
        END;

        CREATE TRIGGER TR_MouvementStock_ProduitActif
        BEFORE INSERT ON MouvementStock
        FOR EACH ROW
        WHEN NEW.TypeMouvement IN ('StockInitial', 'Reapprovisionnement', 'SortieVente', 'CorrectionInventaire')
         AND NOT EXISTS (SELECT 1 FROM Produit WHERE Id = NEW.ProduitId AND Actif = 1)
        BEGIN
            SELECT RAISE(ABORT, 'Un produit archivé ne peut pas recevoir ce mouvement de stock.');
        END;

        CREATE TRIGGER TR_LigneVente_CreeSortieStock
        AFTER INSERT ON LigneVente
        FOR EACH ROW
        WHEN (SELECT Statut FROM Vente WHERE Id = NEW.VenteId) = 'Validee'
        BEGIN
            INSERT INTO MouvementStock (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
            SELECT NEW.ProduitId, v.UtilisateurId, -NEW.Quantite, 'SortieVente', CAST(NEW.VenteId AS TEXT),
                   'Vente validée', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            FROM Vente v WHERE v.Id = NEW.VenteId;
        END;

        CREATE TRIGGER TR_Vente_ValidationCreeSorties
        AFTER UPDATE OF Statut ON Vente
        FOR EACH ROW
        WHEN NEW.Statut = 'Validee' AND OLD.Statut <> 'Validee'
        BEGIN
            INSERT OR IGNORE INTO MouvementStock
                (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
            SELECT l.ProduitId, NEW.UtilisateurId, -l.Quantite, 'SortieVente', CAST(NEW.Id AS TEXT),
                   'Vente validée', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            FROM LigneVente l
            WHERE l.VenteId = NEW.Id;
        END;

        CREATE TRIGGER TR_AnnulationVente_CreeCompensations
        AFTER INSERT ON AnnulationVente
        FOR EACH ROW
        BEGIN
            INSERT OR IGNORE INTO MouvementStock
                (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
            SELECT l.ProduitId, NEW.UtilisateurId, l.Quantite, 'AnnulationVente', CAST(NEW.VenteId AS TEXT),
                   'Compensation de vente annulée', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            FROM LigneVente l
            WHERE l.VenteId = NEW.VenteId;
        END;
        """;
}
