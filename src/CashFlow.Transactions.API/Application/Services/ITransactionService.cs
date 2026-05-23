using CashFlow.Transactions.API.Application.DTOs;

namespace CashFlow.Transactions.API.Application.Services;

public interface ITransactionService
{
    Task<TransactionResponse> RegisterAsync(RegisterTransactionRequest request, CancellationToken ct = default);
    Task<TransactionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TransactionResponse>> ListByDateAsync(DateOnly date, CancellationToken ct = default);
}
