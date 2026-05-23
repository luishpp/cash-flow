using CashFlow.Transactions.API.Domain.Exceptions;

namespace CashFlow.Transactions.API.Domain.ValueObjects;

public enum TransactionType
{
    Credit = 1,
    Debit = 2
}

public static class TransactionTypeExtensions
{
    public static TransactionType Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Tipo da transação é obrigatório.");

        return value.Trim().ToLowerInvariant() switch
        {
            "credit" or "credito" or "crédito" => TransactionType.Credit,
            "debit"  or "debito"  or "débito"  => TransactionType.Debit,
            _ => throw new DomainException(
                $"Tipo de transação inválido: '{value}'. Use 'credit' ou 'debit'.")
        };
    }

    public static string ToSnakeCase(this TransactionType type) => type switch
    {
        TransactionType.Credit => "credit",
        TransactionType.Debit  => "debit",
        _ => throw new DomainException($"Tipo de transação desconhecido: {type}")
    };
}
