using CashFlow.Transactions.API.Application.DTOs;
using FluentValidation;

namespace CashFlow.Transactions.API.Application.Validators;

/// <summary>
/// Validação de input no boundary (ADR-013). Falha aqui retorna HTTP 400.
/// Invariantes de domínio adicionais são validadas dentro da entidade Transaction.
/// </summary>
public sealed class RegisterTransactionValidator : AbstractValidator<RegisterTransactionRequest>
{
    public RegisterTransactionValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Tipo é obrigatório.")
            .Must(t => t is "credit" or "debit")
            .WithMessage("Tipo deve ser 'credit' ou 'debit'.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Valor deve ser maior que zero.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Descrição é obrigatória.")
            .MaximumLength(200).WithMessage("Descrição não pode exceder 200 caracteres.");

        RuleFor(x => x.MovementDate)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Data do movimento não pode ser futura.");
    }
}
