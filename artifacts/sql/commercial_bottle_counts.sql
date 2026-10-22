-- Run after the certified-commercial and commercial-role schema migrations.
-- This adaptation changes only a check constraint; it never updates inventory rows.
START TRANSACTION;
DO $preflight$
DECLARE missing text;
BEGIN
    SELECT string_agg(table_name, ', ' ORDER BY table_name) INTO missing
    FROM (VALUES ('reference_materials'), ('reference_certified_components'), ('reference_commercial_roles')) AS required(table_name)
    WHERE to_regclass(format('%I', table_name)) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION USING ERRCODE = '55000',
            MESSAGE = 'Missing bottle-count prerequisites in the current search_path: ' || missing,
            DETAIL = format('Database: %s; schema: %s; search_path: %s', current_database(), current_schema(), current_setting('search_path')),
            HINT = 'Run preflight_commercial_materials.sql in this same connection. Check the schema and reconcile only the missing certified-commercial or commercial-role prerequisites before retrying.';
    END IF;
END $preflight$;
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
    "ProductVersion" character varying(32) NOT NULL
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019232052_AllowCommercialBottleCounts') THEN
    ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_presentation;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019232052_AllowCommercialBottleCounts') THEN
    ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation CHECK (presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND effective_expiration_date <= expiration_date)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261019232052_AllowCommercialBottleCounts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261019232052_AllowCommercialBottleCounts', '10.0.10');
    END IF;
END $EF$;
COMMIT;

