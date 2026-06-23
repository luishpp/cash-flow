# ADR-027: Extração de autenticação para bounded context próprio (CashFlow.Identity.API)

**Status:** Aceita — refina e cria home próprio para [ADR-016](adr-016-jwt-authentication.md), [ADR-021](adr-021-argon2id-password-hashing.md), [ADR-023](adr-023-account-lockout.md), [ADR-024](adr-024-refresh-tokens-rotation.md).

**Data:** 2026-06.

## Contexto

A versão original do projeto hospedava `/api/v1/auth/login`, `/refresh`, `/logout` dentro de **Transactions.API**, justificando-se por proximidade de código (repositórios de user, infraestrutura de Dapper já estavam ali).

Em entrevista técnica, o trade-off foi questionado diretamente: *"Por que acoplar autenticação no serviço de transação?"*. A defesa dada na ocasião não atravessou os limites arquiteturais — foi resposta de **implementação**, não de **arquitetura**. **Esse foi o ponto que mais expôs gap de raciocínio distribuído.**

Esta ADR registra a refatoração: extrair autenticação para **bounded context próprio** (`CashFlow.Identity.API`), com schema PostgreSQL dedicado, user dedicado, migrations próprias e linguagem ubíqua de Identity isolada do domínio de fluxo de caixa.

## Decisão

Criar projeto **`CashFlow.Identity.API`** contendo:

- Domain: `AppUser`, `RefreshToken`, `DomainException`
- Application/Auth: `IAuthenticationService`/`AuthenticationService`, `IPasswordHasher`/`Argon2idPasswordHasher`, `IRefreshTokenFactory`/`Sha256RefreshTokenFactory`, `LockoutSettings`, `RefreshTokenSettings`, `AuthDtos`
- Infrastructure: `AppUserRepository`, `RefreshTokenRepository`, `DemoUserSeeder`, Dapper UoW próprio, `MigrationRunner` próprio
- Controllers: `AuthController` (`/api/v1/auth/login`, `/refresh`, `/logout`)
- Schema PostgreSQL **`identity`** com user **`app_identity`** (separado de `transactions` e `balance`)
- Migrations próprias (renumeradas 001 a 003)

Transactions.API agora **só valida JWT** emitido pela Identity (via `CashFlow.Shared.Security`) — não emite.

## Análise pelos 4 limites

### 1. Limite de domínio

Esse é o limite **mais forte** para Identity. A linguagem ubíqua é completamente distinta:

| Domínio Transactions | Domínio Identity |
|---|---|
| Lançamento, débito, crédito, saldo, conta, movimentação | Credential, password hash, lockout, refresh rotation, session, MFA, password policy |
| Aggregate `Transaction` (Money, MovementDate, TransactionType) | Aggregate `AppUser` (lockout state, failed attempts), `RefreshToken` (rotation chain) |
| Invariantes de negócio financeiro | Invariantes de identidade e ciclo de vida de sessão |
| Glossário pt-br ↔ en-us de fluxo de caixa | Glossário pt-br ↔ en-us de segurança/IAM |

Forçar essas duas linguagens no mesmo BC é **antipattern clássico** de DDD (modelos colidem, mudança em um afeta o outro). Identity é BC **separado por design**, mesmo que esteja "tudo no mesmo Postgres" por economia.

### 2. Limite de escala

Padrões de carga distintos:

- **Login (Identity):** concentra picos previsíveis — abertura de caixa às 7h, intervalo de almoço, fechamento às 19h. SLA crítico em milissegundos: usuário não tolera latência no login.
- **Registro de lançamento (Transactions):** distribui ao longo do dia. Picos absorvíveis por outbox + fila assíncrona.

Em sistema de N comerciantes, login concorrente cresce **com o número de pontos de venda**; registro de lançamento cresce **com o volume de vendas**. Funções diferentes do mesmo N → padrões de escala diferentes.

### 3. Limite de falha

Cenário crítico: **credential stuffing attack** contra o login. Se auth está dentro de Transactions:

- Spike de Argon2id hashing (cada attempt = ~50-100ms de CPU pesada) **degrada o threadpool da API que registra transações**.
- Lockout em massa de usuários **não pode** se traduzir em failure rate da rota de transações.
- Vulnerabilidade descoberta em política de senha exige hotfix da auth — toca um deploy que também serve write side.

Com Identity separada: ataque concentra dano em Identity. Transactions continua aceitando lançamentos de comerciantes já autenticados.

### 4. Limite de deploy / versionamento

Identity evolui em **ritmo regulatório próprio**:

- **LGPD** (Brasil): direito ao esquecimento, log de acessos, tempo de retenção.
- **PCI DSS** (se aceitar cartão): rotação de credenciais a cada 90 dias, MFA obrigatório.
- **OWASP ASVS:** lockout policies, refresh rotation, evitar timing attacks.

