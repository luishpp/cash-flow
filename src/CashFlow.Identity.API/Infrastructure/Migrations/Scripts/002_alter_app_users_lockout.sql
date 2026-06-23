-- Migration 004: lockout em identity.app_users (ADR-023).
-- Idempotente — ADD COLUMN IF NOT EXISTS.

ALTER TABLE identity.app_users
    ADD COLUMN IF NOT EXISTS failed_login_attempts INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_app_users_locked_until
    ON identity.app_users (locked_until)
    WHERE locked_until IS NOT NULL;
