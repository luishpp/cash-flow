-- Migration 003: tabela de usuários da aplicação (ADR-016, ADR-021).
-- Armazena password_hash codificado (Argon2id PHC format) — nunca senha em claro.

CREATE TABLE IF NOT EXISTS transactions.app_users (
    id              UUID PRIMARY KEY,
    username        VARCHAR(64) NOT NULL,
    password_hash   VARCHAR(512) NOT NULL,
    role            VARCHAR(32) NOT NULL,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login_at   TIMESTAMPTZ NULL,
    CONSTRAINT uq_app_users_username UNIQUE (username)
);

CREATE INDEX IF NOT EXISTS idx_app_users_username
    ON transactions.app_users (username);
