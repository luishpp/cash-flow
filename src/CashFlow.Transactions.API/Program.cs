using CashFlow.Shared.Security;
using CashFlow.Transactions.API.Application.Services;
using CashFlow.Transactions.API.Application.Validators;
using CashFlow.Transactions.API.Domain.Exceptions;
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

// Transactions.API (após ADR-027): write side puro do BC Transactions.
// Auth foi extraída para Identity.API (ADR-027). Esta API só VALIDA JWT (issued por Identity).
var builder = WebApplication.CreateBuilder(args);

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

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Cole o valor completo do header: Bearer <jwt>. Obtenha o JWT em POST /api/v1/auth/login (na Identity API)."
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

// ---------- Security (ADR-016) ----------
// Transactions.API só VALIDA JWT (issued pela Identity.API — ADR-027).
// Mesma SecretKey/Issuer/Audience compartilhada via env vars.
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();

// ---------- Persistence (Dapper + UoW) (ADR-010) ----------
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();

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
    builder.Services.AddSingleton<IEventPublisher, NoOpEventPublisher>();
}

// ---------- Health Checks (ADR-013) ----------
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// ---------- Migrations (ADR-010) ----------
// DemoUserSeeder foi pra Identity.API junto com /auth — ADR-027.
if (!isTesting)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    MigrationRunner.EnsureUpToDate(connectionString, logger);
}

// ---------- Pipeline ----------
app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogRequestLogging();

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
