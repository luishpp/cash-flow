using CashFlow.Shared.Security;
using CashFlow.Transactions.API.Application.Auth;
using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using CashFlow.Transactions.API.Infrastructure.Repositories;

namespace CashFlow.Transactions.API.Infrastructure.Auth;

/// <summary>
/// Seed do usuário demo (<c>carlos</c> / <c>S3cret!ChangeMe</c>) com hash Argon2id real.
/// Roda no startup, idempotente. <strong>Remover ou desabilitar em produção</strong> via
/// flag de configuração — usar apenas para demonstração local.
/// </summary>
public static class DemoUserSeeder
{
    public const string DemoUsername = "carlos";
    public const string DemoPassword = "S3cret!ChangeMe";
    public const string DemoRole = nameof(CashFlowRoles.Merchant);

    public static async Task EnsureSeededAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        // CreateAsyncScope: DapperUnitOfWork é IAsyncDisposable; o `using` síncrono falharia.
        await using var scope = services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppUserRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginAsync(ct);

        var existing = await repo.GetByUsernameAsync(DemoUsername, ct);
        if (existing is not null)
        {
            logger.LogInformation("Usuário demo '{Username}' já existe — seed skipped.", DemoUsername);
            return;
        }

        var hash = hasher.Hash(DemoPassword);
        var user = AppUser.Create(DemoUsername, hash, DemoRole);
        await repo.InsertAsync(user, ct);
        await uow.CommitAsync(ct);

        logger.LogWarning(
            "Usuário demo '{Username}' criado com hash Argon2id. ⚠️ Demo only — remover em produção.",
            DemoUsername);
    }
}
