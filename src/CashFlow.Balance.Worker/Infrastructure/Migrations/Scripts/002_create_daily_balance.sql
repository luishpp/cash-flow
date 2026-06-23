-- Migration 002: cria projeção de saldo diário

CREATE TABLE IF NOT EXISTS balance.daily_balance (
    date            DATE PRIMARY KEY,
    total_credits   NUMERIC(18,2) NOT NULL DEFAULT 0 CHECK (total_credits >= 0),
    total_debits    NUMERIC(18,2) NOT NULL DEFAULT 0 CHECK (total_debits  >= 0),
    updated_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
