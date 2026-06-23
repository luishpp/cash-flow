using CashFlow.Identity.API;
using CashFlow.Identity.API.Infrastructure.Auth;
using CashFlow.Transactions.API;
using IdentityMigrationRunner = CashFlow.Identity.API.Infrastructure.Migrations.MigrationRunner;
using TransactionsMigrationRunner = CashFlow.Transactions.API.Infrastructure.Migrations.MigrationRunner;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Reqnroll;
using Testcontainers.PostgreSql;

namespace CashFlow.Bdd.Tests.Setup;

/// <summary>
/// Fixture compartilhada entre cenários BDD E2E (ADR-022, refatorada após ADR-027).
///
/// Pós-refator (ADR-027): autenticação vive em <c>Identity.API</c>, lançamentos em
/// <c>Transactions.API</c>. O fixture sobe **DOIS** <c>WebApplicationFactory</c> in-process,
/// um para cada API, com o mesmo Postgres (schemas separados) e o mesmo JWT secret —
/// reproduzindo a topologia distribuída em testes E2E.
///
/// Fluxo:
///   1. Sobe 1 Postgres via Testcontainers (~3-8s)
///   2. Cria schemas <c>identity</c> + <c>transactions</c>
///   3. Boot Identity.API (ASPNETCORE_ENVIRONMENT=Testing pula migrations no startup)
///   4. Boot Transactions.API (ASPNETCORE_ENVIRONMENT=Testing pula MassTransit + migrations)
///   5. Aplica migrations Identity + Transactions manualmente
///   6. Seed demo user via DemoUserSeeder rodando contra Identity services
///   7. Expõe <see cref="IdentityClient"/>, <see cref="TransactionsClient"/> e
///      o helper <see cref="ClientFor(string)"/> para os steps
/// </summary>
[Binding]
public sealed class CashFlowApiFixture
{
    /// <summary>Cliente para Identity.API — /api/v1/auth/login, /refresh, /logout.</summary>
    public static HttpClient IdentityClient { get; private set; } = null!;

    /// <summary>Cliente para Transactions.API — /api/v1/transactions.</summary>
    public static HttpClient TransactionsClient { get; private set; } = null!;

    /// <summary>
    /// Roteamento por URL prefix: steps recebem a URL como parâmetro do Gherkin
    /// (ex.: <c>"/api/v1/auth/login"</c>) e este helper devolve o cliente correto.
    /// Mantém as features pt-BR limpas — não há "client mágico", mas também não há
    /// `if/else` espalhado nos steps.
    /// </summary>
    public static HttpClient ClientFor(string url) =>
        url.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
            ? IdentityClient
            : TransactionsClient;

    public static string DemoUsername => DemoUserSeeder.DemoUsername;
    public static string DemoPassword => DemoUserSeeder.DemoPassword;

    /// <summary>Limite de tentativas configurado via env var sob env "Testing".</summary>
    public const int LockoutMaxAttempts = 3; // ↓ vs prod (5) — testes ficam mais rápidos

    private static PostgreSqlContainer? _postgres;
    private static WebApplicationFactory<IdentityApiAssembly>? _identityFactory;
    private static WebApplicationFactory<TransactionsApiAssembly>? _transactionsFactory;
    private static string _connectionString = string.Empty;

    [BeforeTestRun]
    public static async Task SetupAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("cashflow")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        // Env vars antes de instanciar WebApplicationFactory: ambos os Program.cs lêem
        // builder.Configuration eagerly. Como Testcontainers sobe Postgres "puro" (sem init.sql),
        // ambos os serviços conectam com o usuário SUPERUSER "postgres" — o que dá GRANT total
        // em todos os schemas. Em produção, init.sql cria 3 users separados; em teste, um só basta.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);
        Environment.SetEnvironmentVariable("Authentication__Jwt__Issuer", "cashflow-test");
        Environment.SetEnvironmentVariable("Authentication__Jwt__Audience", "cashflow-test-api");
        Environment.SetEnvironmentVariable("Authentication__Jwt__SecretKey",
            "test-only-symmetric-key-min-32-bytes-padding-padding");
        Environment.SetEnvironmentVariable("Authentication__Jwt__TokenExpirationMinutes", "15");
        Environment.SetEnvironmentVariable("Authentication__Lockout__MaxFailedAttempts",
            LockoutMaxAttempts.ToString());
        Environment.SetEnvironmentVariable("Authentication__Lockout__LockoutDuration", "00:15:00");
        Environment.SetEnvironmentVariable("Authentication__RefreshToken__Lifetime", "7.00:00:00");
        Environment.SetEnvironmentVariable("Authentication__RefreshToken__TokenBytes", "32");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        // Boot dos dois APIs in-process. Cada um tem seu próprio WebApplicationFactory,
        // service provider, e middleware pipeline — exatamente como em produção, só que
        // sem rede entre eles (CreateClient devolve HttpClient com handler in-memory).
        _identityFactory = new WebApplicationFactory<IdentityApiAssembly>()
            .WithWebHostBuilder(_ => { /* env vars cobrem todo o setup */ });
        _transactionsFactory = new WebApplicationFactory<TransactionsApiAssembly>()
            .WithWebHostBuilder(_ => { });

        IdentityClient = _identityFactory.CreateClient();
        TransactionsClient = _transactionsFactory.CreateClient();

        // Em produção init.sql cria schemas. Testcontainers sobe Postgres puro — criamos aqui.
        await using (var bootstrap = new NpgsqlConnection(_connectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "CREATE SCHEMA IF NOT EXISTS identity; CREATE SCHEMA IF NOT EXISTS transactions;",
                bootstrap);
            await cmd.ExecuteNonQueryAsync();
        }

        // Em "Testing" ambos os Program.cs pulam migrations — aplicamos aqui na ordem certa.
        // Identity primeiro (cria identity.app_users), depois Transactions (cria transactions.*).
        using var scope = _identityFactory.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CashFlowApiFixture>>();
        IdentityMigrationRunner.EnsureUpToDate(_connectionString, logger);
        TransactionsMigrationRunner.EnsureUpToDate(_connectionString, logger);

        // Seed demo user via services do Identity.API (onde IAppUserRepository + IPasswordHasher
        // + IUnitOfWork estão registrados após ADR-027).
        await DemoUserSeeder.EnsureSeededAsync(_identityFactory.Services, logger);
    }

    /// <summary>
    /// Reset entre cenários: zera lockout do demo user + apaga refresh tokens.
    /// Garante que um cenário (ex.: lockout) não polua o próximo.
    /// </summary>
    [BeforeScenario(Order = 0)]
    public static async Task ResetAuthStateAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            @"UPDATE identity.app_users
                 SET failed_login_attempts = 0,
                     locked_until = NULL
               WHERE username = @u;", conn))
        {
            cmd.Parameters.AddWithValue("u", DemoUsername);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM identity.refresh_tokens;", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [AfterTestRun]
    public static async Task TeardownAsync()
    {
        IdentityClient?.Dispose();
        TransactionsClient?.Dispose();
        if (_identityFactory is not null) await _identityFactory.DisposeAsync();
        if (_transactionsFactory is not null) await _transactionsFactory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}
