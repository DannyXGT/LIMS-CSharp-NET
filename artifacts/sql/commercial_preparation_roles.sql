START TRANSACTION;

-- Initialize EF history when this incremental script is applied to a schema without it.
-- Do not insert baseline migration IDs: only completed operations below are recorded.
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $preflight$
DECLARE missing text;
BEGIN
    IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
        RETURN;
    END IF;
    SELECT string_agg(table_name, ', ' ORDER BY table_name) INTO missing
    FROM (VALUES ('reference_materials'), ('reference_certified_components'), ('usuarios')) AS required(table_name)
    WHERE to_regclass(format('%I', table_name)) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION USING ERRCODE = '55000',
            MESSAGE = 'Missing commercial role prerequisites in the current search_path: ' || missing,
            HINT = 'Run preflight_commercial_materials.sql. Apply or reconcile the base material and certified commercial schema before this role migration.';
    END IF;
END $preflight$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
    CREATE TABLE reference_commercial_roles (
        material_id uuid NOT NULL,
        kind character varying(24) NOT NULL,
        code character varying(40) NOT NULL,
        request_id uuid NOT NULL,
        request_fingerprint character varying(64) NOT NULL,
        registered_by_user_id integer NOT NULL,
        registered_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_reference_commercial_roles" PRIMARY KEY (material_id, kind),
        CONSTRAINT ck_commercial_role_kind CHECK (kind IN ('Stock','Intermedia')),
        CONSTRAINT "FK_reference_commercial_roles_reference_materials_material_id" FOREIGN KEY (material_id) REFERENCES reference_materials (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_commercial_roles_usuarios_registered_by_user_id" FOREIGN KEY (registered_by_user_id) REFERENCES usuarios (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
    CREATE UNIQUE INDEX "IX_reference_commercial_roles_code" ON reference_commercial_roles (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
    CREATE INDEX "IX_reference_commercial_roles_registered_by_user_id" ON reference_commercial_roles (registered_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
    CREATE UNIQUE INDEX "IX_reference_commercial_roles_request_id" ON reference_commercial_roles (request_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017162400_AddCommercialPreparationRoles') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261017162400_AddCommercialPreparationRoles', '10.0.10');
    END IF;
END $EF$;
COMMIT;

