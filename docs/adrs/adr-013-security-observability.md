# ADR-013: Versionamento de API e Health Checks (Segurança e Observabilidade básicas)

**Status:** Aceita

## Contexto

Os **RNF-05 (Segurança)** e **RNF-09 (Observabilidade)** são objetivos do desafio mesmo que não tenham métricas explícitas. Para o MVP, o mínimo "honesto" sem inflar escopo inclui: prefixar endpoints com versão (`/api/v1/...`), expor health checks padronizados, validar input estrito, configurar CORS com origens declaradas, e configurar HTTPS no compose. Autenticação OAuth/JWT é citada como evolução natural — não cabe no escopo do desafio onde não há identidade definida.

## Decisão

1. **Versionamento**: prefixo `/api/v1/` em todos os endpoints (`Microsoft.AspNetCore.Mvc.Versioning` opcional para o MVP — prefixo é suficiente).
2. **Health Checks**: dois endpoints (`/health/live` e `/health/ready`) via `Microsoft.Extensions.Diagnostics.HealthChecks`. `Live` valida que o processo respira; `Ready` valida dependências (Postgres).
3. **Validação de input**: FluentValidation com `IValidator<T>` por endpoint. DTOs entram pelo modelo binding; validação roda antes do handler.
4. **CORS**: política configurável em `appsettings.json` (vazio no MVP, fácil de adicionar).
5. **HTTPS**: certificados de dev configurados no Dockerfile; em produção, terminação TLS no API Gateway/Ingress.
6. **Logs estruturados**: Serilog com enricher de `Service` (propriedade do componente).

## Trade-offs

| Ganha | Perde |
|---|---|
| Atende RNF-05 e RNF-09 no nível mínimo honesto | Sem autenticação no MVP — limitação assumida |
| Health checks permitem readiness probes em K8s/Compose | Mais 1-2 dependências (HealthChecks, FluentValidation, Serilog) |
| Validação no endpoint evita lixo chegar no domínio | — |
| Versionamento desde o dia 1 evita breaking changes futuros | — |

## Evolução documentada

- **OAuth 2.0 / OIDC** via Microsoft Entra ID (`Microsoft.Identity.Web`) — implementado quando houver identidade definida (web/mobile/integrações).
- **API Gateway (Azure APIM / Apigee)** — para rate limiting distribuído, validação central de JWT, transformação de payload, developer portal. Tópico relevante para a vaga.
- **OpenTelemetry** completo (traces + métricas + logs) com Application Insights — substitui o Serilog/Console quando produção justificar. Propaga `W3C TraceContext` por headers AMQP entre Transactions API → broker → Balance API.
- **Stryker.NET** — testes de mutação para validar qualidade dos testes unitários (complementa ArchTests da [ADR-012](adr-012-architecture-tests.md)).

## Observabilidade em produção: dos logs aos três pilares

> Ponto levantado em avaliação técnica: *"para o contexto do teste atende, mas ficou em nível básico. Em sistemas distribuídos, rastreamento distribuído, métricas por endpoint, correlação entre serviços, profundidade de fila, latência e falhas por componente fazem muita diferença na operação."* O MVP entrega o pilar de **logs** (Serilog estruturado) + health probes; os outros dois pilares são o degrau seguinte.

| Pilar | Estado no MVP | Evolução para produção |
|---|---|---|
| **Logs** | Serilog estruturado + enricher `Service` | OK como base; correlacionar com trace ID |
| **Traces** | Ausente | **OpenTelemetry + W3C TraceContext** propagado pelo broker. O `traceparent` é injetado nos headers AMQP na publicação e extraído no consumer — MassTransit tem instrumentação OTel nativa que faz isso. É o item de maior impacto operacional |
| **Métricas** | Health probes apenas | **RED por endpoint** (Rate, Errors, Duration), com latência em **P95/P99** (não média), via `System.Diagnostics.Metrics` + OTel → Prometheus/Grafana ou Application Insights |

**Métricas específicas do nosso desenho que viram obrigatórias em produção:**

- **Profundidade de fila + idade da mensagem mais antiga.** O consumer roda FIFO deliberado (SAC + `ConcurrentMessageLimit=1` + `PrefetchCount=1` — ver [ADR-004](adr-004-consumer-hostedservice.md)/[ADR-025](adr-025-outbox-and-dlq.md)). Esse desenho sacrifica throughput por ordem; **a profundidade de fila é justamente a métrica que diz se esse trade-off está machucando** — fila crescendo = o consumer single-threaded não está dando conta.
- **DLQ depth como alerta de primeira classe.** O endpoint admin de redelivery da [ADR-025](adr-025-outbox-and-dlq.md) já existe; em produção, mensagem caindo na `*_error` deve disparar alerta, não ser descoberta por acaso.
- **Latência e taxa de falha por componente** (API write, dispatcher de outbox, consumer) — para localizar o gargalo no fluxo `API → outbox → broker → consumer`.

**Por que tracing distribuído importa neste fluxo especificamente:** sem ele, não há como responder *"esta request de transação gerou qual mensagem, consumida quando, e falhou onde?"* — o contexto se perde na fronteira do broker. Propagar o `traceparent` pelo AMQP é o que costura o span da API ao span do consumer num único trace.

## ADRs relacionadas

- [ADR-006](adr-006-rate-limiting.md) — rate limiting é parte da defesa em camadas
- [ADR-010](adr-010-dapper.md) — parameterização Dapper é parte da defesa contra SQL injection
- [ADR-012](adr-012-architecture-tests.md) — fitness functions complementam observabilidade da arquitetura
