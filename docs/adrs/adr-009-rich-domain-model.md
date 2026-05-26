# ADR-009: Rich Domain Model (DDD tático) em vez de modelo anêmico

**Status:** Aceita

## Contexto

O desafio explicita *"Padrões Arquiteturais"* e *"Boas práticas (Design Patterns, SOLID)"* como critérios. As duas entidades centrais — `Transaction` (write side) e `DailyBalance` (projeção) — podem ser modeladas de duas formas:

1. **Modelo anêmico**: classes-DTO com propriedades públicas (`get; set;`), toda a lógica em `Services`.
2. **Rich Domain Model**: entidades com construtor privado, factory methods, encapsulamento forte (`private set`), invariantes garantidas dentro da classe (modelo sempre-válido).

Para um domínio de 2 entidades, ambas as abordagens funcionam. A escolha aqui é **demonstrar conhecimento de DDD tático** num contexto onde isso é avaliado.

## Decisão

Adotar Rich Domain Model com os seguintes padrões em `CashFlow.Transactions.API/Domain/` e `CashFlow.Balance.API/Domain/`:

- **Construtor privado + factory method estático**: `Transaction.Register(amount, type, description, movementDate)` retorna instância válida ou lança `DomainException`.
- **Propriedades com `private set`** (ou `init` quando aplicável): mutação só dentro da própria entidade.
- **Métodos de domínio**: comportamento expressa intenção de negócio (`balance.ApplyCredit(amount)` em vez de manipular `TotalCredits`/`TotalDebits` por fora).
- **Value Objects** em `Domain/ValueObjects/`: `Money` (valor + invariantes de positividade e precisão), `TransactionType` (enum encapsulado com regras), `MovementDate` (date-only com regras de "não-futuro").
- **`DomainException`** para violações de invariantes; nunca usar `null` para indicar erro de validação.

## Exemplo de invariante encapsulada

```csharp
public sealed class Transaction : AuditableEntity
{
    public Guid Id { get; private set; }
    public Money Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public MovementDate MovementDate { get; private set; }

    private Transaction() { } // exigência de ORMs/Dapper

    public static Transaction Register(
        decimal amount, TransactionType type, string description, DateOnly movementDate)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Descrição da transação é obrigatória.");
        if (description.Length > 200)
            throw new DomainException("Descrição não pode exceder 200 caracteres.");

        return new Transaction
        {
            Id = Guid.NewGuid(),
            Amount = Money.From(amount),
            Type = type,
            Description = description.Trim(),
            MovementDate = MovementDate.From(movementDate),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

## Trade-offs

| Ganha | Perde |
|---|---|
| Invariantes garantidas no construtor → impossível criar entidade inválida | Mais código por entidade (factories, value objects) vs. POCO simples |
| Application Services finos — orquestram, não validam regras de negócio | Curva: time precisa entender o padrão (vs. POCO trivial) |
| Testabilidade alta — testes de domínio sem mocks de infraestrutura | Mapeamento Dapper exige construtor privado parameterless (resolvido por records de DTO + factory de reconstrução) |
| Demonstra DDD tático, alinhado ao vocabulário da vaga | — |
| Validação centralizada na entidade — sem duplicação em DTOs e Controllers | — |

## Validação por Testes de Arquitetura (ver [ADR-012](adr-012-architecture-tests.md))

- Entidades em `Domain/Entities/` não devem ter setters públicos.
- Entidades em `Domain/Entities/` não devem referenciar `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Dapper`, `Npgsql`.

## Alternativa descartada

**Modelo anêmico** — viável para CRUD trivial, mas o desafio cobra *"Design Patterns / SOLID / DDD"* explicitamente. Demonstrar Rich Domain custa ~50 linhas a mais e gera diferenciação clara em avaliação.

## ADRs relacionadas

- [ADR-010](adr-010-dapper.md) — Dapper combina naturalmente com Rich Domain
- [ADR-012](adr-012-architecture-tests.md) — fitness functions verificam encapsulamento
