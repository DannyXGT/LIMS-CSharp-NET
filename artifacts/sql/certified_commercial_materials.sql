-- AddCertifiedCommercialMaterials: transactional reconciliation of this migration only.
-- Run the entire file. Existing objects are verified, not treated as migration proof.
-- No material, component or movement rows are inserted, updated or deleted.
START TRANSACTION;
SET LOCAL lock_timeout TO '10s';
SET LOCAL statement_timeout TO '5min';

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $preflight$
DECLARE missing text; app_schema oid;
BEGIN
    SELECT string_agg(name, ', ' ORDER BY name) INTO missing
    FROM (VALUES ('reference_materials'), ('reference_preparations'),
        ('reference_preparation_components'), ('reference_material_movements'),
        ('reference_units'), ('usuarios')) AS required(name)
    WHERE to_regclass(format('%I', name)) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION USING ERRCODE = '55000',
            MESSAGE = 'Missing commercial migration prerequisites in the current search_path: ' || missing,
            HINT = 'Run preflight_commercial_materials.sql and reconcile the base Stock/Intermedia schema first.';
    END IF;
    SELECT relnamespace INTO app_schema FROM pg_class WHERE oid = 'reference_materials'::regclass;
    IF EXISTS (SELECT 1 FROM pg_class WHERE oid IN ('reference_preparations'::regclass,
        'reference_preparation_components'::regclass, 'reference_material_movements'::regclass,
        'reference_units'::regclass, 'usuarios'::regclass, '"__EFMigrationsHistory"'::regclass)
        AND relnamespace <> app_schema) THEN
        RAISE EXCEPTION 'Application tables and migration history resolve to different schemas; correct search_path first';
    END IF;
END $preflight$;

LOCK TABLE reference_materials, reference_preparations,
    reference_preparation_components, reference_material_movements IN ACCESS EXCLUSIVE MODE;
CREATE TEMP TABLE __lims_cm_control ON COMMIT DROP AS
SELECT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials') AS applied;

