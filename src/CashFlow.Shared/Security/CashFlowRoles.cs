namespace CashFlow.Shared.Security;

/// <summary>
/// Roles de aplicação. Constantes evitam strings mágicas espalhadas.
/// </summary>
public static class CashFlowRoles
{
    /// <summary>Comerciante — pode registrar lançamentos e consultar saldo (Carlos da persona).</summary>
    public const string Merchant = "Merchant";
}
