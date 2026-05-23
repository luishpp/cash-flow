using CashFlow.Transactions.API.Application.DTOs;

namespace CashFlow.Transactions.API.Application.Services;

public interface ITransactionService
{
    /// <summary>
    /// Registra uma ou mais transações em uma única UoW (tudo-ou-nada).
    /// Eventos são publicados na ordem após o commit; falha de publish é logada e não desfaz o INSERT.
    /// </summary>
    Task<IReadOnlyList<TransactionResponse>> RegisterManyAsync(
        IReadOnlyList<RegisterTransactionRequest> requests, CancellationToken ct = default);

    Task<TransactionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionResponse>> ListByDateAsync(DateOnly date, CancellationToken ct = default);
}
