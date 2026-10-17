START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    ALTER TABLE reference_preparations ADD composition_basis character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    ALTER TABLE reference_preparations ADD compound character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE TABLE reference_solvents (
        id integer NOT NULL,
        name character varying(200) NOT NULL,
        abbreviation character varying(32) NOT NULL,
        is_active boolean NOT NULL,
        display_order integer NOT NULL,
        CONSTRAINT "PK_reference_solvents" PRIMARY KEY (id),
        CONSTRAINT ck_reference_solvents_identity CHECK (length(trim(name)) > 0 AND length(trim(abbreviation)) > 0 AND display_order >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE TABLE stock_solvents (
        id uuid NOT NULL,
        preparation_id uuid NOT NULL,
        solvent_id integer NOT NULL,
        percent numeric(7,4) NOT NULL,
        position integer NOT NULL,
        CONSTRAINT "PK_stock_solvents" PRIMARY KEY (id),
        CONSTRAINT ck_stock_solvents_percent CHECK (percent > 0 AND percent <= 100 AND position >= 0),
        CONSTRAINT "FK_stock_solvents_reference_preparations_preparation_id" FOREIGN KEY (preparation_id) REFERENCES reference_preparations (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_stock_solvents_reference_solvents_solvent_id" FOREIGN KEY (solvent_id) REFERENCES reference_solvents (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (1, 'THF', 0, TRUE, 'Tetrahidrofurano');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (2, 'Hxn', 1, TRUE, 'Hexano');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (3, 'ACN', 2, TRUE, 'Acetonitrilo');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (4, 'MeOH', 3, TRUE, 'Metanol');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (5, 'Tolueno', 4, TRUE, 'Tolueno');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (6, 'MTBE', 5, TRUE, 'Éter metil tert-butílico');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (7, 'Cyclohexano', 6, TRUE, 'Ciclohexano');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (8, 'H2O', 7, TRUE, 'Agua');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (9, 'HNO3', 8, TRUE, 'Ácido nítrico');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (10, 'HCl', 9, TRUE, 'Ácido clorhídrico');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (11, 'Acetona', 10, TRUE, 'Acetona');
    INSERT INTO reference_solvents (id, abbreviation, display_order, is_active, name)
    VALUES (12, 'DMSO', 11, TRUE, 'Dimetilsulfóxido');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE UNIQUE INDEX "IX_reference_solvents_abbreviation" ON reference_solvents (abbreviation);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE UNIQUE INDEX "IX_stock_solvents_preparation_id_position" ON stock_solvents (preparation_id, position);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE UNIQUE INDEX "IX_stock_solvents_preparation_id_solvent_id" ON stock_solvents (preparation_id, solvent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    CREATE INDEX "IX_stock_solvents_solvent_id" ON stock_solvents (solvent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017031716_AddStockSolventComposition') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261017031716_AddStockSolventComposition', '10.0.10');
    END IF;
END $EF$;
COMMIT;

