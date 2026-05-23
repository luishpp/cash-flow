using CashFlow.Shared.Security;
using CashFlow.Transactions.API.Application.DTOs;
using CashFlow.Transactions.API.Application.Services;
using CashFlow.Transactions.API.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Transactions.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireMerchant)]
public sealed class TransactionsController(
    ITransactionService service,
    IValidator<RegisterTransactionRequest> validator) : ControllerBase
{
    /// <summary>
    /// Registra uma ou mais transações em batch transacional (tudo-ou-nada).
    /// Body é sempre um array — envie [{ ... }] para registrar apenas uma.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        [FromBody] List<RegisterTransactionRequest> requests, CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
            return BadRequest(new { error = "Body deve ser um array com pelo menos uma transação." });

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < requests.Count; i++)
        {
            var v = await validator.ValidateAsync(requests[i], ct);
            if (v.IsValid) continue;
            foreach (var e in v.Errors)
            {
                var key = $"[{i}].{e.PropertyName}";
                errors[key] = errors.TryGetValue(key, out var existing)
                    ? [.. existing, e.ErrorMessage]
                    : [e.ErrorMessage];
            }
        }
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        try
        {
            var responses = await service.RegisterManyAsync(requests, ct);
            return StatusCode(StatusCodes.Status201Created, responses);
        }
        catch (DomainException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var transaction = await service.GetByIdAsync(id, ct);
        return transaction is null ? NotFound() : Ok(transaction);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TransactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByDate(
        [FromQuery] DateOnly date, CancellationToken ct)
    {
        var transactions = await service.ListByDateAsync(date, ct);
        return Ok(transactions);
    }
}
