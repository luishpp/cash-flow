using CashFlow.Shared.Security;
using CashFlow.Transactions.API.Application.DTOs;
using CashFlow.Transactions.API.Application.Services;
using CashFlow.Transactions.API.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Transactions.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireMerchant)]
public sealed class TransactionsController(ITransactionService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterTransactionRequest request, CancellationToken ct)
    {
        try
        {
            var response = await service.RegisterAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
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
