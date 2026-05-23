using CashFlow.Balance.API.Application.DTOs;
using CashFlow.Balance.API.Application.Services;
using CashFlow.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CashFlow.Balance.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireMerchant)]
[EnableRateLimiting("balance")]
public sealed class BalanceController(IBalanceQueryService service) : ControllerBase
{
    [HttpGet("{date}")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByDate(DateOnly date, CancellationToken ct)
    {
        var balance = await service.GetByDateAsync(date, ct);
        if (balance is null)
            return Ok(new BalanceResponse(date, 0m, 0m, 0m, DateTimeOffset.UtcNow));
        return Ok(balance);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BalanceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListByPeriod(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        try
        {
            var balances = await service.ListByPeriodAsync(from, to, ct);
            return Ok(balances);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
