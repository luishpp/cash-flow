# ADR-016: Autenticação JWT Bearer com store demo, autorização por policy

**Status:** Aceita

## Contexto

O enunciado do desafio (seção *"Objetivo do Desafio"*) cobra explicitamente *"Implementar autenticação, autorização, criptografia e mecanismos de proteção contra ataques"*. A versão inicial do MVP cobria validação de input, parameterização SQL, CORS, HTTPS e rate-limiting — mas **nenhuma autenticação/autorização**, o que deixava o atributo de segurança claramente incompleto frente ao enunciado.

A escolha por **JWT Bearer + Policy-based Authorization** apoia-se em três pilares: (1) é a stack nativa do ASP.NET Core (sem dependências externas além do `Microsoft.AspNetCore.Authentication.JwtBearer`), (2) gera tokens stateless adequados para CQRS com dois APIs independentes, e (3) policies declarativas via `[Authorize(Policy = ...)]` mantêm controllers limpos.

## Decisão

Implementar **JWT Bearer Authentication** com **Policy-based Authorization** em ambos os APIs (Transactions e Balance), com a Transactions API hospedando o endpoint `/api/v1/auth/login` que emite os tokens. Mesma chave/audience é configurada nos dois APIs — token emitido em um é aceito no outro.

### Componentes (em `CashFlow.Shared/Security/`)

| Arquivo | Responsabilidade |
|---|---|
| `JwtSettings.cs` | Configuração (Issuer, Audience, SecretKey, ClockSkew, expiração) |
| `ITokenService.cs` + `JwtTokenService.cs` | Emissão HMAC-SHA256 (mín. 32 bytes); claims `sub`, `name`, `role`, `jti` |
| `CashFlowRoles.cs` | Constante `Merchant` — único role no MVP |
| `AuthorizationPolicies.cs` | Constantes `RequireAuthenticated`, `RequireMerchant` |
| `SecurityServiceCollectionExtensions.cs` | `AddCashFlowAuthentication(...)` + `AddCashFlowAuthorization(...)` |

### Componentes (em `CashFlow.Transactions.API/Application/Auth/`)

| Arquivo | Responsabilidade |
|---|---|
| `DemoUserSettings.cs` | Bind da seção `Authentication:Demo` (lista de users + senhas) |
| `IDemoUserStore.cs` + `DemoUserStore.cs` | Validação de credenciais em tempo constante (`CryptographicOperations.FixedTimeEquals`) |
| `AuthDtos.cs` | `LoginRequest`, `TokenResponse` |
| `Controllers/AuthController.cs` | `POST /api/v1/auth/login` retorna `{ accessToken, tokenType, expiresAtUtc, role }` |

### Wire-up no Program.cs (mesmo bloco nos dois APIs)

```csharp
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();
// ... pipeline ...
app.UseAuthentication();
app.UseAuthorization();
```

### Controllers protegidos

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireMerchant)]
public sealed class TransactionsController(...) { ... }
```

`AuthController` é `[AllowAnonymous]` (precisa ser acessível antes de haver token).

### Swagger UI

Ambos os APIs adicionam `OpenApiSecurityScheme` Bearer + `OpenApiSecurityRequirement` no `AddSwaggerGen(...)`. O avaliador clica em **Authorize** no Swagger UI, cola o token e testa qualquer endpoint protegido.

## Demonstração (fluxo de uso)

```bash
# 1. Obter token
curl -X POST http://localhost:5001/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"carlos","password":"S3cret!ChangeMe"}'
# -> { "accessToken":"eyJ...","tokenType":"Bearer","expiresAtUtc":"...","role":"Merchant" }

# 2. Registrar lançamento
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Authorization: Bearer eyJ..." \
  -H "Content-Type: application/json" \
  -d '{"amount":150.00,"type":"credit","description":"Venda","movementDate":"2026-05-23"}'

# 3. Consultar saldo (outro API, mesma chave)
curl http://localhost:5002/api/v1/balance/2026-05-23 \
  -H "Authorization: Bearer eyJ..."

