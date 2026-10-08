START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements DROP CONSTRAINT "FK_reference_material_movements_reference_preparations_prepara~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ak_reference_preparations_consumption;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_dates;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_kind_status;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_purity;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_quantities;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations DROP CONSTRAINT ck_reference_preparations_units;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    DROP INDEX "IX_reference_material_movements_preparation_id_source_material~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    DROP INDEX ux_reference_material_movements_preparation;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements DROP CONSTRAINT ck_reference_material_movements_kind;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE SEQUENCE reference_intermediate_code_sequence START WITH 1 INCREMENT BY 1 NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ALTER COLUMN source_material_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD available_volume numeric(18,6) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD diluted_preparation_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD method_id integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD results_json jsonb NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD updated_by_user_id integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    UPDATE reference_preparations SET available_volume = final_volume, updated_by_user_id = prepared_by_user_id WHERE kind = 'Stock'; UPDATE reference_preparations p SET method_id = m.id FROM reference_methods m WHERE p.kind = 'Stock' AND p.source_method = m.name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ALTER COLUMN source_material_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD component_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD source_preparation_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE TABLE reference_preparation_components (
        id uuid NOT NULL,
        preparation_id uuid NOT NULL,
        source_preparation_id uuid NOT NULL,
        source_type character varying(24) NOT NULL,
        source_version uuid NOT NULL,
        position integer NOT NULL,
        volume_taken numeric(18,6) NOT NULL,
        volume_unit character varying(16) NOT NULL,
        source_snapshot_json jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_reference_preparation_components" PRIMARY KEY (id),
        CONSTRAINT ck_reference_components_source CHECK (preparation_id <> source_preparation_id AND source_type IN ('Stock','Intermedia')),
        CONSTRAINT ck_reference_components_volume CHECK (volume_taken > 0 AND volume_unit IN ('mL','L') AND position >= 0),
        CONSTRAINT "FK_reference_preparation_components_reference_preparations_pre~" FOREIGN KEY (preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_preparation_components_reference_preparations_sou~" FOREIGN KEY (source_preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE INDEX "IX_reference_preparations_diluted_preparation_id" ON reference_preparations (diluted_preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE INDEX "IX_reference_preparations_method_id" ON reference_preparations (method_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE INDEX "IX_reference_preparations_updated_by_user_id" ON reference_preparations (updated_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_dates CHECK (expiration_date >= preparation_date AND (kind <> 'Stock' OR preparation_date <= source_expiration_date) AND updated_at >= created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_kind_status CHECK (kind IN ('Stock','Intermedia') AND status = 'Active' AND (kind <> 'Stock' OR source_material_id IS NOT NULL) AND (kind <> 'Intermedia' OR (source_material_id IS NULL AND method_id IS NOT NULL)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_purity CHECK (kind <> 'Stock' OR (purity_percent_used > 0 AND purity_percent_used <= 100));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_quantities CHECK (final_volume > 0 AND available_volume >= 0 AND available_volume <= final_volume AND (kind <> 'Stock' OR (target_concentration > 0 AND actual_concentration > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight)));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT ck_reference_preparations_units CHECK (final_volume_unit IN ('mL','L') AND (kind <> 'Stock' OR (concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug'))));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE UNIQUE INDEX "IX_reference_material_movements_component_id" ON reference_material_movements (component_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE INDEX "IX_reference_material_movements_source_preparation_id" ON reference_material_movements (source_preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE UNIQUE INDEX ux_reference_material_movements_preparation ON reference_material_movements (preparation_id) WHERE kind = 'StockConsumption';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD CONSTRAINT ck_reference_material_movements_kind CHECK ((kind = 'StockConsumption' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = 'IntermediateConsumption' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE UNIQUE INDEX "IX_reference_preparation_components_preparation_id_position" ON reference_preparation_components (preparation_id, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE UNIQUE INDEX "IX_reference_preparation_components_preparation_id_source_prep~" ON reference_preparation_components (preparation_id, source_preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE INDEX "IX_reference_preparation_components_source_preparation_id" ON reference_preparation_components (source_preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD CONSTRAINT "FK_reference_material_movements_reference_preparation_componen~" FOREIGN KEY (component_id) REFERENCES reference_preparation_components (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD CONSTRAINT "FK_reference_material_movements_reference_preparations_prepara~" FOREIGN KEY (preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_material_movements ADD CONSTRAINT "FK_reference_material_movements_reference_preparations_source_~" FOREIGN KEY (source_preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT "FK_reference_preparations_reference_methods_method_id" FOREIGN KEY (method_id) REFERENCES reference_methods (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT "FK_reference_preparations_reference_preparations_diluted_prepa~" FOREIGN KEY (diluted_preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    ALTER TABLE reference_preparations ADD CONSTRAINT "FK_reference_preparations_usuarios_updated_by_user_id" FOREIGN KEY (updated_by_user_id) REFERENCES usuarios (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    CREATE FUNCTION check_reference_consumption() RETURNS trigger LANGUAGE plpgsql AS $body$
    DECLARE p reference_preparations; c reference_preparation_components; s reference_preparations;
    BEGIN
        SELECT * INTO STRICT p FROM reference_preparations WHERE id = NEW.preparation_id;
        IF NEW.actor_user_id <> p.prepared_by_user_id THEN
            RAISE EXCEPTION 'Consumption actor does not match preparation' USING ERRCODE = '23514';
        END IF;
        IF NEW.kind = 'StockConsumption' THEN
            IF p.kind <> 'Stock' OR NEW.source_material_id IS DISTINCT FROM p.source_material_id
               OR NEW.quantity <> p.actual_weight OR NEW.unit <> p.source_unit THEN
                RAISE EXCEPTION 'Stock consumption does not match preparation' USING ERRCODE = '23514';
            END IF;
        ELSE
            SELECT * INTO STRICT c FROM reference_preparation_components WHERE id = NEW.component_id;
            SELECT * INTO STRICT s FROM reference_preparations WHERE id = NEW.source_preparation_id;
            IF p.kind <> 'Intermedia' OR c.preparation_id <> p.id OR c.source_preparation_id <> s.id
               OR c.source_type <> s.kind OR NEW.unit <> s.final_volume_unit
               OR NEW.quantity <> c.volume_taken * (CASE c.volume_unit WHEN 'L' THEN 1000 ELSE 1 END)
                  / (CASE s.final_volume_unit WHEN 'L' THEN 1000 ELSE 1 END) THEN
                RAISE EXCEPTION 'Component consumption does not match preparation' USING ERRCODE = '23514';
            END IF;
        END IF;
        RETURN NEW;
    END $body$;
    CREATE CONSTRAINT TRIGGER ck_reference_consumption_link
    AFTER INSERT OR UPDATE ON reference_material_movements DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION check_reference_consumption();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007230730_AddIntermediatePreparations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007230730_AddIntermediatePreparations', '10.0.10');
    END IF;
END $EF$;
COMMIT;

