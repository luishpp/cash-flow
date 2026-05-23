using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace CashFlow.Shared.Security;

/// <summary>
/// Extensões de wire-up de autenticação/autorização — idênticas em ambas as APIs.
/// Centralizado em Shared para evitar duplicação entre Transactions.API e Balance.API.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Adiciona <see cref="JwtSettings"/>, <see cref="ITokenService"/> e JWT Bearer Authentication.
    /// </summary>
    public static IServiceCollection AddCashFlowAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException(
                $"Seção '{JwtSettings.SectionName}' não configurada em appsettings.");

        services.AddSingleton(jwt);
        services.AddSingleton<ITokenService, JwtTokenService>();

        var key = Encoding.UTF8.GetBytes(jwt.SecretKey);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.SaveToken = true;
            options.RequireHttpsMetadata = !environment.IsDevelopment();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = jwt.ValidateIssuer,
                ValidateAudience = jwt.ValidateAudience,
                ValidateLifetime = jwt.ValidateLifetime,
                ValidateIssuerSigningKey = jwt.ValidateIssuerSigningKey,

                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),

                ClockSkew = jwt.ClockSkew,

                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role,
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = ctx =>
                {
                    if (ctx.Exception is SecurityTokenExpiredException)
                        ctx.Response.Headers["Token-Expired"] = "true";
                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }

    /// <summary>
    /// Registra políticas reutilizáveis nomeadas em <see cref="AuthorizationPolicies"/>.
    /// </summary>
    public static IServiceCollection AddCashFlowAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(AuthorizationPolicies.RequireMerchant, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(CashFlowRoles.Merchant);
            });
        });

        return services;
    }
}
