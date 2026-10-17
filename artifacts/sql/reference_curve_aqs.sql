START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261016092426_AddCurveAndAqsPreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_kind_status;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261016092426_AddCurveAndAqsPreparations') THEN
    INSERT INTO permission_catalog (permission_code)
    VALUES ('aqs.create');
    INSERT INTO permission_catalog (permission_code)
    VALUES ('aqs.view');
    INSERT INTO permission_catalog (permission_code)
    VALUES ('curva.create');
    INSERT INTO permission_catalog (permission_code)
    VALUES ('curva.view');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261016092426_AddCurveAndAqsPreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_kind_status CHECK (kind IN ('Stock','Intermedia','Curva','AQS') AND status = 'Active' AND (kind <> 'Stock' OR source_material_id IS NOT NULL) AND (kind = 'Stock' OR (source_material_id IS NULL AND method_id IS NOT NULL)) AND (kind NOT IN ('Curva','AQS') OR available_volume = 0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261016092426_AddCurveAndAqsPreparations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261016092426_AddCurveAndAqsPreparations', '10.0.10');
    END IF;
END $EF$;
COMMIT;

