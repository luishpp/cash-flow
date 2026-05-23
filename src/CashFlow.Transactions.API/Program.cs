using CashFlow.Shared.Security;
using CashFlow.Transactions.API.Application.Auth;
using CashFlow.Transactions.API.Application.Services;
using CashFlow.Transactions.API.Application.Validators;
using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Infrastructure.Auth;
using CashFlow.Transactions.API.Infrastructure.Messaging;
using CashFlow.Transactions.API.Infrastructure.Migrations;
using CashFlow.Transactions.API.Infrastructure.Outbox;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using CashFlow.Transactions.API.Infrastructure.Repositories;
using FluentValidation;
using FluentValidation.AspNetCore;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// "Testing" é usado pelo CashFlow.Bdd.Tests (WebApplicationFactory) — ver ADR-022.
// Quando ativo: skip MassTransit/RabbitMQ + skip migrations no startup (testes controlam).
var isTesting = builder.Environment.IsEnvironment("Testing");

// ---------- Serilog (ADR-013) ----------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "CashFlow.Transactions.API")
    .WriteTo.Console());

// ---------- Configuração ----------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
var rabbitHost = builder.Configuration.GetValue<string>("RabbitMq:Host") ?? "rabbitmq";
var rabbitUser = builder.Configuration.GetValue<string>("RabbitMq:User") ?? "guest";
var rabbitPass = builder.Configuration.GetValue<string>("RabbitMq:Password") ?? "guest";

// ---------- API ----------
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ctx =>
            new BadRequestObjectResult(new ValidationProblemDetails(ctx.ModelState));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CashFlow.Transactions.API", Version = "v1" });

    // Botão "Authorize" no Swagger UI — UX importa para o avaliador.
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Cole o valor completo do header: Bearer <jwt>. Obtenha o JWT em POST /api/v1/auth/login."
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

// FluentValidation (ADR-013)
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterTransactionValidator>();

// ---------- Security (ADR-016, ADR-021, ADR-023, ADR-024) ----------
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();
builder.Services.Configure<LockoutSettings>(builder.Configuration.GetSection(LockoutSettings.SectionName));
builder.Services.Configure<RefreshTokenSettings>(builder.Configuration.GetSection(RefreshTokenSettings.SectionName));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<IRefreshTokenFactory, Sha256RefreshTokenFactory>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// ---------- Persistence (Dapper + UoW) (ADR-010) ----------
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// ---------- Application ----------
builder.Services.AddScoped<ITransactionService, TransactionService>();

// ---------- Outbox (ADR-007 mitigation: at-least-once ponta-a-ponta) ----------
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
if (!isTesting)
    builder.Services.AddHostedService<OutboxDispatcher>();

// ---------- MassTransit / RabbitMQ (ADR-002) ----------
if (!isTesting)
{
    builder.Services.AddMassTransit(cfg =>
    {
        cfg.UsingRabbitMq((ctx, rmq) =>
        {
            rmq.Host(rabbitHost, "/", h =>
            {
                h.Username(rabbitUser);
                h.Password(rabbitPass);
            });
        });
    });
    builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
}
else
{
    // ADR-022: BDD E2E não usa broker — IEventPublisher fica como no-op.
    // Testes que precisam observar publicação podem sobrescrever via ConfigureTestServices.
    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
}

// ---------- Health Checks (ADR-013) ----------
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// ---------- Migrations + Seed demo (ADR-010, ADR-021) ----------
if (!isTesting)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    MigrationRunner.EnsureUpToDate(connectionString, logger);
    await DemoUserSeeder.EnsureSeededAsync(app.Services, logger);
}

// ---------- Pipeline ----------
app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();

// Tradução de DomainException → HTTP 422
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

public partial class Program { }
