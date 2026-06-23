-- Migration 003: tabela de idempotência (ADR-011)
-- Garante que reentregas at-least-once não dupliquem o saldo.

CREATE TABLE IF NOT EXISTS balance.processed_events (
    event_id        UUID         NOT NULL,
    consumer_name   VARCHAR(100) NOT NULL,
    processed_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    PRIMARY KEY (event_id, consumer_name)
);

CREATE INDEX IF NOT EXISTS idx_processed_events_at
    ON balance.processed_events (processed_at);
