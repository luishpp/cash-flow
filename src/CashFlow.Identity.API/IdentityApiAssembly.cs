namespace CashFlow.Identity.API;

/// <summary>
/// Marker para <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> em testes E2E (ADR-022, ADR-027).
/// Existe apenas para tipar a referência ao assembly — o <c>Program</c> real (top-level)
/// fica no namespace global, o que causaria ambiguidade quando o projeto de teste referencia
/// múltiplos APIs (Identity, Transactions, Balance, Admin).
/// </summary>
public sealed class IdentityApiAssembly { }
