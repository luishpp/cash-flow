using System.Threading.RateLimiting;
using CashFlow.Balance.API.Application.Services;
using CashFlow.Balance.API.Consumers;
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
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Bearer obtido na Transactions API (/api/v1/auth/login)."
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
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("balance", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(1);
        opt.PermitLimit = 50;
        opt.QueueLimit = 5;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";
        await ctx.HttpContext.Response.WriteAsync(
            "Rate limit excedido. Tente novamente em 1 segundo.", ct);
    };
});

// ---------- Persistence (ADR-010) ----------
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<IBalanceRepository, BalanceRepository>();
builder.Services.AddScoped<IProcessedEventsRepository, ProcessedEventsRepository>();

// ---------- Application ----------
builder.Services.AddScoped<IBalanceQueryService, BalanceQueryService>();
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

// ---------- MassTransit / RabbitMQ + Consumer (ADR-002, ADR-004) ----------
builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<TransactionConsumer>();
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });
        rmq.ReceiveEndpoint("balance.transaction-registered", ep =>
        {
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
