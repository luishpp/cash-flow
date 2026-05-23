# Requisitos Não-Funcionais (RNFs)

Cada RNF deste projeto é registrado como documento próprio, com **origem**, **declaração**, **decisões arquiteturais que o atendem**, **distinção entre MVP e Evolução**, e **forma de verificação**.

> **Princípio:** o enunciado declara dois RNFs **explícitos** (disponibilidade e carga). A seção *Objetivo do Desafio* do PDF também cobra sete dimensões adicionais que tratamos como RNFs derivados. Toda decisão arquitetural rastreia-se de volta para pelo menos um RNF abaixo.

## Índice das RNFs

| ID | Dimensão | Origem | Cobertura no MVP |
|---|---|---|---|
| [RNF-01](rnf-01-disponibilidade.md) | Disponibilidade | Explícito | Sim |
| [RNF-02](rnf-02-carga.md) | Carga (50 req/s, máx 5% perda) | Explícito | Sim |
| [RNF-03](rnf-03-escalabilidade.md) | Escalabilidade | Derivado (Objetivo) | Parcial — stateless + projeção |
| [RNF-04](rnf-04-resiliencia.md) | Resiliência | Derivado (Objetivo) | Sim |
| [RNF-05](rnf-05-seguranca.md) | Segurança | Derivado (Objetivo) | Mínimo honesto |
| [RNF-06](rnf-06-padroes-arquiteturais.md) | Padrões Arquiteturais | Derivado (Objetivo) | Sim |
| [RNF-07](rnf-07-integracao.md) | Integração | Derivado (Objetivo) | Sim |
| [RNF-08](rnf-08-manutenibilidade.md) | Manutenibilidade | Derivado (Objetivo) | Sim |
| [RNF-09](rnf-09-observabilidade.md) | Observabilidade | Derivado (Objetivo) | Mínimo (logs + healthchecks) |

## Rastreabilidade: RNF → ADRs

| RNF | ADRs que atendem |
|---|---|
| [RNF-01](rnf-01-disponibilidade.md) | [001](../adrs/adr-001-cqrs.md), [002](../adrs/adr-002-rabbitmq-masstransit.md), [003](../adrs/adr-003-postgres-schemas.md), [004](../adrs/adr-004-consumer-hostedservice.md) |
| [RNF-02](rnf-02-carga.md) | [005](../adrs/adr-005-polly-retry.md), [006](../adrs/adr-006-rate-limiting.md) |
| [RNF-03](rnf-03-escalabilidade.md) | [001](../adrs/adr-001-cqrs.md), [004](../adrs/adr-004-consumer-hostedservice.md), [006](../adrs/adr-006-rate-limiting.md) |
| [RNF-04](rnf-04-resiliencia.md) | [002](../adrs/adr-002-rabbitmq-masstransit.md), [005](../adrs/adr-005-polly-retry.md), [007](../adrs/adr-007-publish-after-commit.md), [011](../adrs/adr-011-idempotency.md) |
| [RNF-05](rnf-05-seguranca.md) | [006](../adrs/adr-006-rate-limiting.md), [010](../adrs/adr-010-dapper.md), [013](../adrs/adr-013-security-observability.md) |
| [RNF-06](rnf-06-padroes-arquiteturais.md) | [001](../adrs/adr-001-cqrs.md), [009](../adrs/adr-009-rich-domain-model.md) |
| [RNF-07](rnf-07-integracao.md) | [002](../adrs/adr-002-rabbitmq-masstransit.md) |
| [RNF-08](rnf-08-manutenibilidade.md) | [009](../adrs/adr-009-rich-domain-model.md), [012](../adrs/adr-012-architecture-tests.md) |
| [RNF-09](rnf-09-observabilidade.md) | [013](../adrs/adr-013-security-observability.md) |

## Distinção MVP vs Evolução

O escopo do desafio é construir uma solução executável localmente. Tudo marcado como "Evolução" nos arquivos individuais é citado como caminho natural em produção (Azure), demonstrando consciência arquitetural sem inflar o escopo entregável.
