-- Requires the existing migration chain through 20261024173757_AllowHistoricalStockDates.
-- This script owns BEGIN/COMMIT. Failures roll back DDL and migration history together.
-- Prepared for reviewed deployment; never run automatically against production.
BEGIN;
DO $preflight$
BEGIN
    IF to_regclass('"__EFMigrationsHistory"') IS NULL
       OR to_regclass('reference_preparations') IS NULL OR to_regclass('reference_materials') IS NULL THEN
        RAISE EXCEPTION 'Missing schema or EF migration history. No changes applied.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024173757_AllowHistoricalStockDates') THEN
        RAISE EXCEPTION 'Apply prerequisite 20261024173757_AllowHistoricalStockDates first.';
    END IF;
END $preflight$;
LOCK TABLE reference_preparations, reference_materials IN ACCESS EXCLUSIVE MODE;
DO $migration$
DECLARE
    applied boolean;
    preparation_rule text;
    material_rule text;
BEGIN
    SELECT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028045900_PausePreparationDateValidation') INTO applied;
    SELECT lower(regexp_replace(replace(pg_get_expr(conbin, conrelid), '::text', ''), '[()[:space:]]', '', 'g'))
        INTO preparation_rule FROM pg_constraint WHERE conrelid = 'reference_preparations'::regclass AND conname = 'ck_reference_preparations_dates' AND contype = 'c';
    SELECT lower(regexp_replace(replace(pg_get_expr(conbin, conrelid), '::text', ''), '[()[:space:]]', '', 'g'))
        INTO material_rule FROM pg_constraint WHERE conrelid = 'reference_materials'::regclass AND conname = 'ck_reference_materials_dates' AND contype = 'c';
    IF applied THEN
        IF preparation_rule IS DISTINCT FROM 'updated_at>=created_at'
           OR material_rule IS DISTINCT FROM 'presentation<>''solid''orexpiration_date>=received_date' THEN
            RAISE EXCEPTION 'Migration history and date constraints disagree. Review schema drift; no automatic repair.';
        END IF;
    ELSE
        IF preparation_rule IS DISTINCT FROM 'kind=''stock''orexpiration_date>=preparation_dateandupdated_at>=created_at'
           OR material_rule IS DISTINCT FROM 'expiration_date>=received_date' THEN
            RAISE EXCEPTION 'Unexpected predecessor date constraints. Review schema drift; no changes applied.';
        END IF;
        ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_dates;
        ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_dates;
        ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_dates CHECK (updated_at >= created_at);
        ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_dates CHECK (presentation <> 'Solid' OR expiration_date >= received_date);
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20261028045900_PausePreparationDateValidation', '10.0.10');
    END IF;
END $migration$;
COMMIT;
