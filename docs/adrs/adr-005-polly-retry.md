# ADR-005: Polly Retry no consumer (Circuit Breaker e DLQ como evolução)

**Status:** Aceita *(revisada — versão anterior incluía CB e DLQ no MVP)*

## Contexto

O **RNF-02** define no máximo 5% de perda de requisições no Balance. No consumer, falhas transitórias (banco temporariamente indisponível, timeout de rede) podem causar perda de mensagens se não forem tratadas. A pergunta é: qual o **mínimo de resiliência** suficiente para o escopo do desafio sem inflar a superfície de teste e manutenção?

## Decisão

Para o MVP, implementar **apenas Retry com exponential backoff** via **Polly v8** no `TransactionConsumer`. Circuit Breaker e Dead Letter Queue são citados como evolução natural — implementados quando houver telemetria que justifique (taxa de falha real, padrão de cascata observado).

## Por que apenas Retry no MVP

| Cenário | Frequência esperada no escopo local | Resolvido por |
|---|---|---|
| Timeout transitório (rede Docker, banco contended) | Real, mas raro | **Retry com backoff** — suficiente |
| Banco fora por minutos | Não acontece em ambiente local rodando localmente | Circuit Breaker — evolução |
| Mensagem malformada (bug) | Possível em dev, raro | Validação no consumer + DLQ — evolução |

Implementar CB + DLQ desde o MVP gera complexidade que não tem como ser **demonstrada** em ambiente local de avaliação: não há cenário onde o banco fica fora por minutos para o CB abrir; não há mensagem malformada chegando da produção para validar a DLQ. Polly Retry resolve o caso prático (jitter de rede no Docker) com configuração de 10 linhas.

## Configuração concreta (MVP)

```csharp
// Polly v8 — pipeline de retry registrado no Program.cs e usado pelo TransactionConsumer
services.AddResiliencePipeline("consumer-pipeline", pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    });
});
```

O `TransactionConsumer` injeta `ResiliencePipelineProvider<string>` e envolve `ConsolidationService.ApplyAsync(...)` com `pipeline.ExecuteAsync`.

## Trade-offs do MVP

| Ganha | Perde |
|---|---|
| Tolerância a falhas transitórias (jitter de rede, contenção de banco) | Mensagens com falha persistente vão para a fila de redelivery do RabbitMQ até intervenção manual |
| Configuração simples e testável (mock de exception) | Sem CB: em cenário extremo de banco fora, retentativas ficariam em loop |
| Atende RNF-02 no comportamento esperado para 50 req/s | Sem DLQ explícita: mensagens "venenosas" precisam de inspeção manual no broker |
| Mantém o consumer enxuto | — |

## Evolução documentada (não-MVP)

1. **Circuit Breaker** — adicionar quando houver telemetria mostrando taxa de falha sustentada ou cascatas reais. Configuração de referência:

   ```csharp
   pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions
   {
       FailureRatio = 0.5,
       MinimumThroughput = 5,
       SamplingDuration = TimeSpan.FromSeconds(30),
       BreakDuration = TimeSpan.FromSeconds(30)
   });
   ```

2. **Dead Letter Queue** — habilitar via MassTransit (`endpointConfigurator.UseMessageRetry()` + `endpointConfigurator.ConfigureDeadLetterQueue()`). Requer rotina de inspeção/reprocessamento que não cabe no escopo de avaliação.

3. **Idempotência no consumer** — pré-requisito para retry seguro. Implementado no MVP via [ADR-011](adr-011-idempotency.md) (não é evolução, é base do Retry funcionar corretamente).

## Alternativa descartada

**Retry manual com `try/catch` + `Task.Delay`** — funciona, mas não oferece backoff exponencial com jitter padronizado, não compõe com outras políticas, e é propenso a erros (loop infinito acidental se a condição de saída estiver errada). Polly resolve isso com configuração declarativa.

## ADRs relacionadas

- [ADR-002](adr-002-rabbitmq-masstransit.md) — broker que faz redelivery em caso de Nack
- [ADR-004](adr-004-consumer-hostedservice.md) — consumer que executa dentro da pipeline
- [ADR-011](adr-011-idempotency.md) — pré-requisito para retry seguro com at-least-once
