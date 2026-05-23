# ADR-006: Rate limiting nativo do ASP.NET Core

**Status:** Aceita

## Contexto

O **RNF-02** define 50 req/s como pico no serviço Balance. É necessário proteger o serviço contra sobrecarga, mantendo a perda controlada abaixo de 5%. Uma opção seria usar um API Gateway externo (Azure API Management), mas isso adiciona um componente que não roda localmente em Docker.

## Decisão

Usar o middleware nativo `Microsoft.AspNetCore.RateLimiting` (disponível desde .NET 8; em uso aqui sobre .NET 10) com política de **fixed window** na `CashFlow.Balance.API`, associada ao `BalanceController` via `[EnableRateLimiting("balance")]`.

## Configuração concreta

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("balance", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(1);
        opt.PermitLimit = 50;
        opt.QueueLimit = 5;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";
        await ctx.HttpContext.Response.WriteAsync(
            "Rate limit excedido. Tente novamente em 1 segundo.", ct);
    };
});
```

## Como isso atende o RNF-02

| Cenário | Comportamento |
|---|---|
| ≤50 req/s | Todas atendidas normalmente |
| 51-55 req/s | 5 entram na fila, processadas no próximo segundo |
| >55 req/s | Excedentes recebem HTTP 429 com `Retry-After` header |

Com a fila de 5 (`QueueLimit`), picos curtos de até 55 req/s são absorvidos. Acima disso, a rejeição é explícita e controlada — o cliente sabe que precisa esperar, e a perda nunca excede o que ultrapassa 55/s, bem dentro do teto de 5%.

## Trade-offs

| Ganha | Perde |
|---|---|
| Zero dependência externa — roda em qualquer ambiente | Rate limiting per-instance (não distribuído entre réplicas) |
| Configuração simples, testável | Em produção com múltiplas instâncias, precisaria de rate limiting distribuído (Redis + sliding window) |
| Atende RNF-02 para o cenário do desafio (instância única) | Não substitui um API Gateway completo (sem roteamento, sem auth centralizada) |
| Resposta padrão HTTP 429 com `Retry-After` | — |

## Alternativa descartada

**Azure API Management** — resolve rate limiting, auth e roteamento, mas é um serviço gerenciado que custa ~US$300/mês e não roda em Docker. Para um desafio que precisa funcionar com `docker compose up`, é over-engineering. Documentado como evolução futura.

## ADRs relacionadas

- [ADR-013](adr-013-security-observability.md) — APIM/Apigee como evolução para rate limit distribuído
