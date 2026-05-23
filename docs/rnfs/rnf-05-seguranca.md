# RNF-05 — Segurança

**Origem:** Derivado *(seção "Objetivo do Desafio" do PDF)*.

## Declaração

> "Segurança: Proteja os dados e sistemas contra ameaças. Implemente autenticação, autorização, criptografia e mecanismos de proteção contra ataques."

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-006 — Rate Limiting](../adrs/adr-006-rate-limiting.md) | Mitigação de DoS leve por origem. |
| [ADR-010 — Dapper](../adrs/adr-010-dapper.md) | Parameterização sempre via `@params` — proteção contra SQL injection. |
| [ADR-013 — Versionamento, healthchecks, validação, CORS, HTTPS](../adrs/adr-013-security-observability.md) | Validação estrita no boundary, CORS configurável, HTTPS no compose. |
| [ADR-016 — JWT Bearer + Policy-based Authorization](../adrs/adr-016-jwt-authentication.md) | Autenticação stateless + autorização declarativa via `[Authorize(Policy=...)]`. |
| [ADR-021 — Hashing Argon2id](../adrs/adr-021-argon2id-password-hashing.md) | Senha persistida como hash Argon2id (OWASP defaults); user enumeration mitigado. |
| [ADR-022 — BDD E2E](../adrs/adr-022-bdd-e2e-webapplicationfactory.md) | Fluxo de auth validado ponta-a-ponta via WebApplicationFactory + Testcontainers. |
| [ADR-023 — Account lockout](../adrs/adr-023-account-lockout.md) | 2ª camada contra brute-force: 5 tentativas falhas → trava por 15 min. |
| [ADR-024 — Refresh tokens com rotação](../adrs/adr-024-refresh-tokens-rotation.md) | Access JWT 15 min + refresh opaco 7 dias rotacionado a cada uso; logout revoga. |

## Cobertura no MVP

**Sólido.** O MVP cobre autenticação real (com hash), autorização declarativa, validação e isolamento. O que entregamos:

- ✅ **Autenticação**: JWT Bearer HMAC-SHA256 (access **15 min**), emitido em `POST /api/v1/auth/login`, validado em ambos os APIs ([ADR-016](../adrs/adr-016-jwt-authentication.md)).
- ✅ **Hash de senha**: **Argon2id** (OWASP defaults: 64 MiB, 3 iterações, salt 16 bytes, hash 32 bytes) em `transactions.app_users` ([ADR-021](../adrs/adr-021-argon2id-password-hashing.md)).
- ✅ **Account lockout**: 5 tentativas falhas consecutivas → trava por 15 min ([ADR-023](../adrs/adr-023-account-lockout.md)).
- ✅ **Refresh tokens**: opacos (256 bits random, hash SHA-256 no DB), 7 dias, **rotação a cada uso**. `POST /api/v1/auth/refresh` + `POST /api/v1/auth/logout` ([ADR-024](../adrs/adr-024-refresh-tokens-rotation.md)).
- ✅ **Autorização**: Policy-based (`AuthorizationPolicies.RequireMerchant`) aplicada via `[Authorize]` em controllers protegidos.
- ✅ **Anti user enumeration**: `AuthenticationService` retorna a mesma resposta para "user não existe", "senha errada" e "conta travada".
- ✅ **Validação E2E**: 9 cenários BDD via `WebApplicationFactory` + Testcontainers Postgres cobrem login válido/inválido, JWT + AuthZ, lockout após N tentativas, rotação de refresh, logout ([ADR-022](../adrs/adr-022-bdd-e2e-webapplicationfactory.md)).
- ✅ **SQL Injection**: parameterização Dapper em 100% das queries (`@param`).
- ✅ **Input validation**: FluentValidation antes do handler; `DomainException` para invariantes.
- ✅ **DoS leve**: rate limiting nativo na Balance API.
- ✅ **Princípio do menor privilégio (banco)**: `app_transactions` e `app_balance` com GRANTs restritos ao próprio schema (ver [ADR-003](../adrs/adr-003-postgres-schemas.md)).
- ✅ **Versionamento de API** (`/api/v1/`) — evita breaking changes futuros.
- ✅ **CORS configurável** em `appsettings.json`.
- ⚠️ **HTTPS**: certificados de dev no Dockerfile; em produção, terminação TLS no API Gateway / Ingress.
- ⚠️ **Symmetric key JWT** compartilhada entre os APIs — adequada ao MVP; produção exige RSA/ECDSA + JWKS.

