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
- **API Gateway (Azure APIM / Apigee)** — para rate limiting distribuído, validação central de JWT, transformação de payload, developer portal. Tópico relevante para a vaga Verx.
- **OpenTelemetry** completo (traces + métricas + logs) com Application Insights — substitui o Serilog/Console quando produção justificar. Propaga `W3C TraceContext` por headers AMQP entre Transactions API → broker → Balance API.
- **Stryker.NET** — testes de mutação para validar qualidade dos testes unitários (complementa ArchTests da [ADR-012](adr-012-architecture-tests.md)).

## ADRs relacionadas

- [ADR-006](adr-006-rate-limiting.md) — rate limiting é parte da defesa em camadas
- [ADR-010](adr-010-dapper.md) — parameterização Dapper é parte da defesa contra SQL injection
- [ADR-012](adr-012-architecture-tests.md) — fitness functions complementam observabilidade da arquitetura
