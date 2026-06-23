using CashFlow.Identity.API.Domain.Entities;
using CashFlow.Identity.API.Domain.Exceptions;
using FluentAssertions;

namespace CashFlow.UnitTests.Identity.Domain;

public class RefreshTokenTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private const string ValidHash = "validHashBase64Encoded==";
    private static readonly TimeSpan ValidLifetime = TimeSpan.FromDays(7);

    [Fact]
    public void Issue_WithValidArgs_CreatesActiveTokenWithFutureExpiry()
    {
        var t = RefreshToken.Issue(ValidUserId, ValidHash, ValidLifetime);

        t.Id.Should().NotBe(Guid.Empty);
        t.UserId.Should().Be(ValidUserId);
        t.TokenHash.Should().Be(ValidHash);
        t.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        t.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(ValidLifetime), TimeSpan.FromSeconds(2));
        t.RevokedAt.Should().BeNull();
        t.ReplacedByTokenHash.Should().BeNull();
        t.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Issue_WithEmptyUserId_ThrowsDomainException()
    {
        var act = () => RefreshToken.Issue(Guid.Empty, ValidHash, ValidLifetime);
        act.Should().Throw<DomainException>().WithMessage("*UserId*obrigatório*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_WithNullOrWhitespaceHash_ThrowsDomainException(string? hash)
    {
        var act = () => RefreshToken.Issue(ValidUserId, hash!, ValidLifetime);
        act.Should().Throw<DomainException>().WithMessage("*TokenHash*obrigatório*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Issue_WithNonPositiveLifetime_ThrowsDomainException(int seconds)
    {
        var act = () => RefreshToken.Issue(ValidUserId, ValidHash, TimeSpan.FromSeconds(seconds));
        act.Should().Throw<DomainException>().WithMessage("*Lifetime*positivo*");
    }

    [Fact]
    public void IsActive_FalseWhenExpired()
    {
        var t = RefreshToken.Rehydrate(
            Guid.NewGuid(), ValidUserId, ValidHash,
            createdAt: DateTimeOffset.UtcNow.AddDays(-8),
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
            revokedAt: null, replacedByTokenHash: null);

        t.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_FalseWhenRevoked()
    {
        var t = RefreshToken.Rehydrate(
            Guid.NewGuid(), ValidUserId, ValidHash,
            createdAt: DateTimeOffset.UtcNow.AddDays(-1),
            expiresAt: DateTimeOffset.UtcNow.AddDays(6),
            revokedAt: DateTimeOffset.UtcNow.AddHours(-1),
            replacedByTokenHash: null);

        t.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_TrueWhenWithinLifetimeAndNotRevoked()
    {
        var t = RefreshToken.Rehydrate(
            Guid.NewGuid(), ValidUserId, ValidHash,
            createdAt: DateTimeOffset.UtcNow.AddHours(-1),
            expiresAt: DateTimeOffset.UtcNow.AddDays(7),
            revokedAt: null, replacedByTokenHash: null);

        t.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Revoke_SetsRevokedAtAndReplacedByTokenHash()
    {
        var t = RefreshToken.Issue(ValidUserId, ValidHash, ValidLifetime);
        const string successorHash = "successorHash==";

        t.Revoke(successorHash);

        t.RevokedAt.Should().NotBeNull();
        t.RevokedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        t.ReplacedByTokenHash.Should().Be(successorHash);
        t.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Revoke_WithoutSuccessor_LeavesReplacedByNull()
    {
        var t = RefreshToken.Issue(ValidUserId, ValidHash, ValidLifetime);

        t.Revoke();

        t.RevokedAt.Should().NotBeNull();
        t.ReplacedByTokenHash.Should().BeNull();
    }

    [Fact]
    public void Revoke_IsIdempotent()
    {
        var t = RefreshToken.Issue(ValidUserId, ValidHash, ValidLifetime);

        t.Revoke();
        var firstRevokedAt = t.RevokedAt;

        Thread.Sleep(20);
        t.Revoke("other-hash");

        t.RevokedAt.Should().Be(firstRevokedAt, "revogação repetida não deve alterar o timestamp");
        t.ReplacedByTokenHash.Should().BeNull("segundo Revoke não deve sobrescrever ReplacedBy");
    }
}