## Trade-off aceito

Symmetric key compartilhada entre Transactions e Balance é aceitável no escopo isolado do desafio (1 sistema, 2 serviços do mesmo dono). Em produção, **chave assimétrica RSA/ECDSA + JWKS endpoint + OIDC com Microsoft Entra ID** é o caminho documentado em [ADR-016](../adrs/adr-016-jwt-authentication.md) § Evolução.

## Verificação

- **Autenticação:** `curl http://localhost:5002/api/v1/balance/2026-05-23` sem header `Authorization` → deve responder `401 Unauthorized` (provado por cenário BDD E2E).
- **Autorização:** token válido sem role `Merchant` → deve responder `403 Forbidden`.
- **Login:** `POST /api/v1/auth/login` com credenciais válidas (`carlos` / `S3cret!ChangeMe`) → retorna `accessToken`, `refreshToken` e role `Merchant` (provado por cenário BDD E2E).
- **Hash:** consulta direta em `transactions.app_users` mostra `password_hash` com prefixo `$argon2id$v=19$m=65536,t=3,p=1$...` — nunca texto claro.
- **Lockout:** após 5 logins falhos consecutivos, mesmo a senha correta retorna 401 (provado por cenário BDD `@lockout`).
- **Refresh rotativo:** `POST /api/v1/auth/refresh` retorna par novo (access + refresh); chamar de novo com o refresh original retorna 401 (provado por cenário BDD `@refresh`).
- **Logout:** `POST /api/v1/auth/logout` retorna 204; refresh subsequente retorna 401 (provado por cenário BDD `@refresh`).
- **Anti user enumeration:** login com `usuario-inexistente` retorna mesma resposta de `senha-errada` (provado por cenário BDD E2E).
- **SQL Injection:** tentar injetar `'; DROP TABLE x; --` no campo `description` de um POST → deve ser persistido literalmente (parameterizado) ou rejeitado pela validação.
- **Rate limiting:** já coberto em [RNF-02](rnf-02-carga.md).
- **CORS:** com origem não-listada, browser deve bloquear pré-flight.
- **GRANTs:** conectar como `app_balance` e tentar `SELECT * FROM transactions.transactions` → deve falhar com permissão negada.

## Evolução

- **Pepper** (chave secreta extra concatenada ao password antes do hash) via Key Vault.
- **Rate limit por IP no `/auth/login`** — complementa lockout por user.
- **Reuse detection cascade** — refresh já revogado → revoga todos os tokens do user (suspeita de roubo).
- **Refresh em HttpOnly + Secure cookie** (em vez de body de response) quando houver frontend.
- **OAuth 2.0 / OIDC** via Microsoft Entra ID (`Microsoft.Identity.Web`) — APIs só validam JWT do IdP.
- **Chaves assimétricas** (RSA/ECDSA) + JWKS endpoint (`.well-known/jwks.json`).
- **MFA (TOTP RFC 6238)** para roles privilegiadas.
- **API Gateway (Azure APIM / Apigee)** para validação centralizada de JWT, IP filtering, mTLS.
- **Key Vault + Managed Identity** para secrets — eliminar chave/senhas em `appsettings.json`.
- **Azure Defender for Cloud** para vulnerability scanning contínuo.
- **OWASP ASVS** como baseline de hardening.
