START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_preparation_components DROP CONSTRAINT ck_reference_components_source;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials DROP CONSTRAINT ck_reference_materials_purity;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_material_movements DROP CONSTRAINT ck_reference_material_movements_kind;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_preparation_components ALTER COLUMN source_preparation_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_preparation_components ADD source_material_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ALTER COLUMN purity_percent DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD commercial_json jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD effective_expiration_date date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD opened_date date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD opening_required boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD presentation character varying(24) NOT NULL DEFAULT 'Solid';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE TABLE reference_certified_components (
        id uuid NOT NULL,
        material_id uuid NOT NULL,
        name text NOT NULL,
        cas_number text,
        certified_concentration numeric NOT NULL,
        concentration_unit text NOT NULL,
        expanded_uncertainty numeric,
        purity_percent numeric,
        lot text,
        additional_data text,
        CONSTRAINT "PK_reference_certified_components" PRIMARY KEY (id),
        CONSTRAINT ck_certified_component_values CHECK (certified_concentration > 0 AND (expanded_uncertainty IS NULL OR expanded_uncertainty >= 0) AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100))),
        CONSTRAINT "FK_reference_certified_components_reference_materials_material~" FOREIGN KEY (material_id) REFERENCES reference_materials (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE TABLE reference_material_opening_history (
        id uuid NOT NULL,
        material_id uuid NOT NULL,
        previous_date date,
        opened_date date,
        actor_user_id integer NOT NULL,
        changed_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_reference_material_opening_history" PRIMARY KEY (id),
        CONSTRAINT "FK_reference_material_opening_history_reference_materials_mate~" FOREIGN KEY (material_id) REFERENCES reference_materials (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_material_opening_history_usuarios_actor_user_id" FOREIGN KEY (actor_user_id) REFERENCES usuarios (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE UNIQUE INDEX "IX_reference_preparation_components_preparation_id_source_mate~" ON reference_preparation_components (preparation_id, source_material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE INDEX "IX_reference_preparation_components_source_material_id" ON reference_preparation_components (source_material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_preparation_components ADD CONSTRAINT ck_reference_components_source CHECK ((source_preparation_id IS NOT NULL AND source_material_id IS NULL AND preparation_id <> source_preparation_id AND source_type IN ('Stock','Intermedia')) OR (source_preparation_id IS NULL AND source_material_id IS NOT NULL AND source_type IN ('CommercialSolution','CommercialMix')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_presentation CHECK (presentation IN ('Solid','CommercialSolution','CommercialMix') AND (presentation = 'Solid' OR (commercial_json IS NOT NULL AND package_count = 1 AND effective_expiration_date <= expiration_date)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_materials ADD CONSTRAINT ck_reference_materials_purity CHECK ((presentation = 'Solid' AND purity_percent IS NOT NULL AND purity_percent > 0 AND purity_percent <= 100) OR (presentation <> 'Solid' AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100))));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_material_movements ADD CONSTRAINT ck_reference_material_movements_kind CHECK ((kind = 'StockConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = 'IntermediateConsumption' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL) OR (kind = 'CommercialConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE INDEX "IX_reference_certified_components_material_id" ON reference_certified_components (material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE INDEX "IX_reference_material_opening_history_actor_user_id" ON reference_material_opening_history (actor_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    CREATE INDEX "IX_reference_material_opening_history_material_id" ON reference_material_opening_history (material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    ALTER TABLE reference_preparation_components ADD CONSTRAINT "FK_reference_preparation_components_reference_materials_source~" FOREIGN KEY (source_material_id) REFERENCES reference_materials (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261017063950_AddCertifiedCommercialMaterials', '10.0.10');
    END IF;
END $EF$;
COMMIT;

