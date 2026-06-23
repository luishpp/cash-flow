using CashFlow.Identity.API.Application.Auth;
using CashFlow.Identity.API.Domain.Exceptions;
using CashFlow.Identity.API.Infrastructure.Auth;
using CashFlow.Identity.API.Infrastructure.Migrations;
using CashFlow.Identity.API.Infrastructure.Persistence;
using CashFlow.Identity.API.Infrastructure.Repositories;
using CashFlow.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

// Identity.API (ADR-027) — bounded context próprio de autenticação.
// Schema postgres separado (identity), user próprio (app_identity).
var builder = WebApplication.CreateBuilder(args);

var isTesting = builder.Environment.IsEnvironment("Testing");

// ---------- Serilog (ADR-013) ----------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "CashFlow.Identity.API")
    .WriteTo.Console());

// ---------- Configuração ----------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");

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
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CashFlow.Identity.API", Version = "v1" });
});

// ---------- Security (ADR-016, ADR-021, ADR-023, ADR-024) ----------
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();
builder.Services.Configure<LockoutSettings>(builder.Configuration.GetSection(LockoutSettings.SectionName));
builder.Services.Configure<RefreshTokenSettings>(builder.Configuration.GetSection(RefreshTokenSettings.SectionName));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<IRefreshTokenFactory, Sha256RefreshTokenFactory>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// ---------- Persistence (Dapper + UoW) ----------
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// ---------- Health Checks (ADR-013) ----------
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// ---------- Migrations + Seed demo (ADR-027) ----------
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
