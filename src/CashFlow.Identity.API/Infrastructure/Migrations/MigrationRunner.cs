using System.Reflection;
using DbUp;

namespace CashFlow.Identity.API.Infrastructure.Migrations;

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
            .JournalToPostgresqlTable("identity", "schemaversions")
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(result.Error, "Falha ao aplicar migrations Identity.");
            throw new InvalidOperationException("Migrations falharam — abortando startup.", result.Error);
        }

        logger.LogInformation("Migrations Identity aplicadas com sucesso.");
    }
}
