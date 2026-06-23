using CashFlow.Admin.API.Application.Admin;
using CashFlow.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;

// Admin.API (ADR-028) — endpoints administrativos da DLQ.
// Não depende de Postgres; só fala com RabbitMQ direto.
var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog (ADR-013) ----------
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "CashFlow.Admin.API")
    .WriteTo.Console());

// ---------- API ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CashFlow.Admin.API", Version = "v1" });

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

// ---------- Security (ADR-016) ----------
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();

// ---------- Application ----------
builder.Services.AddScoped<ErrorQueueRedeliveryService>();

// ---------- Health Checks (ADR-013) ----------
// Admin.API só expõe liveness — admin endpoints são best-effort.
// Readiness em RabbitMQ seria over-engineering: ops conviverão com 502 transitório
// se broker estiver fora; a DLQ não vai a lugar nenhum.
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------- Pipeline ----------
app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

public partial class Program { }
