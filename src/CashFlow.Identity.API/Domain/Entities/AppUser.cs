using CashFlow.Identity.API.Domain.Exceptions;

namespace CashFlow.Identity.API.Domain.Entities;

/// <summary>
/// Usuário da aplicação. Rich Domain Model (ADR-009): construtor privado, factory method,
/// invariantes encapsuladas. PasswordHash é opaco para o domínio — o algoritmo (Argon2id)
/// fica em <c>Application/Auth/Argon2idPasswordHasher.cs</c>.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    private AppUser() { }

    public static AppUser Create(string username, string passwordHash, string role)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("Username é obrigatório.");
        if (username.Length > 64)
            throw new DomainException("Username não pode exceder 64 caracteres.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash é obrigatório.");
        if (string.IsNullOrWhiteSpace(role))
            throw new DomainException("Role é obrigatório.");

        return new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = null,
            FailedLoginAttempts = 0,
            LockedUntil = null,
        };
    }

    /// <summary>Reconstitui entidade vinda do banco — sem validações de criação.</summary>
    public static AppUser Rehydrate(
        Guid id, string username, string passwordHash, string role,
        bool isActive, DateTimeOffset createdAt, DateTimeOffset? lastLoginAt,
        int failedLoginAttempts, DateTimeOffset? lockedUntil) => new()
    {
        Id = id,
        Username = username,
        PasswordHash = passwordHash,
        Role = role,
        IsActive = isActive,
        CreatedAt = createdAt,
        LastLoginAt = lastLoginAt,
        FailedLoginAttempts = failedLoginAttempts,
        LockedUntil = lockedUntil,
    };

    /// <summary>True se a conta está travada por lockout que ainda não expirou (ADR-023).</summary>
    public bool IsLockedOut => LockedUntil is { } until && until > DateTimeOffset.UtcNow;

    /// <summary>
    /// Registra tentativa falha de login. Se contador atingir <paramref name="maxAttempts"/>,
    /// trava a conta por <paramref name="lockoutDuration"/>.
    /// </summary>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts += 1;
        if (FailedLoginAttempts >= maxAttempts)
            LockedUntil = DateTimeOffset.UtcNow.Add(lockoutDuration);
    }

    /// <summary>Sucesso de login: reset do contador + atualização do timestamp.</summary>
    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        LastLoginAt = DateTimeOffset.UtcNow;
    }
}
