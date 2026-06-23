using CashFlow.Admin.API.Application.Admin;
using CashFlow.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Admin.API.Controllers;

/// <summary>
/// Operações administrativas — visibilidade e reprocessamento da DLQ.
/// Em produção: separar para uma role Admin própria e expor por trás de gateway interno.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthorizationPolicies.RequireMerchant)]
public sealed class AdminController(ErrorQueueRedeliveryService service) : ControllerBase
{
    /// <summary>Quantidade de mensagens em <c>balance.transaction-registered_error</c> agora.</summary>
    [HttpGet("errors/count")]
    [ProducesResponseType(typeof(ErrorCountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Count(CancellationToken ct)
    {
        var count = await service.PeekErrorCountAsync(ct);
        return Ok(new ErrorCountResponse(count));
    }

    /// <summary>Move mensagens da DLQ para a fila principal para reprocessamento.</summary>
    [HttpPost("errors/redeliver")]
    [ProducesResponseType(typeof(RedeliverResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Redeliver([FromQuery] int? max, CancellationToken ct)
    {
        var moved = await service.RedeliverAllAsync(max, ct);
        return Ok(new RedeliverResponse(moved));
    }

    public sealed record ErrorCountResponse(uint Count);
    public sealed record RedeliverResponse(int Moved);
}
