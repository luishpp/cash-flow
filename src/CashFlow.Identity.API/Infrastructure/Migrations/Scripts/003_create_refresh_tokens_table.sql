-- Migration 005: refresh tokens persistidos para rotação (ADR-024).
-- token_hash = SHA-256(raw_token) em Base64; raw_token NUNCA é persistido.

CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
    id                      UUID PRIMARY KEY,
    user_id                 UUID NOT NULL REFERENCES identity.app_users(id),
    token_hash              VARCHAR(128) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at              TIMESTAMPTZ NOT NULL,
    revoked_at              TIMESTAMPTZ NULL,
    replaced_by_token_hash  VARCHAR(128) NULL,
    CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash)
);

-- Lookup por hash (uso no /refresh e /logout).
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_token_hash
    ON identity.refresh_tokens (token_hash);

-- Limpeza periódica (housekeeping) de tokens expirados/revogados.
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id_active
    ON identity.refresh_tokens (user_id)
    WHERE revoked_at IS NULL;
