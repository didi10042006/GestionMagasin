namespace GestionMagasin.Services;

public enum UserRole
{
    Administrateur,
    Vendeur
}

public sealed record AuthenticatedUser(int Id, string Username, string FullName, UserRole Role);
