-- Inicialização do PostgreSQL para o CashFlow.
-- Roda automaticamente no primeiro startup do container (volume init).
--
-- Cria 3 bounded contexts isolados por schema + user dedicado:
--   - identity      (ADR-027) — auth, app_users, refresh_tokens     → app_identity
--   - transactions  (ADR-003) — write side, outbox, transactions    → app_transactions
--   - balance       (ADR-003) — read side, daily_balance,
--                                processed_events                   → app_balance
--
-- Cada usuário tem GRANT restrito ao seu schema. Tentativa de cross-schema
-- access via password leak / SQL injection é bloqueada pelo banco — não confia no app.

-- Usuários por bounded context
CREATE USER app_identity     WITH PASSWORD 'identity_pwd';
CREATE USER app_transactions WITH PASSWORD 'transactions_pwd';
CREATE USER app_balance      WITH PASSWORD 'balance_pwd';

-- Schemas (idempotente)
CREATE SCHEMA IF NOT EXISTS identity     AUTHORIZATION app_identity;
CREATE SCHEMA IF NOT EXISTS transactions AUTHORIZATION app_transactions;
CREATE SCHEMA IF NOT EXISTS balance      AUTHORIZATION app_balance;

-- Privilégios — cada user só vê seu próprio schema
GRANT USAGE, CREATE ON SCHEMA identity     TO app_identity;
GRANT USAGE, CREATE ON SCHEMA transactions TO app_transactions;
GRANT USAGE, CREATE ON SCHEMA balance      TO app_balance;

-- Bloqueia acesso cruzado entre schemas (defesa em profundidade)
REVOKE ALL ON SCHEMA identity     FROM app_transactions, app_balance;
REVOKE ALL ON SCHEMA transactions FROM app_identity,     app_balance;
REVOKE ALL ON SCHEMA balance      FROM app_identity,     app_transactions;

-- Permite que DbUp crie a tabela SchemaVersions em cada schema
ALTER DEFAULT PRIVILEGES IN SCHEMA identity
    GRANT ALL ON TABLES TO app_identity;
ALTER DEFAULT PRIVILEGES IN SCHEMA transactions
    GRANT ALL ON TABLES TO app_transactions;
ALTER DEFAULT PRIVILEGES IN SCHEMA balance
    GRANT ALL ON TABLES TO app_balance;
