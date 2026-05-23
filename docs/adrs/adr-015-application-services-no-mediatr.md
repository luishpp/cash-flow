# ADR-015: Application Services como dispatcher (sem MediatR)

**Status:** Aceita

## Contexto

A vaga ([`../references/vaga-verx.md`](../references/vaga-verx.md)) cita Mediator entre os padrões aceitáveis. A implementação canônica em .NET é a biblioteca **MediatR**, que adota o pattern **Request/Handler** com pipeline (`IRequest<T>`, `IRequestHandler<TReq, TRes>`, `IPipelineBehavior<T,R>`).

Dois fatores tornam essa opção problemática neste momento:

1. **Licenciamento**: a partir da v12 (2024-12), **MediatR e MassTransit passaram a ter versões comerciais pagas** para uso em produção. Adicionar dependência com modelo de licenciamento ainda em estabilização é dívida técnica desnecessária quando há alternativa nativa.
2. **Custo×benefício no escopo**: o domínio tem **2 casos de uso** (`RegisterTransaction`, `GetBalance`). Introduzir uma biblioteca para 2 handlers + 2 requests é classic over-engineering — adiciona uma dependência externa, uma camada de indireção e zero comportamento que não seja resolvido por DI nativa do .NET.

## Decisão

Adotar o pattern **Application Services + DI direta** como mecanismo de dispatch:

```
Controller ──▶ IXxxService (Application) ──▶ Domain + Infrastructure
```

- Cada *use case* vira um método em um `IXxxService` (ex.: `ITransactionService.RegisterAsync(...)`, `IBalanceQueryService.GetByDateAsync(...)`).
- A implementação é resolvida via `IServiceCollection.AddScoped<IXxxService, XxxService>()`.
- Cross-cutting concerns (logging, validação, transação) ficam em:
  - **Middleware** ASP.NET Core (logs, exception handling) — ver [ADR-013](adr-013-security-observability.md).
  - **FluentValidation auto-validation** (input) — ver [ADR-013](adr-013-security-observability.md).
  - **`IUnitOfWork`** explícito nos services (transação Dapper) — ver [ADR-010](adr-010-dapper.md) e [ADR-011](adr-011-idempotency.md).

### Exemplo (já implementado)

```csharp
// CashFlow.Transactions.API/Application/Services/TransactionService.cs
public sealed class TransactionService : ITransactionService
{
    private readonly ITransactionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IEventPublisher _publisher;

    public async Task<Guid> RegisterAsync(RegisterTransactionRequest req, CancellationToken ct)
    {
        var transaction = Transaction.Register(req.Amount, req.Type, req.Description, req.MovementDate);

        await _uow.BeginAsync(ct);
        try
        {
            await _repo.AddAsync(transaction, ct);
            await _uow.CommitAsync(ct);
        }
        catch { await _uow.RollbackAsync(ct); throw; }

        // Publica após commit (ver ADR-007)
        await _publisher.PublishAsync(new TransactionRegistered(...), ct);
        return transaction.Id;
    }
}
```

```csharp
// Program.cs
builder.Services.AddScoped<ITransactionService, TransactionService>();
```

```csharp
// TransactionsController.cs
[HttpPost]
public async Task<IActionResult> Register(
    [FromBody] RegisterTransactionRequest req, CancellationToken ct)
{
    var id = await _service.RegisterAsync(req, ct);
    return Accepted(new { id });
}
```

A "mediação" entre intent e implementação está no **container de DI** — que **é** um mediator de dependências. Para 2 casos de uso, não há ganho em embrulhar isso em outra abstração.

## Trade-offs

| Ganha | Perde |
|---|---|
| Zero dependência externa para algo que DI nativa já resolve | Sem pipeline de behaviors built-in (logging/validation/transação ficam em middleware/aspectos próprios) |
| Sem questão de licenciamento (MediatR v12+ é pago em produção) | Sem padronização cross-handler "todo handler retorna `Result<T>`" — controlado por convenção |
| Stack-trace honesto: `Controller → Service → Repository`, sem reflection no meio | Se o número de handlers crescer para >20, vale revisitar (ver "Quando re-avaliar") |
| Onboarding: dev novo entende o fluxo em 5 minutos sem aprender abstração nova | — |

## Padrões aplicados (mapeamento)

| Padrão da vaga (vaga-verx.md § "padrões e referências") | Onde no código |
|---|---|
| **Dependency Injection** | `Program.cs` registra services/repositories/UoW |
| **Inversion of Control** | Controllers dependem de **interfaces** (`ITransactionService`, `IBalanceQueryService`) — implementação trocável em testes |
| **Façade** | `IXxxService` esconde a orquestração entre domínio, repositório e mensageria atrás de uma API simples |
| **Unit of Work** | `IUnitOfWork` ([ADR-010](adr-010-dapper.md)) coordena transação Dapper |
| **Mock object** | Testes unitários usam Moq sobre `IXxxService` e `IXxxRepository` |

## Alternativas descartadas

### MediatR
- **Por quê não:** v12+ pago; introduz reflection e pipeline para um problema que não temos (2 handlers); equipe perde stack-trace claro.
- **Quando reconsiderar:** se a contagem de handlers passar de ~20 **E** houver demanda real por cross-cutting pipeline (auditoria automática, retry declarativo). Para esse cenário, a alternativa preferida seria um **dispatcher próprio leve** (ver abaixo) antes de pagar licença.

### Custom Request Dispatcher (in-house)
Implementação mínima (~30 linhas) seria:

```csharp
public interface IRequestHandler<in TReq, TRes>
{
    Task<TRes> HandleAsync(TReq request, CancellationToken ct);
}

public interface IRequestDispatcher
{
    Task<TRes> SendAsync<TReq, TRes>(TReq request, CancellationToken ct);
}

public sealed class RequestDispatcher(IServiceProvider sp) : IRequestDispatcher
{
    public Task<TRes> SendAsync<TReq, TRes>(TReq request, CancellationToken ct)
        => sp.GetRequiredService<IRequestHandler<TReq, TRes>>().HandleAsync(request, ct);
}
```

- **Por quê não agora:** o ganho prático para 2 use cases é zero; adicionar essa indireção custa clareza sem reduzir código. Documentado aqui como **caminho de evolução** se a contagem de handlers crescer.

### Notificações de domínio via `INotifier` / event bus interno
- **Por quê não:** já há mensageria assíncrona (RabbitMQ + MassTransit, [ADR-002](adr-002-rabbitmq-masstransit.md)) para eventos cross-bounded-context. Eventos intra-processo não são necessários no escopo.

## Quando re-avaliar esta decisão

- Handlers passarem de ~20 e cross-cutting começar a aparecer copiado em vários services.
- Necessidade de pipeline declarativo (ex.: `[Audit]`, `[Retry]` por atributo).
- Adoção de Source Generators para dispatch tipado (alternativa moderna ao reflection do MediatR — pode ser construída in-house).

## ADRs relacionadas

- [ADR-001](adr-001-cqrs.md) — CQRS define os dois "lados"; services são o ponto de entrada de cada lado.
- [ADR-009](adr-009-rich-domain-model.md) — Rich Domain mantém services finos (orquestradores).
- [ADR-010](adr-010-dapper.md) — `IUnitOfWork` é a camada onde os services coordenam transação.
- [ADR-013](adr-013-security-observability.md) — middleware ASP.NET Core cobre cross-cutting (logs, exceções, validação).
