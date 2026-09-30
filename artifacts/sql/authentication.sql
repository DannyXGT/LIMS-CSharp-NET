SET search_path TO public;

START TRANSACTION;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE TABLE auth_sessions (
        id uuid NOT NULL,
        user_id integer NOT NULL,
        token_family_id uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        last_seen_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        revocation_reason character varying(120),
        client_name character varying(120),
        CONSTRAINT "PK_auth_sessions" PRIMARY KEY (id),
        CONSTRAINT "FK_auth_sessions_usuarios_user_id" FOREIGN KEY (user_id) REFERENCES usuarios (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE TABLE auth_refresh_tokens (
        id uuid NOT NULL,
        session_id uuid NOT NULL,
        token_hash character(64) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        consumed_at timestamp with time zone,
        replaced_by_token_id uuid,
        revoked_at timestamp with time zone,
        CONSTRAINT "PK_auth_refresh_tokens" PRIMARY KEY (id),
        CONSTRAINT "FK_auth_refresh_tokens_auth_refresh_tokens_replaced_by_token_id" FOREIGN KEY (replaced_by_token_id) REFERENCES auth_refresh_tokens (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_auth_refresh_tokens_auth_sessions_session_id" FOREIGN KEY (session_id) REFERENCES auth_sessions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE INDEX "IX_auth_refresh_tokens_replaced_by_token_id" ON auth_refresh_tokens (replaced_by_token_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE INDEX ix_auth_refresh_tokens_session_id ON auth_refresh_tokens (session_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE UNIQUE INDEX ux_auth_refresh_tokens_hash ON auth_refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE INDEX ix_auth_sessions_expiry_revocation ON auth_sessions (expires_at, revoked_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE INDEX ix_auth_sessions_user_id ON auth_sessions (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    CREATE UNIQUE INDEX ux_auth_sessions_token_family_id ON auth_sessions (token_family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260926045625_CreateAuthenticationSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260926045625_CreateAuthenticationSessions', '10.0.10');
    END IF;
END $EF$;
COMMIT;

