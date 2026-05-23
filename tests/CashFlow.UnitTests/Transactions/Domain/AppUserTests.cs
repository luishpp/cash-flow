using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Domain.Exceptions;
using FluentAssertions;

namespace CashFlow.UnitTests.Transactions.Domain;

public class AppUserTests
{
    private const string ValidUsername = "carlos";
    private const string ValidHash = "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string ValidRole = "Merchant";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [Fact]
    public void Create_WithValidData_ReturnsActiveUserWithLowercaseUsername()
    {
        var u = AppUser.Create("Carlos", ValidHash, ValidRole);

        u.Id.Should().NotBe(Guid.Empty);
        u.Username.Should().Be("carlos");
        u.PasswordHash.Should().Be(ValidHash);
        u.Role.Should().Be(ValidRole);
        u.IsActive.Should().BeTrue();
        u.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        u.LastLoginAt.Should().BeNull();
        u.FailedLoginAttempts.Should().Be(0);
        u.LockedUntil.Should().BeNull();
        u.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsUsernameWhitespace()
    {
        var u = AppUser.Create("  carlos  ", ValidHash, ValidRole);
        u.Username.Should().Be("carlos");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceUsername_ThrowsDomainException(string? username)
    {
        var act = () => AppUser.Create(username!, ValidHash, ValidRole);
        act.Should().Throw<DomainException>().WithMessage("*Username*obrigatório*");
    }

    [Fact]
    public void Create_WithTooLongUsername_ThrowsDomainException()
    {
        var longUsername = new string('x', 65);
        var act = () => AppUser.Create(longUsername, ValidHash, ValidRole);
        act.Should().Throw<DomainException>().WithMessage("*Username*64*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespacePasswordHash_ThrowsDomainException(string? hash)
    {
        var act = () => AppUser.Create(ValidUsername, hash!, ValidRole);
        act.Should().Throw<DomainException>().WithMessage("*Password hash*obrigatório*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceRole_ThrowsDomainException(string? role)
    {
        var act = () => AppUser.Create(ValidUsername, ValidHash, role!);
        act.Should().Throw<DomainException>().WithMessage("*Role*obrigatório*");
    }

    [Fact]
    public void Rehydrate_PreservesAllFields()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow.AddDays(-7);
        var lastLogin = DateTimeOffset.UtcNow.AddHours(-1);
        var locked = DateTimeOffset.UtcNow.AddMinutes(5);

        var u = AppUser.Rehydrate(id, "carlos", ValidHash, "Merchant",
            isActive: true, created, lastLogin,
            failedLoginAttempts: 3, lockedUntil: locked);

        u.Id.Should().Be(id);
        u.Username.Should().Be("carlos");
        u.PasswordHash.Should().Be(ValidHash);
        u.Role.Should().Be("Merchant");
        u.IsActive.Should().BeTrue();
        u.CreatedAt.Should().Be(created);
        u.LastLoginAt.Should().Be(lastLogin);
        u.FailedLoginAttempts.Should().Be(3);
        u.LockedUntil.Should().Be(locked);
    }

    [Fact]
    public void Rehydrate_PreservesInactiveAndUnlockedState()
    {
        var u = AppUser.Rehydrate(Guid.NewGuid(), "carlos", ValidHash, "Merchant",
            isActive: false, DateTimeOffset.UtcNow, lastLoginAt: null,
            failedLoginAttempts: 0, lockedUntil: null);

        u.IsActive.Should().BeFalse();
        u.LastLoginAt.Should().BeNull();
        u.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void IsLockedOut_FalseWhenLockedUntilIsNull()
    {
        var u = AppUser.Create(ValidUsername, ValidHash, ValidRole);
        u.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_TrueWhenLockedUntilInFuture()
    {
        var u = AppUser.Rehydrate(Guid.NewGuid(), "carlos", ValidHash, "Merchant",
            isActive: true, DateTimeOffset.UtcNow, lastLoginAt: null,
            failedLoginAttempts: 5,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10));
        u.IsLockedOut.Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_FalseWhenLockedUntilInPast()
    {
        var u = AppUser.Rehydrate(Guid.NewGuid(), "carlos", ValidHash, "Merchant",
            isActive: true, DateTimeOffset.UtcNow, lastLoginAt: null,
            failedLoginAttempts: 5,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));
        u.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_IncrementsCounter()
    {
        var u = AppUser.Create(ValidUsername, ValidHash, ValidRole);

        u.RegisterFailedLogin(MaxAttempts, LockoutDuration);
        u.FailedLoginAttempts.Should().Be(1);
        u.IsLockedOut.Should().BeFalse();

        u.RegisterFailedLogin(MaxAttempts, LockoutDuration);
        u.FailedLoginAttempts.Should().Be(2);
        u.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_LocksAccountWhenReachingMaxAttempts()
    {
        var u = AppUser.Create(ValidUsername, ValidHash, ValidRole);

        for (var i = 0; i < MaxAttempts - 1; i++)
            u.RegisterFailedLogin(MaxAttempts, LockoutDuration);

        u.IsLockedOut.Should().BeFalse("not yet locked at attempt N-1");

        u.RegisterFailedLogin(MaxAttempts, LockoutDuration);

        u.FailedLoginAttempts.Should().Be(MaxAttempts);
        u.IsLockedOut.Should().BeTrue();
        u.LockedUntil.Should().NotBeNull();
        u.LockedUntil!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow.Add(LockoutDuration), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RecordSuccessfulLogin_ResetsCounterAndUnlocks()
    {
        var u = AppUser.Rehydrate(Guid.NewGuid(), "carlos", ValidHash, "Merchant",
            isActive: true, DateTimeOffset.UtcNow, lastLoginAt: null,
            failedLoginAttempts: 4,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        u.RecordSuccessfulLogin();

        u.FailedLoginAttempts.Should().Be(0);
        u.LockedUntil.Should().BeNull();
        u.LastLoginAt.Should().NotBeNull();
        u.LastLoginAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }
}
