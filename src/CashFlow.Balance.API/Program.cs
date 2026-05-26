using System.Threading.RateLimiting;
using CashFlow.Balance.API.Application.Services;
using CashFlow.Balance.API.Consumers;
using CashFlow.Balance.API.Infrastructure.Configuration;
using CashFlow.Balance.API.Infrastructure.Migrations;
using CashFlow.Balance.API.Infrastructure.Persistence;
using CashFlow.Balance.API.Infrastructure.Repositories;
using CashFlow.Shared.Security;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Retry;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog (ADR-013) ----------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "CashFlow.Balance.API")
    .WriteTo.Console());

// ---------- Configuração ----------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
var rabbitHost = builder.Configuration.GetValue<string>("RabbitMq:Host") ?? "rabbitmq";
var rabbitUser = builder.Configuration.GetValue<string>("RabbitMq:User") ?? "guest";
var rabbitPass = builder.Configuration.GetValue<string>("RabbitMq:Password") ?? "guest";

// ---------- API ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CashFlow.Balance.API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Cole o valor completo do header: Bearer <jwt>. Obtenha o JWT em POST /api/v1/auth/login (na Transactions API)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ---------- Security (ADR-015) ----------
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();

// ---------- Rate Limiting (ADR-006) ----------
// Configurável via seção `RateLimiting:Balance` em appsettings (ou env vars
// RateLimiting__Balance__PermitLimit etc.). Seção opcional — sem ela, defaults do
// RateLimitSettings aplicam. Tweak sem rebuild: editar appsettings + restart.
var rateLimitSettings = builder.Configuration
    .GetSection(RateLimitSettings.SectionName)
    .Get<RateLimitSettings>() ?? new RateLimitSettings();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("balance", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(rateLimitSettings.WindowSeconds);
        opt.PermitLimit = rateLimitSettings.PermitLimit;
        opt.QueueLimit = rateLimitSettings.QueueLimit;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = rateLimitSettings.RetryAfterSeconds;
        await ctx.HttpContext.Response.WriteAsync(
            $"Rate limit excedido. Tente novamente em {rateLimitSettings.RetryAfterSeconds} segundo(s).", ct);
    };
});

// ---------- Persistence (ADR-010) ----------
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<IBalanceRepository, BalanceRepository>();
builder.Services.AddScoped<IProcessedEventsRepository, ProcessedEventsRepository>();

// ---------- Application ----------
builder.Services.AddScoped<IBalanceQueryService, BalanceQueryService>();
builder.Services.AddScoped<IConsolidationService, ConsolidationService>();
builder.Services.AddScoped<CashFlow.Balance.API.Application.Admin.ErrorQueueRedeliveryService>();

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

// ---------- MassTransit / RabbitMQ + Consumer (ADR-002, ADR-004) ----------
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<TransactionConsumer>();
    cfg.AddDelayedMessageScheduler();   // usa o plugin rabbitmq_delayed_message_exchange
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
            // FIFO real (ordering): SAC entre réplicas + processamento sequencial em cada uma.
            // Sem isso, MassTransit dispara em paralelo (PrefetchCount=16, concurrency ilimitada)
            // e a ordem dos lançamentos pode ser invertida — relevante para batches grandes.
            ep.SingleActiveConsumer = true;
            ep.ConcurrentMessageLimit = 1;
            ep.PrefetchCount = 1;

            // 1º nível: Polly dentro do Consumer (3 tentativas exp backoff até ~3s) — transientes rápidos.
            // 2º nível: redelivery agendada no broker — janelas maiores cobrem outage de banco/dependência.
            // Após esgotar todos, mensagem vai para `balance.transaction-registered_error` (DLQ visível).
            ep.UseDelayedRedelivery(r => r.Intervals(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)));

            ep.ConfigureConsumer<TransactionConsumer>(ctx);
        });
    });
});

// ---------- Health Checks (ADR-013) ----------
// Não inclui consumer no /health/ready — falha no consumer não derruba queries.
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// ---------- Migrations no startup (ADR-010) ----------
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    MigrationRunner.EnsureUpToDate(connectionString, logger);
}

// ---------- Pipeline ----------
// Swagger exposto em todos os ambientes — decisão consciente do MVP: o desafio
// pressupõe que o avaliador rode `docker compose up` (env Production) e teste via
// Swagger UI. Em deploy real, gatear com `IsDevelopment()` ou flag de config.
app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

public partial class Program { }
