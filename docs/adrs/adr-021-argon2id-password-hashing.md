# ADR-021: Hashing de senha com Argon2id substituindo DemoUserStore

**Status:** Aceita *(supersede o store em-memória do [ADR-016](adr-016-jwt-authentication.md))*

## Contexto

A [ADR-016](adr-016-jwt-authentication.md) introduziu JWT Bearer com `DemoUserStore` (senha em texto claro no `appsettings.json`, comparação em tempo constante via `FixedTimeEquals`). Foi explicitamente marcado como **MVP-only**: bom o suficiente para demonstrar o fluxo em ambiente local, mas inaceitável em produção.

Esta ADR evolui o store de demo para um **store real em Postgres** com hash Argon2id e seed pós-migration do usuário demo (`carlos`).

### Por que Argon2id

| Algoritmo | Veredicto |
|---|---|
| **Argon2id** | ✅ Vencedor da Password Hashing Competition (2015); recomendado pelo OWASP Password Storage Cheat Sheet. Resistente a GPU + side-channel. |
| bcrypt | OK como alternativa, mas limite de 72 bytes em senhas e parâmetros menos auditáveis |
| PBKDF2-SHA512 | Aceitável (FIPS 140); fraqueza relativa contra GPU comparado a Argon2id |
| scrypt | Aceitável; menos adotado no .NET |
| SHA-256 / SHA-512 sozinhos | ❌ Não-iterativos; bilhões de hashes/s em GPU |

Konscious.Security.Cryptography.Argon2 (MIT, ~1.3M downloads) é a implementação .NET canônica.

## Decisão

Substituir `DemoUserStore` por uma stack real de autenticação:

| Camada | Componente |
|---|---|
| `Domain/Entities/AppUser.cs` | Rich Domain entity (Id, Username, PasswordHash, Role, IsActive, CreatedAt, LastLoginAt) com factory `Create()` + reidratação `Rehydrate()` |
| `Application/Auth/IPasswordHasher.cs` + `Argon2idPasswordHasher.cs` | Interface + impl Argon2id com formato PHC (`$argon2id$v=19$m=65536,t=3,p=1$<salt>$<hash>`) |
| `Application/Auth/IAuthenticationService.cs` + impl | Orquestra busca → verify → `RecordLogin` → persistir last_login_at |
| `Infrastructure/Repositories/IAppUserRepository.cs` + `AppUserRepository.cs` | Dapper SQL parameterizado em `transactions.app_users` |
| `Infrastructure/Migrations/Scripts/003_create_app_users_table.sql` | Tabela + índice + unique constraint |
| `Infrastructure/Auth/DemoUserSeeder.cs` | Seed idempotente do usuário demo no startup |

### Parâmetros Argon2id (OWASP defaults)

| Parâmetro | Valor | Justificativa |
|---|---|---|
| Memory size | 64 MiB (65536 KB) | OWASP recomenda ≥ 19 MiB; 64 MiB é o "default seguro" mais comum |
| Iterations | 3 | OWASP min |
| Parallelism | 1 | Single thread — suficiente para uma API que faz hash a cada login |
| Salt size | 16 bytes | OWASP min |
| Hash size | 32 bytes | OWASP min |
| Version | 0x13 (v1.3) | Latest |

Custo por hash em hardware moderno: ~250 ms. Aceitável para login; inviável para brute-force.

### Formato PHC armazenado

```
$argon2id$v=19$m=65536,t=3,p=1$<salt-b64-sem-padding>$<hash-b64-sem-padding>
```

Auto-descritivo — permite tunar `m`/`t`/`p` no futuro sem migração de schema (basta re-hashear no próximo login).

### Seed demo

`DemoUserSeeder.EnsureSeededAsync` roda após migrations no startup:
- Verifica se `carlos` existe → se sim, skip
- Hasheia `S3cret!ChangeMe` → insere em `transactions.app_users`

Idempotente. Marcado com `LogWarning("⚠️ Demo only — remover em produção")` para deixar claro.

### Prevenção contra user enumeration

`AuthenticationService.AuthenticateAsync` retorna `null` em **qualquer** falha (usuário inexistente, hash inválido, inativo) — sem distinguir. Logs internos têm a razão real (para troubleshooting); a resposta HTTP é sempre `401 Unauthorized` com a mesma mensagem.

## Trade-offs

| Ganha | Perde |
|---|---|
| Senha nunca persistida em claro — vazamento do DB ≠ vazamento de senhas | +~50ms de custo no startup do hash do seed demo |
| Login real (lookup + verify) em vez de comparação em memória | Login agora exige Postgres up — diferente do DemoUserStore que era 100% in-memory |
| Formato PHC permite evolução de parâmetros sem migração | Konscious.Argon2 não é mantido pela Microsoft — risco baixo (MIT, popular) |
| `last_login_at` registrado — base para detectar contas dormentes | Sem hash de salt extra (pepper) — pode ser evolução futura via Key Vault |
| Sem user enumeration — sondagem de "este user existe?" não funciona | Mensagem genérica frustra UX legítima (mitigado por logs internos detalhados) |

## Evolução natural

| Item | Quando |
|---|---|
| **Pepper** (chave secreta extra concatenada ao password antes do hash) | Quando houver Key Vault — separa proteção em camadas |
| **Lockout / rate limit por usuário** (após N tentativas) | Antes do primeiro user real em produção |
| **Reset de senha por email** | Quando email/SMTP existir |
| **MFA (TOTP RFC 6238)** | Para roles privilegiadas |
| **OIDC com IdP externo** (Entra ID, Auth0, Keycloak) | Quando integração corporativa exigir SSO — APIs deixam de armazenar credenciais |
| **Rotação automática de parâmetros Argon2id** | Quando hardware permitir aumentar `m` / `t` sem regressão de UX |

## Validação

- **Unit**: `tests/CashFlow.UnitTests/Transactions/Domain/AppUserTests.cs` (9 testes — Create, Rehydrate, RecordLogin, validações).
- **Mutation**: Stryker.NET cobre `AppUser` no escopo `**/Domain/**/*.cs` ([ADR-020](adr-020-stryker-mutation-testing.md)).
- **E2E**: cenários BDD via `WebApplicationFactory` + Testcontainers Postgres ([ADR-022](adr-022-bdd-e2e-webapplicationfactory.md)) — login válido, senha errada, user inexistente, endpoint protegido com/sem token.

## ADRs relacionadas

- [ADR-016](adr-016-jwt-authentication.md) — JWT Bearer; esta ADR substitui o `DemoUserStore` lá citado.
- [ADR-009](adr-009-rich-domain-model.md) — `AppUser` segue o pattern Rich Domain (factory + private set).
- [ADR-010](adr-010-dapper.md) — `AppUserRepository` usa Dapper parameterizado.
- [ADR-020](adr-020-stryker-mutation-testing.md) — `AppUser` está no escopo de mutação.
- [ADR-022](adr-022-bdd-e2e-webapplicationfactory.md) — fluxo de auth validado E2E.
