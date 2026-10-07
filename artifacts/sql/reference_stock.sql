START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE SEQUENCE reference_stock_code_sequence START WITH 1 INCREMENT BY 1 NO CYCLE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE TABLE reference_preparations (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        name character varying(200) NOT NULL,
        kind character varying(24) NOT NULL,
        status character varying(24) NOT NULL,
        source_material_id uuid NOT NULL,
        source_version uuid NOT NULL,
        source_name character varying(200) NOT NULL,
        source_lot character varying(120) NOT NULL,
        source_cas_number character varying(80),
        source_catalog_number character varying(80),
        source_brand character varying(120) NOT NULL,
        source_method character varying(200) NOT NULL,
        source_location character varying(200) NOT NULL,
        source_expiration_date date NOT NULL,
        source_total_quantity numeric(18,6) NOT NULL,
        purity_percent_used numeric(7,4) NOT NULL,
        source_unit character varying(16) NOT NULL,
        target_concentration numeric(18,6) NOT NULL,
        actual_concentration numeric(40,28) NOT NULL,
        concentration_unit character varying(16) NOT NULL,
        final_volume numeric(18,6) NOT NULL,
        final_volume_unit character varying(16) NOT NULL,
        calculated_weight numeric(40,28) NOT NULL,
        actual_weight numeric(18,6) NOT NULL,
        formula character varying(240) NOT NULL,
        preparation_date date NOT NULL,
        expiration_date date NOT NULL,
        storage_temperature character varying(200),
        calculated_weight_unit character varying(16) NOT NULL,
        actual_weight_unit character varying(16) NOT NULL,
        actual_concentration_unit character varying(16) NOT NULL,
        prepared_by_user_id integer NOT NULL,
        notes character varying(2000),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        version uuid NOT NULL,
        request_fingerprint character varying(64) NOT NULL,
        CONSTRAINT "PK_reference_preparations" PRIMARY KEY (id),
        CONSTRAINT ak_reference_preparations_consumption UNIQUE (id, source_material_id, prepared_by_user_id, actual_weight, source_unit),
        CONSTRAINT ck_reference_preparations_dates CHECK (expiration_date >= preparation_date AND preparation_date <= source_expiration_date AND updated_at >= created_at),
        CONSTRAINT ck_reference_preparations_identity CHECK (length(trim(code)) > 0 AND length(trim(name)) > 0 AND length(request_fingerprint) = 64),
        CONSTRAINT ck_reference_preparations_kind_status CHECK (kind = 'Stock' AND status = 'Active'),
        CONSTRAINT ck_reference_preparations_purity CHECK (purity_percent_used > 0 AND purity_percent_used <= 100),
        CONSTRAINT ck_reference_preparations_quantities CHECK (target_concentration > 0 AND actual_concentration > 0 AND final_volume > 0 AND calculated_weight > 0 AND actual_weight > 0 AND source_total_quantity >= actual_weight),
        CONSTRAINT ck_reference_preparations_units CHECK (concentration_unit IN ('mg/L','g/L','µg/L','µg/mL') AND final_volume_unit IN ('mL','L') AND calculated_weight_unit = source_unit AND actual_weight_unit = source_unit AND actual_concentration_unit = concentration_unit AND source_unit IN ('g','mg','µg','μg','ug')),
        CONSTRAINT "FK_reference_preparations_reference_materials_source_material_~" FOREIGN KEY (source_material_id) REFERENCES reference_materials (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_preparations_usuarios_prepared_by_user_id" FOREIGN KEY (prepared_by_user_id) REFERENCES usuarios (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE TABLE reference_material_movements (
        id uuid NOT NULL,
        preparation_id uuid NOT NULL,
        source_material_id uuid NOT NULL,
        kind character varying(24) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        unit character varying(16) NOT NULL,
        balance_before numeric(18,6) NOT NULL,
        balance_after numeric(18,6) NOT NULL,
        actor_user_id integer NOT NULL,
        occurred_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_reference_material_movements" PRIMARY KEY (id),
        CONSTRAINT ck_reference_material_movements_balance CHECK (quantity > 0 AND balance_before >= quantity AND balance_after >= 0 AND balance_after = balance_before - quantity),
        CONSTRAINT ck_reference_material_movements_kind CHECK (kind = 'StockConsumption'),
        CONSTRAINT "FK_reference_material_movements_reference_materials_source_mat~" FOREIGN KEY (source_material_id) REFERENCES reference_materials (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_material_movements_reference_preparations_prepara~" FOREIGN KEY (preparation_id, source_material_id, actor_user_id, quantity, unit) REFERENCES reference_preparations (id, source_material_id, prepared_by_user_id, actual_weight, source_unit) ON DELETE RESTRICT,
        CONSTRAINT "FK_reference_material_movements_usuarios_actor_user_id" FOREIGN KEY (actor_user_id) REFERENCES usuarios (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX "IX_reference_material_movements_actor_user_id" ON reference_material_movements (actor_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX "IX_reference_material_movements_preparation_id_source_material~" ON reference_material_movements (preparation_id, source_material_id, actor_user_id, quantity, unit);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX ix_reference_material_movements_source_time ON reference_material_movements (source_material_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE UNIQUE INDEX ux_reference_material_movements_preparation ON reference_material_movements (preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX ix_reference_preparations_actor ON reference_preparations (prepared_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX ix_reference_preparations_date ON reference_preparations (preparation_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE INDEX ix_reference_preparations_source ON reference_preparations (source_material_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    CREATE UNIQUE INDEX ux_reference_preparations_code ON reference_preparations (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007045455_CreateReferenceStock') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007045455_CreateReferenceStock', '10.0.10');
    END IF;
END $EF$;
COMMIT;