-- A recorded migration is validation-only. Missing objects must not be silently
-- recreated when the history claims they already exist.
DO $apply$
BEGIN
    IF NOT (SELECT applied FROM pg_temp.__lims_cm_control) THEN
        ALTER TABLE reference_preparation_components ALTER COLUMN source_preparation_id DROP NOT NULL;
        ALTER TABLE reference_preparation_components ADD COLUMN IF NOT EXISTS source_material_id uuid;
        ALTER TABLE reference_materials ALTER COLUMN purity_percent DROP NOT NULL;
        ALTER TABLE reference_materials ADD COLUMN IF NOT EXISTS commercial_json jsonb;
        ALTER TABLE reference_materials ADD COLUMN IF NOT EXISTS effective_expiration_date date;
        ALTER TABLE reference_materials ADD COLUMN IF NOT EXISTS opened_date date;
        ALTER TABLE reference_materials ADD COLUMN IF NOT EXISTS opening_required boolean NOT NULL DEFAULT FALSE;
        ALTER TABLE reference_materials ADD COLUMN IF NOT EXISTS presentation character varying(24) NOT NULL DEFAULT 'Solid';
        CREATE TABLE IF NOT EXISTS reference_certified_components (
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
        CREATE TABLE IF NOT EXISTS reference_material_opening_history (
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
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_reference_preparation_components_preparation_id_source_mate~" ON reference_preparation_components (preparation_id, source_material_id);
        CREATE INDEX IF NOT EXISTS "IX_reference_preparation_components_source_material_id" ON reference_preparation_components (source_material_id);
        CREATE INDEX IF NOT EXISTS "IX_reference_certified_components_material_id" ON reference_certified_components (material_id);
        CREATE INDEX IF NOT EXISTS "IX_reference_material_opening_history_actor_user_id" ON reference_material_opening_history (actor_user_id);
        CREATE INDEX IF NOT EXISTS "IX_reference_material_opening_history_material_id" ON reference_material_opening_history (material_id);
    END IF;
END $apply$;

-- Verify column types/nullability before interpreting any existing constraints.
DO $columns$
DECLARE invalid text;
BEGIN
    WITH expected(table_name, column_name, sql_type, required) AS (VALUES
        ('reference_preparation_components', 'source_preparation_id', 'uuid', false),
        ('reference_preparation_components', 'source_material_id', 'uuid', false),
        ('reference_materials', 'purity_percent', 'numeric(7,4)', false),
        ('reference_materials', 'commercial_json', 'jsonb', false),
        ('reference_materials', 'effective_expiration_date', 'date', false),
        ('reference_materials', 'opened_date', 'date', false),
        ('reference_materials', 'opening_required', 'boolean', true),
        ('reference_materials', 'presentation', 'character varying(24)', true),
        ('reference_certified_components', 'id', 'uuid', true),
        ('reference_certified_components', 'material_id', 'uuid', true),
        ('reference_certified_components', 'name', 'text', true),
        ('reference_certified_components', 'cas_number', 'text', false),
        ('reference_certified_components', 'certified_concentration', 'numeric', true),
        ('reference_certified_components', 'concentration_unit', 'text', true),
        ('reference_certified_components', 'expanded_uncertainty', 'numeric', false),
        ('reference_certified_components', 'purity_percent', 'numeric', false),
        ('reference_certified_components', 'lot', 'text', false),
        ('reference_certified_components', 'additional_data', 'text', false),
        ('reference_material_opening_history', 'id', 'uuid', true),
        ('reference_material_opening_history', 'material_id', 'uuid', true),
        ('reference_material_opening_history', 'previous_date', 'date', false),
        ('reference_material_opening_history', 'opened_date', 'date', false),
        ('reference_material_opening_history', 'actor_user_id', 'integer', true),
        ('reference_material_opening_history', 'changed_at', 'timestamp with time zone', true)
    )
    SELECT string_agg(e.table_name || '.' || e.column_name || ' expected ' || e.sql_type,
        '; ' ORDER BY e.table_name, e.column_name) INTO invalid
    FROM expected e LEFT JOIN pg_attribute a ON a.attrelid = to_regclass(e.table_name)
        AND a.attname = e.column_name AND a.attnum > 0 AND NOT a.attisdropped
    WHERE a.attnum IS NULL OR format_type(a.atttypid, a.atttypmod) <> e.sql_type OR a.attnotnull <> e.required;
    IF invalid IS NOT NULL THEN
        RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'Incompatible or missing commercial columns: ' || invalid,
            HINT = 'No migration history is recorded by this failed transaction. Review the reported schema differences.';
    END IF;
END $columns$;

-- Canonicalize CHECK expressions using an empty temporary clone with the real
-- column types. Only the documented pre-commercial checks may be replaced.
CREATE FUNCTION pg_temp.__lims_cm_check(target regclass, check_name text, expression text, legacy_expression text)
RETURNS void LANGUAGE plpgsql AS $function$
DECLARE actual text; expected text; baseline text; successor text; quantity_check text;
    quantity_constraint record; kind "char"; valid boolean;
    can_change boolean := NOT (SELECT applied FROM pg_temp.__lims_cm_control);
BEGIN
    EXECUTE format('CREATE TEMP TABLE __lims_cm_probe (LIKE %s) ON COMMIT DROP', target);
    EXECUTE format('ALTER TABLE pg_temp.__lims_cm_probe ADD CONSTRAINT expected_check CHECK (%s)', expression);
    SELECT pg_get_expr(conbin, conrelid) INTO expected FROM pg_constraint
        WHERE conrelid = 'pg_temp.__lims_cm_probe'::regclass AND conname = 'expected_check';
    IF legacy_expression IS NOT NULL THEN
        EXECUTE format('ALTER TABLE pg_temp.__lims_cm_probe ADD CONSTRAINT baseline_check CHECK (%s)', legacy_expression);
        SELECT pg_get_expr(conbin, conrelid) INTO baseline FROM pg_constraint
            WHERE conrelid = 'pg_temp.__lims_cm_probe'::regclass AND conname = 'baseline_check';
    END IF;
    -- A later additive migration permits multiple packages. Accept its exact known
    -- expression without replacing it or reducing the physical inventory.
    IF target = 'reference_materials'::regclass AND check_name = 'ck_reference_materials_presentation' THEN
        EXECUTE 'ALTER TABLE pg_temp.__lims_cm_probe ADD CONSTRAINT successor_check CHECK (presentation IN (''Solid'',''CommercialSolution'',''CommercialMix'') AND (presentation = ''Solid'' OR (commercial_json IS NOT NULL AND effective_expiration_date <= expiration_date)))';
        SELECT pg_get_expr(conbin, conrelid) INTO successor FROM pg_constraint
            WHERE conrelid = 'pg_temp.__lims_cm_probe'::regclass AND conname = 'successor_check';
        EXECUTE 'ALTER TABLE pg_temp.__lims_cm_probe ADD CONSTRAINT quantity_check CHECK (presentation_quantity > 0)';
        SELECT pg_get_expr(conbin, conrelid) INTO quantity_check FROM pg_constraint
            WHERE conrelid = 'pg_temp.__lims_cm_probe'::regclass AND conname = 'quantity_check';
    END IF;
    SELECT pg_get_expr(conbin, conrelid), contype, convalidated INTO actual, kind, valid
        FROM pg_constraint WHERE conrelid = target AND conname = check_name;
    IF kind IS NULL THEN
        IF NOT can_change THEN RAISE EXCEPTION 'Recorded migration has missing constraint %.%', target, check_name USING ERRCODE = '55000'; END IF;
        EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I CHECK (%s)', target, check_name, expression);
    ELSIF kind = 'c' AND (actual = expected OR actual = successor) THEN
        IF NOT valid THEN
            IF NOT can_change THEN RAISE EXCEPTION 'Recorded migration has unvalidated constraint %.%', target, check_name USING ERRCODE = '55000'; END IF;
            EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', target, check_name);
        END IF;
    ELSIF can_change AND kind = 'c' AND quantity_check IS NOT NULL AND actual = quantity_check THEN
        -- Known legacy naming collision: this name protects positive quantity,
        -- not the commercial presentation. Retain that protection separately.
        SELECT contype, convalidated, pg_get_expr(conbin, conrelid) AS expression
            INTO quantity_constraint FROM pg_constraint
            WHERE conrelid = target AND conname = 'ck_reference_materials_presentation_quantity';
        IF NOT FOUND THEN
            IF NOT valid THEN
                EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', target, check_name);
            END IF;
            EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO ck_reference_materials_presentation_quantity', target, check_name);
        ELSE
            IF quantity_constraint.contype <> 'c' OR quantity_constraint.expression IS DISTINCT FROM quantity_check THEN
                RAISE EXCEPTION 'Incompatible positive-quantity constraint on %; legacy constraint retained', target USING ERRCODE = '55000';
            END IF;
            IF NOT quantity_constraint.convalidated THEN
                EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT ck_reference_materials_presentation_quantity', target);
            END IF;
            EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', target, check_name);
        END IF;
        EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I CHECK (%s)', target, check_name, expression);
    ELSIF can_change AND kind = 'c' AND baseline IS NOT NULL AND actual = baseline THEN
        EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', target, check_name);
        EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I CHECK (%s)', target, check_name, expression);
    ELSE
        RAISE EXCEPTION USING ERRCODE = '55000',
            MESSAGE = format('Incompatible check constraint %s.%s; existing definition was not replaced', target, check_name),
            DETAIL = format('Existing expression: %s; expected expression: %s', coalesce(actual, '<not a CHECK expression>'), expected),
            HINT = 'After ROLLBACK, query pg_get_constraintdef for this table and constraint. Reconcile its actual definition before retrying; existing data and constraints have not been replaced by this check.';
    END IF;
    DROP TABLE pg_temp.__lims_cm_probe;
END $function$;

SELECT pg_temp.__lims_cm_check('reference_preparation_components'::regclass, 'ck_reference_components_source', '(source_preparation_id IS NOT NULL AND source_material_id IS NULL AND preparation_id <> source_preparation_id AND source_type IN (''Stock'',''Intermedia'')) OR (source_preparation_id IS NULL AND source_material_id IS NOT NULL AND source_type IN (''CommercialSolution'',''CommercialMix''))', 'preparation_id <> source_preparation_id AND source_type IN (''Stock'',''Intermedia'')');
SELECT pg_temp.__lims_cm_check('reference_materials'::regclass, 'ck_reference_materials_presentation', 'presentation IN (''Solid'',''CommercialSolution'',''CommercialMix'') AND (presentation = ''Solid'' OR (commercial_json IS NOT NULL AND package_count = 1 AND effective_expiration_date <= expiration_date))', NULL);
SELECT pg_temp.__lims_cm_check('reference_materials'::regclass, 'ck_reference_materials_purity', '(presentation = ''Solid'' AND purity_percent IS NOT NULL AND purity_percent > 0 AND purity_percent <= 100) OR (presentation <> ''Solid'' AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100)))', 'purity_percent > 0 AND purity_percent <= 100');
SELECT pg_temp.__lims_cm_check('reference_material_movements'::regclass, 'ck_reference_material_movements_kind', '(kind = ''StockConsumption'' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = ''IntermediateConsumption'' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL) OR (kind = ''CommercialConsumption'' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NOT NULL)', '(kind = ''StockConsumption'' AND source_material_id IS NOT NULL AND source_preparation_id IS NULL AND component_id IS NULL) OR (kind = ''IntermediateConsumption'' AND source_material_id IS NULL AND source_preparation_id IS NOT NULL AND component_id IS NOT NULL)');
SELECT pg_temp.__lims_cm_check('reference_certified_components'::regclass, 'ck_certified_component_values', 'certified_concentration > 0 AND (expanded_uncertainty IS NULL OR expanded_uncertainty >= 0) AND (purity_percent IS NULL OR (purity_percent > 0 AND purity_percent <= 100))', NULL);

CREATE FUNCTION pg_temp.__lims_cm_fk(target regclass, fk_name text, local_column text, referenced regclass, referenced_column text, delete_action text)
RETURNS void LANGUAGE plpgsql AS $function$
DECLARE existing pg_constraint; local_id smallint; referenced_id smallint;
    can_change boolean := NOT (SELECT applied FROM pg_temp.__lims_cm_control);
BEGIN
    SELECT attnum INTO local_id FROM pg_attribute WHERE attrelid = target AND attname = local_column AND NOT attisdropped;
    SELECT attnum INTO referenced_id FROM pg_attribute WHERE attrelid = referenced AND attname = referenced_column AND NOT attisdropped;
    SELECT * INTO existing FROM pg_constraint WHERE conrelid = target AND conname = fk_name;
    IF existing.oid IS NULL THEN
        IF NOT can_change THEN RAISE EXCEPTION 'Recorded migration has missing foreign key %.%', target, fk_name USING ERRCODE = '55000'; END IF;
        EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I FOREIGN KEY (%I) REFERENCES %s (%I) ON DELETE %s',
            target, fk_name, local_column, referenced, referenced_column, delete_action);
    ELSE
        IF existing.contype <> 'f' OR existing.confrelid <> referenced OR existing.conkey <> ARRAY[local_id]
            OR existing.confkey <> ARRAY[referenced_id] OR existing.confupdtype <> 'a' OR existing.confmatchtype <> 's'
            OR existing.confdeltype::text <> (CASE delete_action WHEN 'CASCADE' THEN 'c' ELSE 'r' END)
            OR existing.condeferrable OR existing.condeferred THEN
            RAISE EXCEPTION 'Incompatible foreign key %.%; existing definition was not replaced', target, fk_name USING ERRCODE = '55000';
        END IF;
        IF NOT existing.convalidated THEN
            IF NOT can_change THEN RAISE EXCEPTION 'Recorded migration has unvalidated foreign key %.%', target, fk_name USING ERRCODE = '55000'; END IF;
            EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', target, fk_name);
        END IF;
    END IF;
END $function$;

SELECT pg_temp.__lims_cm_fk('reference_certified_components'::regclass, 'FK_reference_certified_components_reference_materials_material~', 'material_id', 'reference_materials'::regclass, 'id', 'CASCADE');
SELECT pg_temp.__lims_cm_fk('reference_material_opening_history'::regclass, 'FK_reference_material_opening_history_reference_materials_mate~', 'material_id', 'reference_materials'::regclass, 'id', 'RESTRICT');
SELECT pg_temp.__lims_cm_fk('reference_material_opening_history'::regclass, 'FK_reference_material_opening_history_usuarios_actor_user_id', 'actor_user_id', 'usuarios'::regclass, 'id', 'RESTRICT');
SELECT pg_temp.__lims_cm_fk('reference_preparation_components'::regclass, 'FK_reference_preparation_components_reference_materials_source~', 'source_material_id', 'reference_materials'::regclass, 'id', 'RESTRICT');

DO $verify$
DECLARE invalid text; pk record; rel regclass; id_attribute smallint; default_expression text;
    can_change boolean := NOT (SELECT applied FROM pg_temp.__lims_cm_control);
BEGIN
    -- Both new tables need a real, non-deferrable primary key on id.
    FOREACH rel IN ARRAY ARRAY['reference_certified_components'::regclass, 'reference_material_opening_history'::regclass] LOOP
        SELECT attnum INTO id_attribute FROM pg_attribute WHERE attrelid = rel AND attname = 'id' AND NOT attisdropped;
        SELECT * INTO pk FROM pg_constraint WHERE conrelid = rel AND contype = 'p';
        IF NOT FOUND THEN
            IF NOT can_change THEN RAISE EXCEPTION 'Recorded migration has missing primary key on %', rel USING ERRCODE = '55000'; END IF;
            EXECUTE format('ALTER TABLE %s ADD PRIMARY KEY (id)', rel);
        ELSIF pk.conkey <> ARRAY[id_attribute] OR pk.condeferrable OR NOT pk.convalidated THEN
            RAISE EXCEPTION 'Incompatible primary key on %', rel USING ERRCODE = '55000';
        END IF;
    END LOOP;

    -- Missing defaults can be completed only before recording this migration.
    SELECT pg_get_expr(d.adbin, d.adrelid) INTO default_expression FROM pg_attrdef d
        JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum
        WHERE a.attrelid = 'reference_materials'::regclass AND a.attname = 'presentation';
    IF default_expression IS NULL AND can_change THEN
        ALTER TABLE reference_materials ALTER COLUMN presentation SET DEFAULT 'Solid';
    ELSIF default_expression IS NULL OR default_expression NOT IN ('''Solid''', '''Solid''::character varying', '''Solid''::text') THEN
        RAISE EXCEPTION 'Incompatible presentation default: %', default_expression USING ERRCODE = '55000';
    END IF;
    SELECT pg_get_expr(d.adbin, d.adrelid) INTO default_expression FROM pg_attrdef d
        JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum
        WHERE a.attrelid = 'reference_materials'::regclass AND a.attname = 'opening_required';
    IF default_expression IS NULL AND can_change THEN
        ALTER TABLE reference_materials ALTER COLUMN opening_required SET DEFAULT false;
    ELSIF default_expression IS NULL OR default_expression <> 'false' THEN
        RAISE EXCEPTION 'Incompatible opening_required default: %', default_expression USING ERRCODE = '55000';
    END IF;

    WITH expected(table_name, index_name, columns, unique_index) AS (VALUES
        ('reference_preparation_components', 'IX_reference_preparation_components_preparation_id_source_mate~', ARRAY['preparation_id', 'source_material_id']::text[], true),
        ('reference_preparation_components', 'IX_reference_preparation_components_source_material_id', ARRAY['source_material_id']::text[], false),
        ('reference_certified_components', 'IX_reference_certified_components_material_id', ARRAY['material_id']::text[], false),
        ('reference_material_opening_history', 'IX_reference_material_opening_history_actor_user_id', ARRAY['actor_user_id']::text[], false),
        ('reference_material_opening_history', 'IX_reference_material_opening_history_material_id', ARRAY['material_id']::text[], false)
    )
    SELECT string_agg(e.index_name, ', ' ORDER BY e.index_name) INTO invalid
    FROM expected e LEFT JOIN pg_class c ON c.oid = to_regclass(format('%I', e.index_name))
        LEFT JOIN pg_index i ON i.indexrelid = c.oid LEFT JOIN pg_am am ON am.oid = c.relam
    WHERE i.indexrelid IS NULL OR i.indrelid <> to_regclass(e.table_name) OR i.indisunique <> e.unique_index
        OR NOT i.indisvalid OR NOT i.indisready OR i.indpred IS NOT NULL OR i.indexprs IS NOT NULL
        OR am.amname <> 'btree' OR i.indnkeyatts <> cardinality(e.columns) OR i.indnatts <> cardinality(e.columns)
        OR (SELECT array_agg(a.attname::text ORDER BY key.position)
            FROM unnest(i.indkey::smallint[]) WITH ORDINALITY AS key(attribute, position)
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = key.attribute) IS DISTINCT FROM e.columns;
    IF invalid IS NOT NULL THEN
        RAISE EXCEPTION 'Missing or incompatible commercial indexes: %', invalid USING ERRCODE = '55000';
    END IF;
END $verify$;

-- This is the only migration ID this file can record, after all verification.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261017063950_AddCertifiedCommercialMaterials', '10.0.10'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261017063950_AddCertifiedCommercialMaterials');
DROP FUNCTION pg_temp.__lims_cm_check(regclass, text, text, text);
DROP FUNCTION pg_temp.__lims_cm_fk(regclass, text, text, regclass, text, text);
COMMIT;