Cada update regulatório vira deploy de Identity — sem touch em Transactions. Recíproco: schema de transações evolui (novos tipos de lançamento, novas categorias) sem touch em auth.

## Trade-offs

| Ganha | Paga |
|---|---|
| Domínio de Identity isolado (linguagem ubíqua própria) | +1 container no docker-compose |
| Schema PostgreSQL dedicado com user próprio (defesa em profundidade) | +1 csproj + Program.cs + Dockerfile |
| Deploy regulatório (LGPD/PCI) sem touch em transações | Duplicação de infra de Dapper (`UnitOfWork`, `ConnectionFactory`) em Identity + Transactions + Balance.Core |
| Failure isolado: credential stuffing não derruba write side | JWT compartilhado via env vars (em prod: Key Vault) |
| Política de senha evolui em ritmo próprio | Adicional `init.sql` cria schema/user |
| Argon2id hashing dedicado (não compete CPU com write side) | Maior complexidade de boot orchestration |

## Por que **NÃO** usar Azure Entra ID / Auth0 já

Em produção real, terceirizar Identity é decisão correta:

- Compliance (SOC2, ISO27001) por design
- MFA, social login, SSO built-in
- Não reinventar primitivas (Argon2id, refresh rotation)

**Para o escopo deste demo**, manter Identity in-house ainda demonstra valor:

1. Mostra dominação dos building blocks (Argon2id, refresh rotation, lockout) — fundamentos que você precisa entender mesmo se terceirizar.
2. Mantém o demo executável localmente sem credenciais cloud.
3. Citado como evolução natural: *"em produção, substituir DemoUserSeeder + AuthController por integração OIDC com Entra ID — domínio e contratos permanecem; só a fonte de truth muda"*.

## Configuração

```csharp
// Identity Program.cs
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();
builder.Services.Configure<LockoutSettings>(builder.Configuration.GetSection(...));
builder.Services.Configure<RefreshTokenSettings>(builder.Configuration.GetSection(...));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<IRefreshTokenFactory, Sha256RefreshTokenFactory>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Migrations próprias com journal table em schema identity
MigrationRunner.EnsureUpToDate(connectionString, logger);
// → JournalToPostgresqlTable("identity", "schemaversions")
```

```sql
-- infra/postgres/init.sql
CREATE USER app_identity WITH PASSWORD 'identity_pwd';
CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION app_identity;

-- Defesa em profundidade: bloqueia cross-schema mesmo via SQL injection
REVOKE ALL ON SCHEMA identity FROM app_transactions, app_balance;
```

## Por que mesma `SecretKey` JWT em 4 serviços (Identity, Transactions, Balance, Admin)

Identity **emite** o JWT; os outros **validam** com a mesma `HMAC-SHA256` key. Em produção:

- Em vez de symmetric key, usar **RS256/ES256**: Identity assina com private key, outros validam com public key (`jwks.json`).
- Public key distribuída via Identity's `/.well-known/openid-configuration`.
- Rotação de chave sem coordenar deploy dos validadores.

Por simplicidade do demo, mantemos HMAC + env var. Evolução clara documentada.

## Alternativas descartadas

**A. Manter auth dentro de Transactions.API (status quo)**
Defendido na entrevista por reuso de Dapper UoW e repositórios. **Rejeitado** porque o reuso é otimização de implementação, não de arquitetura — 5 arquivos de UoW duplicados é trivial comparado ao acoplamento de domínios.

**B. Auth como microsserviço genérico (não BC)**
Mesma extração, mas sem o vocabulário DDD. **Rejeitado** porque perde a oportunidade de demonstrar raciocínio estratégico: Identity é Core ou Supporting subdomain dependendo do negócio (pra um fintech, é Core; pra um SaaS de fluxo de caixa, é Supporting → candidato a terceirizar quando maduro).

**C. Substituir por IdentityServer / Duende IdentityServer já no MVP**
Excesso de cerimônia para 1 endpoint de login. Duende é pago. Adiar para evolução.

## RNFs atendidos

| RNF | Como atende |
|---|---|
| **RNF-05 — Segurança** | Isolamento físico de dados de credential; ataque a auth não degrada write side; defesa em profundidade via GRANT |
| **RNF-08 — Manutenibilidade** | Domínio de Identity tem código + ciclo de evolução próprios |

## Fitness functions que defendem essa decisão

Adicionadas em `tests/CashFlow.Architecture.Tests/BoundedContextIsolationTests.cs`:

- `Identity_MustNotDependOn_Transactions_Balance_or_Admin`
- `Transactions_MustNotDependOn_Identity_Balance_or_Admin`
- Adicionalmente: `Identity_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` + `Identity_Entities_MustNotHavePublicSetters` (Rich Domain em AppUser e RefreshToken).
