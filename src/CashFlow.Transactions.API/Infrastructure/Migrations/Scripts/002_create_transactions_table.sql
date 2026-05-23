-- Migration 002: cria tabela transactions.transactions

CREATE TABLE IF NOT EXISTS transactions.transactions (
    id              UUID PRIMARY KEY,
    amount          NUMERIC(18,2) NOT NULL CHECK (amount > 0),
    type            VARCHAR(20) NOT NULL CHECK (type IN ('credit', 'debit')),
    description     VARCHAR(200) NOT NULL,
    movement_date   DATE NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_transactions_movement_date
    ON transactions.transactions (movement_date);
