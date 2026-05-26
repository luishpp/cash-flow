# ADR-001: Adotar CQRS para separar Transactions e Balance

**Status:** Aceita

## Contexto

O desafio pede dois serviços: um para controle de lançamentos (Transactions) e outro de consolidado diário (Balance). O **RNF-01** exige que a queda do Balance não afete o Transactions. Isso implica que os dois não podem compartilhar o mesmo processo, a mesma transação ou a mesma dependência crítica.

## Decisão

Adotar **CQRS (Command Query Responsibility Segregation)**:

- **Command side** → `CashFlow.Transactions.API` (escrita: registra débitos e créditos)
- **Query side** → `CashFlow.Balance.API` (leitura: expõe saldo diário consolidado)

O saldo consolidado (`DailyBalance`) é uma **projeção** derivada dos eventos de escrita (`TransactionRegistered`), não uma consulta direta ao mesmo banco.

## Trade-offs

| Ganha | Perde |
|---|---|
| Independência total de deploy e ciclo de vida entre os serviços | Consistência eventual — o saldo pode estar alguns segundos defasado |
| Modelo de leitura otimizado para consulta (desnormalizado, O(1)) | Duplicação de dados entre write store e read store |
| Escala independente — pode ter N instâncias do Balance sem afetar Transactions | Complexidade de sincronização via eventos |
| Atende RNF-01 diretamente | Mais componentes para operar (2 APIs + consumer vs. 1 API monolítica) |

## Por que a consistência eventual é aceitável aqui

O Balance é uma consulta de saldo — não é uma transferência bancária entre contas. Um atraso de 2-3 segundos entre o registro do lançamento e a atualização do saldo consolidado não causa impacto operacional para o comerciante. Se fosse um sistema de pagamentos em tempo real, essa decisão seria diferente.

## Read surface exposto pelo Balance.API

O desafio pede **"relatório que disponibilize o saldo diário consolidado"** — sem prescrever shape específico de API. Mapeamos isso em dois endpoints, cobrindo as duas formas que o comerciante (Carlos) consulta o consolidado na jornada documentada em [`../analysis/analise-desafio-arquiteto.md`](../analysis/analise-desafio-arquiteto.md):

| Endpoint | Retorna | Jornada coberta |
| --- | --- | --- |
| `GET /api/v1/balance/{date}` | Saldo consolidado de um dia (`BalanceResponse` — zerado se não houver movimento) | "Olhada rápida" e "Fechamento do dia" |
| `GET /api/v1/balance?from=X&to=Y` | `BalancePeriodResponse`: totais agregados (`totalCredits`/`totalDebits`/`balance`) no topo + lista diária em `days[]` | "Histórico / preparação para contador" |

### Por que o endpoint de período devolve totais no envelope (e não num `/summary` separado)

O endpoint de período tem dois usos típicos — ver a lista detalhada e ver o total do intervalo. As alternativas avaliadas:

- **Endpoint separado `/balance/summary`** — descartada: "summary" em cima de "Balance" (que já significa "Consolidado" no bounded context — ver [Mapeamento de termos](../analysis/analise-desafio-arquiteto.md)) duplica semântica. Pior, inventa um conceito ("consolidado de período") que o desafio não pede; o desafio fala em "saldo **diário** consolidado".
- **Apenas o array, cliente soma** — descartada: cada cliente repete a soma; risco trivial de divergência.
- **Envelope com totais no topo + `days[]` aninhado** ✅ — escolhida. O topo espelha o shape de `BalanceResponse` (`totalCredits`/`totalDebits`/`balance`), o que comunica "este é o mesmo objeto consolidado, em escala de período"; cliente pega o total em uma leitura.

Agregação roda **em memória** sobre o resultado do `ListByPeriodAsync` — sem nova query SQL, sem duplicar lógica. Para os tamanhos de período esperados (semanas/meses), o overhead vs. um `SUM` no banco é desprezível e mantém uma única fonte de verdade da query.

## Alternativa descartada

**Serviço único com consolidação síncrona** — simples de implementar, mas viola RNF-01: se a lógica de consolidação falhar, o lançamento falha junto.

## ADRs relacionadas

- [ADR-002](adr-002-rabbitmq-masstransit.md) — mensageria entre write e read sides
- [ADR-003](adr-003-postgres-schemas.md) — separação de schemas para isolar a camada de dados
- [ADR-004](adr-004-consumer-hostedservice.md) — onde roda o consumer da projeção
- [ADR-009](adr-009-rich-domain-model.md) — modelo rico nas entidades de ambos os lados
