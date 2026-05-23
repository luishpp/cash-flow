using CashFlow.Transactions.API;
using CashFlow.Transactions.API.Infrastructure.Auth;
using CashFlow.Transactions.API.Infrastructure.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Reqnroll;
using Testcontainers.PostgreSql;

namespace CashFlow.Bdd.Tests.Setup;

/// <summary>
/// Fixture compartilhada entre cenários BDD E2E (ADR-022):
///   - sobe 1 Postgres via Testcontainers (~3-8s)
///   - registra WebApplicationFactory&lt;TransactionsApiAssembly&gt; com env "Testing"
///     (MassTransit/RabbitMQ pulado via flag, IEventPublisher vira no-op)
///   - aplica migrations + seed demo user
///   - reseta lockout + revoga refresh tokens entre cenários (estado independente)
///   - expõe HttpClient para os steps
/// </summary>
[Binding]
public sealed class CashFlowApiFixture
{
    public static HttpClient Client { get; private set; } = null!;
    public static string DemoUsername => DemoUserSeeder.DemoUsername;
    public static string DemoPassword => DemoUserSeeder.DemoPassword;
    /// <summary>Limite de tentativas configurado no Program.cs sob env "Testing".</summary>
    public const int LockoutMaxAttempts = 3; // ↓ vs prod (5) — testes ficam mais rápidos

    private static PostgreSqlContainer? _postgres;
    private static WebApplicationFactory<TransactionsApiAssembly>? _factory;
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

        // Env vars antes de instanciar WebApplicationFactory: Program.cs lê builder.Configuration
        // eagerly (antes de Build), então ConfigureAppConfiguration chega tarde demais.
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

        _factory = new WebApplicationFactory<TransactionsApiAssembly>()
            .WithWebHostBuilder(builder => { /* env vars já cobrem todo o setup */ });

        Client = _factory.CreateClient();

        // Em "Testing" o Program.cs pula migrations + seed — aplicamos manualmente aqui.
        using var scope = _factory.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CashFlowApiFixture>>();
        MigrationRunner.EnsureUpToDate(_connectionString, logger);
        await DemoUserSeeder.EnsureSeededAsync(_factory.Services, logger);
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
            @"UPDATE transactions.app_users
                 SET failed_login_attempts = 0,
                     locked_until = NULL
               WHERE username = @u;", conn))
        {
            cmd.Parameters.AddWithValue("u", DemoUsername);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM transactions.refresh_tokens;", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [AfterTestRun]
    public static async Task TeardownAsync()
    {
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}
