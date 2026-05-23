namespace CashFlow.Transactions.API;

/// <summary>
/// Marker para <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> em testes E2E (ADR-022).
/// Existe apenas para tipar a referência ao assembly — o <c>Program</c> real (top-level)
/// fica no namespace global, o que causaria ambiguidade quando o projeto de teste referencia
/// múltiplos APIs.
/// </summary>
public sealed class TransactionsApiAssembly { }
