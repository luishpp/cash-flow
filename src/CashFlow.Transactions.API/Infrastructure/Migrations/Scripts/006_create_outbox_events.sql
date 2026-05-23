-- Migration 006: tabela outbox (publisher-side reliability — ADR-007 mitigation)
-- Garante que evento e INSERT da transação caem na MESMA tx — se RabbitMQ estiver fora,
-- o evento fica em PENDING e o dispatcher (BackgroundService) tenta novamente.

CREATE TABLE IF NOT EXISTS transactions.outbox_events (
    seq             BIGSERIAL    NOT NULL,
    id              UUID         PRIMARY KEY,
    event_type      VARCHAR(200) NOT NULL,
    payload         JSONB        NOT NULL,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at    TIMESTAMPTZ  NULL,
    attempts        INTEGER      NOT NULL DEFAULT 0,
    last_error      TEXT         NULL
);

-- ORDER BY seq garante FIFO determinístico mesmo em batch (NOW() idêntico no mesmo statement).
CREATE INDEX IF NOT EXISTS idx_outbox_events_pending
    ON transactions.outbox_events (seq)
    WHERE published_at IS NULL;
