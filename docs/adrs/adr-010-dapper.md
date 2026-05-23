# ADR-010: Dapper em vez de Entity Framework Core

**Status:** Aceita

## Contexto

A camada de persistência precisa atender três cenários:

1. Escrever uma `Transaction` na tabela `transactions.transactions`.
2. Fazer upsert da projeção `balance.daily_balance` no consumer.
3. Consulta simples por data ou range na Balance API.

Volume modesto, queries previsíveis. EF Core 8 e Dapper são as duas opções naturais no ecossistema .NET.

## Decisão

Usar **Dapper** como ORM principal, com **DbUp** para migrations versionadas em SQL puro. Repositórios em `Infrastructure/Repositories/` recebem `IDbConnectionFactory` injetado.

## Por que Dapper (e não EF Core)

| Critério | Dapper | EF Core |
|---|---|---|
| Controle sobre SQL gerado | Total (você escreve) | Parcial (LINQ → SQL pode surpreender) |
| Performance em leitura | ~Igual ao ADO.NET raw | ~10–20% mais lento em queries complexas (overhead de change tracking) |
| Curva de aprendizado | Baixa (SQL + classes POCO) | Média-alta (LINQ, lazy loading, conventions, tracking) |
| Migrations | Externa (DbUp/Flyway/Roundhouse) | Built-in (`dotnet ef migrations`) |
| Suporte a Rich Domain | Excelente (sem convenções intrusivas) | Bom, mas exige `HasField`, `Ignore`, configuração extra |
| Tamanho da dependência | ~200KB | ~5MB + Providers |
| Debugging | SQL na cara — trivial | Logging de SQL gerado, EF inserts em batch surpreendem |

## Por que Rich Domain combina com Dapper

EF Core entra em conflito com construtor privado parameterless: precisa de `HasField`, `Ignore`, configuração fluente para Value Objects. Funciona, mas com cerimônia. Dapper aceita `private Transaction() { }` naturalmente e mapeia colunas → propriedades sem reclamar de encapsulamento. Para entidades sempre-válidas (ver [ADR-009](adr-009-rich-domain-model.md)), Dapper é menos intrusivo.

## Trade-offs

| Ganha | Perde |
|---|---|
| Controle total do SQL — performance previsível, plano de execução visível | Migrations não são auto-geradas — precisa escrever SQL incremental |
| Combina naturalmente com Rich Domain (sem fricção de conventions) | Sem change tracking — UoW precisa ser manual (transações explícitas) |
| Dependência leve, sem provider gigante | Sem LINQ — queries dinâmicas exigem `StringBuilder` cuidadoso |
| Onboarding rápido para quem sabe SQL | Sem migration auto-rollback (cuidado com ordem das migrations) |
| Sem surpresa de N+1 — você vê toda query | Time precisa ser disciplinado para escrever SQL seguro (parameterização sempre) |

## Mitigação dos riscos

1. **SQL Injection**: Dapper parameteriza por padrão quando usado corretamente (`@param` em vez de concatenação). Teste de arquitetura ([ADR-012](adr-012-architecture-tests.md)) pode validar ausência de `string.Format`/concatenação em arquivos `*Repository.cs`.
2. **Unit of Work**: implementar `IUnitOfWork` (em `Infrastructure/Persistence/`) que abre `IDbTransaction` no escopo do Application Service. Repositórios recebem a transação via `uow.Transaction`.
3. **Migrations**: DbUp roda automaticamente no startup das APIs (idempotente — só aplica scripts não-aplicados). Scripts em `Infrastructure/Migrations/Scripts/*.sql` marcados como `EmbeddedResource`.

## Configuração concreta

```csharp
// Infrastructure / DI (Program.cs)
services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
services.AddScoped<ITransactionRepository, TransactionRepository>();

// Migrations no startup
MigrationRunner.EnsureUpToDate(connectionString, logger);
```

## Alternativa descartada — EF Core

Escolha legítima e produtiva, especialmente para times grandes e schemas complexos. Foi preterida aqui porque:

1. O desafio premia **demonstração de controle arquitetural** — escrever SQL próprio mostra mais profundidade que delegar para `DbContext`.
2. Rich Domain Model fica mais limpo sem fricção de mapeamento.
3. Volume e complexidade do domínio não exigem o ganho de produtividade do EF.

Em produção com domínio maior e time desenvolvendo em paralelo, EF Core voltaria à mesa.

## ADRs relacionadas

- [ADR-003](adr-003-postgres-schemas.md) — PostgreSQL via Npgsql
- [ADR-007](adr-007-publish-after-commit.md) — UoW manual define o "commit boundary"
- [ADR-009](adr-009-rich-domain-model.md) — Rich Domain sem fricção de ORM
- [ADR-012](adr-012-architecture-tests.md) — fitness functions podem validar parameterização
