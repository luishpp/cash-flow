-- Inicialização do PostgreSQL para o CashFlow (ADR-003).
-- Roda automaticamente no primeiro startup do container (volume init).
--
-- Cria:
--   - Usuários app_transactions e app_balance (com senhas)
--   - Schemas transactions e balance
--   - GRANTs restritos: cada usuário só tem acesso ao seu schema
--
-- O database 'cashflow' já é criado pelo POSTGRES_DB do container.

-- Usuários por bounded context
CREATE USER app_transactions WITH PASSWORD 'transactions_pwd';
CREATE USER app_balance      WITH PASSWORD 'balance_pwd';

-- Schemas (idempotente)
CREATE SCHEMA IF NOT EXISTS transactions AUTHORIZATION app_transactions;
CREATE SCHEMA IF NOT EXISTS balance      AUTHORIZATION app_balance;

-- Privilégios — cada user só vê seu próprio schema
GRANT USAGE, CREATE ON SCHEMA transactions TO app_transactions;
GRANT USAGE, CREATE ON SCHEMA balance      TO app_balance;

-- Bloqueia acesso cruzado entre schemas
REVOKE ALL ON SCHEMA balance      FROM app_transactions;
REVOKE ALL ON SCHEMA transactions FROM app_balance;

-- Permite que DbUp crie a tabela SchemaVersions em cada schema
ALTER DEFAULT PRIVILEGES IN SCHEMA transactions
    GRANT ALL ON TABLES TO app_transactions;
ALTER DEFAULT PRIVILEGES IN SCHEMA balance
    GRANT ALL ON TABLES TO app_balance;
