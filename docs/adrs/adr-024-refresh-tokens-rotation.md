# ADR-024: Refresh tokens opacos com rotação a cada uso

**Status:** Aceita

## Contexto

[ADR-016](adr-016-jwt-authentication.md) emitia JWT de **60 minutos** sem refresh — depois disso o usuário tinha que se autenticar novamente. Dois problemas opostos:

1. **Token longo (60min+)** — janela de exposição grande se vazar (XSS, log indevido, browser extension maliciosa).
2. **Token curto sem refresh** — UX péssima: usuário re-loga a cada hora.

Padrão de mercado: **access token curto (15 min)** + **refresh token longo (7 dias)**. O refresh é trocado por novo access sem re-prompt de credenciais. JWT não tem `revoke` nativo — refresh tokens persistidos em DB resolvem isso.

## Decisão

Implementar refresh tokens **opacos** (não-JWT, alta entropia random) com **rotação a cada uso**:

| Parâmetro | Valor | Justificativa |
|---|---|---|
| Access token (JWT) expira em | **15 min** (↓ de 60) | Janela de exposição reduzida 4× |
| Refresh token expira em | **7 dias** | Padrão SaaS (Auth0, Microsoft Entra ID default) |
| Tamanho do raw token | **256 bits** (32 bytes) | Entropia suficiente — chance de colisão desprezível |
| Encoding raw | **Base64Url** | URL-safe (cabe em headers, cookies, query strings) |
| Hash p/ persistência | **SHA-256** | Não-iterativo basta — alta entropia já protege contra dicionário |
| Rotação | **A cada `/refresh`** | Token usado vira inválido imediatamente — limita reuso |

### Por que tokens opacos (não JWT)

JWT seria tentador (stateless, sem lookup em DB). Mas:
- **Não dá pra revogar** sem manter uma denylist — anula a vantagem stateless.
- **Refresh raramente é chamado** (1× a cada 15min) — custo de lookup em DB é irrelevante.
- **Opaco protege**: vazamento revela só o token, não claims (`sub`, `role`) que JWT exporia.

### Por que SHA-256 (e não Argon2id)

Argon2id ([ADR-021](adr-021-argon2id-password-hashing.md)) custa ~250ms — adequado para senhas (baixa entropia: ~30 bits de entropia humana). Refresh tokens têm **256 bits de entropia random** — impossível brute-forçar mesmo com SHA-256 puro. Argon2id aqui só atrasaria `/refresh` sem ganho de segurança.

### Esquema de persistência

```sql
CREATE TABLE transactions.refresh_tokens (
    id                      UUID PRIMARY KEY,
    user_id                 UUID NOT NULL REFERENCES transactions.app_users(id),
    token_hash              VARCHAR(128) NOT NULL UNIQUE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at              TIMESTAMPTZ NOT NULL,
    revoked_at              TIMESTAMPTZ NULL,
    replaced_by_token_hash  VARCHAR(128) NULL
);
```

- `token_hash` único — colisão impossível na prática, mas garantida no schema.
- `replaced_by_token_hash` — encadeia a cadeia de rotação. Útil para auditoria e para implementar **reuse detection** no futuro (ver "Evolução").
- Índice parcial em `(user_id) WHERE revoked_at IS NULL` — housekeeping/listagem de sessões ativas.

### Endpoints

```text
POST /api/v1/auth/login    → emite par (access JWT 15min + refresh opaco 7d)
POST /api/v1/auth/refresh  → consome refresh + emite par novo, revoga o antigo
POST /api/v1/auth/logout   → revoga o refresh (access continua válido até expirar — aceitável p/ 15min)
```

Response do login agora inclui:

```json
{
  "accessToken": "eyJhbG...",
  "tokenType": "Bearer",
  "expiresAtUtc": "2026-05-23T17:15:00+00:00",
  "role": "Merchant",
  "refreshToken": "9aQ7VxN3kP...",
  "refreshTokenExpiresAtUtc": "2026-05-30T17:00:00+00:00"
}
```

### Fluxo de rotação

```text
Login → token A (refresh) + access JWT
       └─► DB: A active

/refresh com A → token B (novo refresh) + access JWT novo
              └─► DB: A revoked, B active, A.replaced_by = hash(B)

/refresh com A novamente → 401 (A já revogado)
```

## Trade-offs

| Ganha | Perde |
|---|---|
| Access token de 15 min — janela de comprometimento 4× menor | UX exige refresh transparente no cliente (boilerplate frontend) |
| Logout funciona — revoga refresh; próximo /refresh falha | Logout NÃO invalida access ativo (válido até 15min — aceitável) |
| Rotação detecta reuso (em evolução: revogar cadeia) | Cada login deixa um row no DB — exige housekeeping |
| Sessão de 7 dias (UX boa) com janela de risco curta | Mais complexidade que JWT puro: 1 endpoint extra, 1 tabela, 1 entity |
| Token opaco — vazamento não vaza claims | Refresh exige DB lookup (irrelevante: ~1× por 15min) |
| SHA-256 puro p/ hash — performance não impacta `/refresh` | Mudar p/ HMAC-SHA256 c/ pepper seria caminho de evolução |

## Validação

- **Unit**: `RefreshTokenTests` cobre `Issue` (válido + falhas), `IsActive` (3 cenários: expirado/revogado/ativo), `Revoke` (com/sem sucessor, idempotência).
- **Mutation**: Stryker.NET no `**/Domain/**/*.cs` ([ADR-020](adr-020-stryker-mutation-testing.md)) — RefreshToken score atual ≥ 85%.
- **BDD E2E** (`Features/AutenticacaoE2E.feature`):
  - Login agora retorna access + refresh
  - `/refresh` rotaciona e emite par novo
  - `/refresh` com token já rotacionado → 401
  - `/logout` revoga; `/refresh` subsequente → 401

## Evolução natural

| Item | Quando |
|---|---|
| **Reuse detection cascade** — se um refresh JÁ revogado é apresentado, suspeita de roubo → revoga TODOS os refresh tokens daquele user. Hoje a tabela `replaced_by_token_hash` já tem os dados; falta o cascade. | Antes do primeiro user real em produção |
| **Refresh em HttpOnly + Secure cookie** (não em response body) | Quando houver frontend; mitiga XSS roubando o refresh do localStorage |
| **Pepper** (chave secreta concatenada ao raw antes do SHA-256) — Key Vault | Quando houver Key Vault |
| **Sliding window** (a cada uso, estende expiração ao invés de manter os 7 dias originais) | Quando UX exigir sessões muito longas sem re-prompt |
| **Job de cleanup** dos refresh tokens revogados/expirados (housekeeping) | Quando volume de logins ficar alto — 1 row por login |
| **Device-bound refresh** (token assinado com chave do device) | Quando MFA + device-management entrarem |
| **OIDC com IdP externo** | Substitui todo este ADR — IdP gerencia refresh ([ADR-016](adr-016-jwt-authentication.md) § Evolução) |

## ADRs relacionadas

- [ADR-016](adr-016-jwt-authentication.md) — JWT/AuthN; access token agora dura 15min em vez de 60.
- [ADR-021](adr-021-argon2id-password-hashing.md) — Argon2id para senhas; SHA-256 aqui é a escolha certa para tokens opacos.
- [ADR-023](adr-023-account-lockout.md) — user travado não consegue rotacionar refresh.
- [ADR-009](adr-009-rich-domain-model.md) — `RefreshToken` é Rich Domain (factory + invariantes).
- [ADR-022](adr-022-bdd-e2e-webapplicationfactory.md) — cenários BDD E2E validam o fluxo de rotação.
