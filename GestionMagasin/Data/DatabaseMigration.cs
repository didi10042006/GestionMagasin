namespace GestionMagasin.Data;

internal sealed record DatabaseMigration(
    int Version,
    string Description,
    string Sql,
    bool RequiresForeignKeysDisabled = false);
