# ADR-023: Account lockout após tentativas falhas consecutivas

**Status:** Aceita

## Contexto

[ADR-021](adr-021-argon2id-password-hashing.md) introduziu hash Argon2id com custo ~250ms por verify. Isso já é uma defesa primária contra brute-force online (atacante consegue ~4 tentativas/segundo por thread). Mas não basta:

- **Brute-force lento** (1 tentativa/min, embaixo de qualquer rate limit) continua viável em horas/dias.
- **Conta-específico** (alvo: 1 username conhecido) — atacante não precisa enumerar, só persistir.
- **Credential stuffing** com listas vazadas — testa pares (email, password) já comprometidos contra a nossa base.

O OWASP Authentication Cheat Sheet recomenda **account lockout** como camada complementar: após N tentativas falhas, trava a conta por X minutos. Não substitui rate limiting nem hash forte — soma com eles.

## Decisão

Adicionar lockout por usuário com defaults pragmáticos:

| Parâmetro | Default | Justificativa |
|---|---|---|
| `MaxFailedAttempts` | **5** | OWASP recomenda 3-10; 5 é o ponto comum (Microsoft, AWS, Auth0 default) |
| `LockoutDuration` | **15 min** | Curto o bastante para usuário legítimo esperar; longo o bastante para tornar brute-force ineficaz |

Configurável via `Authentication:Lockout` em appsettings.

### Implementação

**Domain** (`Domain/Entities/AppUser.cs`):

```csharp
public int FailedLoginAttempts { get; private set; }
public DateTimeOffset? LockedUntil { get; private set; }
public bool IsLockedOut => LockedUntil is { } until && until > DateTimeOffset.UtcNow;

public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
{
    FailedLoginAttempts += 1;
    if (FailedLoginAttempts >= maxAttempts)
        LockedUntil = DateTimeOffset.UtcNow.Add(lockoutDuration);
}

public void RecordSuccessfulLogin()
{
    FailedLoginAttempts = 0;
    LockedUntil = null;
    LastLoginAt = DateTimeOffset.UtcNow;
}
```

**Application** (`AuthenticationService`):

```csharp
if (user.IsLockedOut) return null;                   // bloqueia ANTES de verify (não vaza timing)
if (!hasher.Verify(password, user.PasswordHash))
{
    user.RegisterFailedLogin(_lockout.MaxFailedAttempts, _lockout.LockoutDuration);
    await users.UpdateAuthStateAsync(user, ct);      // persiste contador + locked_until
    await uow.CommitAsync(ct);
    return null;
}
user.RecordSuccessfulLogin();
await users.UpdateAuthStateAsync(user, ct);
```

**Migration 004** (`Infrastructure/Migrations/Scripts/004_alter_app_users_lockout.sql`):

```sql
ALTER TABLE transactions.app_users
    ADD COLUMN IF NOT EXISTS failed_login_attempts INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_app_users_locked_until
    ON transactions.app_users (locked_until)
    WHERE locked_until IS NOT NULL;
```

Índice parcial — só indexa registros travados, ínfimo overhead em produção.

### Comportamento durante lockout expirado

Após `LockedUntil` passar:
- `IsLockedOut` retorna false → user pode tentar logar
- `FailedLoginAttempts` **permanece em N** (não reseta automaticamente)
- Próxima falha → contador continua incrementando (N+1) → re-lock imediato

Trade-off escolhido: rigoroso vs. permissivo. Atacante que esperou 15 min para tentar de novo provavelmente não é o user legítimo.

### Anti user-enumeration preservado

Resposta HTTP é a mesma para todos os cenários de falha (usuário inexistente, senha errada, conta travada): **401 Unauthorized + body genérico**. Logs internos distinguem para troubleshooting. Mantém a invariante introduzida em [ADR-021](adr-021-argon2id-password-hashing.md).

## Trade-offs

| Ganha | Perde |
|---|---|
| Brute-force online inviável — 5 tentativas/15 min = ~480 tentativas/dia | Usuário legítimo que esqueceu a senha trava em 5 tentativas (UX) |
| Counter persistido por usuário — protege mesmo se atacante distribui IP origem | Não há reset automático após expirar — exige login bem-sucedido |
| Custo ~zero: 2 colunas + 1 índice parcial | Sem distinção "trava por suspeita" vs "trava por excesso" |
| Combina com hash lento (Argon2id) — duas camadas independentes | DoS-leve por conta: atacante intencional pode travar conta alvo de propósito |

### Mitigação do "trava conta alvo"

Para o MVP, aceitamos. Em produção, mitigações comuns:
- **Reset via email**: link de "destravar conta" enviado para o email do usuário.
- **Lockout com IP-aware**: separa contadores por IP origem (atacante de IPs únicos não trava o legítimo).
- **CAPTCHA** após 2-3 falhas (passa pela UX, não trava).

## Validação

- **Unit**: `AppUserTests` cobre `IsLockedOut`, `RegisterFailedLogin` (incremento, lockout no max), `RecordSuccessfulLogin` (reset).
- **Mutation**: Stryker.NET no escopo `**/Domain/**/*.cs` ([ADR-020](adr-020-stryker-mutation-testing.md)).
- **BDD E2E**: cenário `"Conta é travada após N tentativas falhas"` em `Features/AutenticacaoE2E.feature` — usa env de teste com `MaxFailedAttempts=3` para rodar rápido.

## Evolução natural

| Item | Quando |
|---|---|
| **Notificação por email** ao usuário quando conta trava | Quando email/SMTP existir |
| **Reset por email** ("link mágico" para destravar) | Junto com reset de senha |
| **Lockout IP-aware** (Postgres adicional table `failed_logins_by_ip`) | Quando bot/scraping virar tema |
| **CAPTCHA escalonado** (hCaptcha/reCaptcha v3) após 2-3 falhas | Quando UX exigir alternativa ao lockout duro |
| **Rate limit no `/auth/login` por IP** | Curto prazo — complementa lockout por user |
| **Analytics de tentativas falhas** (dashboard) | Quando time de SecOps existir |

## ADRs relacionadas

- [ADR-021](adr-021-argon2id-password-hashing.md) — Argon2id; lockout é a 2ª camada contra brute-force.
- [ADR-016](adr-016-jwt-authentication.md) — JWT/AuthN cuja política de retry agora tem limite.
- [ADR-024](adr-024-refresh-tokens-rotation.md) — refresh tokens; user travado também não consegue rotacionar refresh.
- [ADR-009](adr-009-rich-domain-model.md) — invariantes de lockout encapsuladas em `AppUser`.
- [ADR-022](adr-022-bdd-e2e-webapplicationfactory.md) — cenário BDD E2E valida o fluxo.