# 4. Sem token: 401
curl -i http://localhost:5002/api/v1/balance/2026-05-23
# HTTP/1.1 401 Unauthorized
```

## Trade-offs

| Ganha | Perde |
|---|---|
| Tokens stateless, validados localmente — zero round-trip a auth server por request | Logout/revogação requer rotação de chave ou denylist (não está no MVP) |
| Stack 100% nativa do ASP.NET Core — sem dependência paga ou em estabilização | DemoUserStore em config é claramente MVP-only — produção exige store real |
| Swagger UI tem botão "Authorize" — fluxo de teste end-to-end óbvio para o avaliador | Sem refresh token, sem MFA, sem confirmação de email |
| Token JWT compartilhado entre Transactions e Balance (mesma key) — Carlos faz login uma vez | Chave simétrica compartilhada: se vaza, ambos os APIs comprometidos. Em produção: keys separadas ou JWKS |
| Policy-based — adicionar role nova = adicionar política + mudar atributo, não reescrever controllers | — |

## ⚠️ Escopo MVP — caminho de evolução para produção

Este ADR documenta o **MVP local**, suficiente para demonstrar conhecimento de segurança em testes técnicos. Cada item abaixo é caminho natural para produção:

| MVP | Evolução em produção |
|---|---|
| Senha em texto claro em `appsettings` | Hash + salt (Argon2id ou PBKDF2 ≥ 600k iterações) em tabela `users` (Postgres) |
| `DemoUserStore` in-memory | `IAppUserRepository` em Postgres com lockout, falhas de login, reset de senha |
| Chave HMAC-SHA256 em `appsettings`/env-var | Azure Key Vault / AWS Secrets Manager, com rotação automática |
| Symmetric key | RSA/ECDSA assimétrica + JWKS endpoint (`.well-known/jwks.json`) — APIs validam só com a chave pública |
| Sem refresh token | Refresh token rotativo (HttpOnly + Secure cookie), denylist de tokens revogados |
| Sem OIDC | Microsoft Entra ID / Auth0 / Keycloak — APIs só validam JWT emitido pelo IdP |
| Sem MFA | TOTP (RFC 6238) + recovery codes |
| Sem API Gateway | Azure APIM / Apigee na frente — centraliza JWT validation, rate limit distribuído, observabilidade |

### Aprofundamento: por que a chave simétrica compartilhada não escala para produção

> Ponto levantado em avaliação técnica: *"a chave simétrica compartilhada entre serviços ficaria limitada a um cenário local. Em produção, faria mais sentido evoluir para RSA/ECDSA com JWKS, rotação de chaves e separação mais clara de responsabilidades."* Confirma e detalha a linha "Symmetric key → RSA/ECDSA + JWKS" da tabela acima.

**O risco do HS256 compartilhado (desenho atual):** com chave simétrica, *quem valida o token tem a mesma chave de quem emite*. Hoje Transactions e Balance compartilham a mesma `SecretKey` — ou seja, o Balance, que deveria apenas **verificar** tokens, é tecnicamente capaz de **forjá-los**. A chave de validação vira um segredo crítico replicado por todos os serviços: a superfície de ataque cresce com o número de serviços, e fere o princípio do menor privilégio (*confused deputy*).

**A evolução (RS256/ES256 + JWKS):**

- O emissor (auth service / IdP) guarda a **chave privada** e é o **único** que assina. Os consumidores recebem só a **chave pública** — que não é segredo e pode ser distribuída livremente.
- Os consumidores buscam a pública de um endpoint **JWKS** (`/.well-known/jwks.json`) e cacheiam. Some a necessidade de distribuir segredo entre serviços.
- **Rotação sem downtime via `kid`:** o JWKS expõe várias chaves identificadas por *key ID* (claim `kid` no header do JWT). Publica-se a chave nova, passa-se a assinar com ela, e os consumidores resolvem a pública correta pelo `kid` do token. A antiga é aposentada após expirarem os tokens que ela assinou. Zero coordenação síncrona entre serviços.
- **ES256 (ECDSA) vs RS256 (RSA):** ECDSA gera chaves/assinaturas menores e assina mais rápido (tendência atual); RSA tem suporte mais universal. Ambos resolvem o ponto.
- **Separação de responsabilidades:** o desenho torna explícito que *um* serviço tem o poder de emitir identidade; os demais apenas consomem. É exatamente a "separação mais clara" apontada na avaliação.

**Ligação com o que já existe no MVP:** a [ADR-024](adr-024-refresh-tokens-rotation.md) já encurtou o access token para 15 min e adicionou refresh rotativo — isso mitiga o problema de revogação (JWT stateless não revoga antes de expirar) e é pré-requisito natural do desenho assimétrico em produção.

## Validação

- **Testes unitários atuais** continuam verdes (não testam HTTP — bypass do middleware Authorization).
- **Manual via Swagger UI**: `dotnet run --project src/CashFlow.Transactions.API` → http://localhost:5001/swagger → Authorize → testar.
- **Integração futura**: cenário BDD ([ADR-017](adr-017-bdd-reqnroll.md)) cobrindo "login → registrar transação → consultar saldo" via `WebApplicationFactory` está como evolução de curto prazo.

## Alternativas descartadas

### OAuth 2.0 / OIDC completo com IdP externo (Entra ID, Auth0, Keycloak)
- **Por quê não:** ferramentas externas em um teste técnico aumentam o atrito de execução para o avaliador (subir Keycloak, configurar realm, etc.). Documentado como evolução.

### Cookie-based authentication
- **Por quê não:** menos adequado para APIs REST consumidas por SPAs/mobile. Refresh-token-em-cookie HttpOnly+Secure permanece como evolução opcional.

### Header API key estático
- **Por quê não:** sem expiração, sem identidade, sem role — não atende ao critério da vaga ("autenticação E autorização").

## ADRs relacionadas

- [ADR-013](adr-013-security-observability.md) — segurança "mínima honesta" anterior; esta ADR sobe para "mínima sólida".
- [ADR-015](adr-015-application-services-no-mediatr.md) — `AuthController` segue o mesmo padrão Application Services (DI direta, sem MediatR).
- [ADR-017](adr-017-bdd-reqnroll.md) — cenários BDD podem cobrir o fluxo end-to-end de auth.
