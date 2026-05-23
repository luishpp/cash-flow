namespace CashFlow.Shared.Security;

/// <summary>
/// Nomes de políticas reutilizáveis. Single source of truth — controllers só citam o nome.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Apenas usuários autenticados (qualquer role).</summary>
    public const string RequireAuthenticated = nameof(RequireAuthenticated);

    /// <summary>Usuário autenticado com role <see cref="CashFlowRoles.Merchant"/>.</summary>
    public const string RequireMerchant = nameof(RequireMerchant);
}
