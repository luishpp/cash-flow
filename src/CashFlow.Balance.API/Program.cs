using System.Threading.RateLimiting;
using CashFlow.Balance.API.Application.Services;
using CashFlow.Balance.API.Infrastructure.Configuration;
using CashFlow.Balance.Core.Infrastructure.Persistence;
using CashFlow.Balance.Core.Infrastructure.Repositories;
using CashFlow.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Serilog;

// Balance.API (ADR-026): read side puro.
// Consumer + ConsolidationService + ProcessedEventsRepository + Migrations vivem em Balance.Worker.
// AdminController (DLQ ops) ainda mora aqui — sai em ADR-028 (Admin.API).
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

// ---------- Security (ADR-016) ----------
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();

// ---------- Rate Limiting (ADR-006) ----------
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

// ---------- Persistence read-only (ADR-010, ADR-026) ----------
// Worker é dono do schema; API só lê. Connection string usa o mesmo user app_balance.
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<IBalanceRepository, BalanceRepository>();

// ---------- Application ----------
builder.Services.AddScoped<IBalanceQueryService, BalanceQueryService>();

// ---------- Health Checks (ADR-013) ----------
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

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
