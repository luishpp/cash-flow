using CashFlow.Balance.Core.Infrastructure.Persistence;
using CashFlow.Balance.Core.Infrastructure.Repositories;
using CashFlow.Balance.Worker.Application.Services;
using CashFlow.Balance.Worker.Consumers;
using CashFlow.Balance.Worker.Infrastructure.Migrations;
using CashFlow.Balance.Worker.Infrastructure.Repositories;
using MassTransit;
using Polly;
using Polly.Retry;
using Serilog;

// Worker Service host (ADR-026): IHost genérico, sem ASP.NET.
// Dono das migrations do schema balance (worker é o writer).
var builder = Host.CreateApplicationBuilder(args);

// ---------- Serilog (ADR-013) ----------
builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "CashFlow.Balance.Worker")
    .WriteTo.Console());

// ---------- Configuração ----------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
var rabbitHost = builder.Configuration.GetValue<string>("RabbitMq:Host") ?? "rabbitmq";
var rabbitUser = builder.Configuration.GetValue<string>("RabbitMq:User") ?? "guest";
var rabbitPass = builder.Configuration.GetValue<string>("RabbitMq:Password") ?? "guest";

// ---------- Persistence (Dapper + UoW) ----------
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<IBalanceRepository, BalanceRepository>();
builder.Services.AddScoped<IProcessedEventsRepository, ProcessedEventsRepository>();

// ---------- Application ----------
builder.Services.AddScoped<IConsolidationService, ConsolidationService>();

// ---------- Polly Retry (ADR-005) ----------
builder.Services.AddResiliencePipeline("consumer-pipeline", pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    });
});

// ---------- MassTransit / RabbitMQ + Consumer (ADR-002, ADR-026) ----------
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<TransactionConsumer>();
    cfg.AddDelayedMessageScheduler();
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.UseDelayedMessageScheduler();
        rmq.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });
        rmq.ReceiveEndpoint("balance.transaction-registered", ep =>
        {
            // FIFO real: SAC + processamento sequencial em cada réplica.
            ep.SingleActiveConsumer = true;
            ep.ConcurrentMessageLimit = 1;
            ep.PrefetchCount = 1;

            // 2 níveis de retry: Polly (transientes <3s) + delayed redelivery (outages 1/5/15min).
            // Após esgotar, mensagem vai para `balance.transaction-registered_error` (DLQ).
            ep.UseDelayedRedelivery(r => r.Intervals(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)));

            ep.ConfigureConsumer<TransactionConsumer>(ctx);
        });
    });
});

var host = builder.Build();

// ---------- Migrations no startup (ADR-010, ADR-026) ----------
// Worker é dono do schema balance — ele cria as tabelas; API só consome.
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    MigrationRunner.EnsureUpToDate(connectionString, logger);
}

await host.RunAsync();

public partial class Program { }
