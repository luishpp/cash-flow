using System.Reflection;
using DbUp;

namespace CashFlow.Balance.API.Infrastructure.Migrations;

public static class MigrationRunner
{
    public static void EnsureUpToDate(string connectionString, ILogger logger)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                name => name.Contains(".Migrations.Scripts."))
            .JournalToPostgresqlTable("balance", "schemaversions")
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(result.Error, "Falha ao aplicar migrations.");
            throw new InvalidOperationException("Migrations falharam — abortando startup.", result.Error);
        }

        logger.LogInformation("Migrations aplicadas com sucesso.");
    }
}
