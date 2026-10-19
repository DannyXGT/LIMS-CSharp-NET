-- Read-only diagnostics. In an aborted session run ROLLBACK; first.
BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name,
       current_user AS database_user,
       current_schema() AS default_schema,
       current_setting('search_path') AS search_path;

WITH required(table_name) AS (VALUES
    ('__EFMigrationsHistory'), ('usuarios'), ('reference_materials'),
    ('reference_units'), ('reference_preparations'),
    ('reference_preparation_components'), ('reference_material_movements'),
    ('reference_solvents'), ('stock_solvents'),
    ('reference_certified_components'), ('reference_material_opening_history'),
    ('reference_commercial_roles'))
SELECT r.table_name, n.nspname AS actual_schema,
       pg_catalog.pg_table_is_visible(c.oid) AS visible_in_search_path,
       pg_catalog.to_regclass(format('%I', r.table_name))::text AS resolved_relation
FROM required r
LEFT JOIN pg_catalog.pg_class c ON c.relname = r.table_name AND c.relkind IN ('r', 'p')
LEFT JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
ORDER BY r.table_name, n.nspname;

SELECT n.nspname AS actual_schema, c.relname AS table_name,
       k.conname AS constraint_name, pg_catalog.pg_get_constraintdef(k.oid) AS definition
FROM pg_catalog.pg_constraint k
JOIN pg_catalog.pg_class c ON c.oid = k.conrelid
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
WHERE k.conname IN ('ck_reference_components_source', 'ck_reference_materials_purity',
    'ck_reference_material_movements_kind', 'ck_reference_preparations_kind_status')
ORDER BY c.relname, n.nspname, k.conname;

DO $diagnostic$
DECLARE history record; migration record;
BEGIN
    FOR history IN
        SELECT n.nspname FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = '__EFMigrationsHistory' AND c.relkind IN ('r', 'p')
        ORDER BY n.nspname
    LOOP
        RAISE NOTICE 'Migration history schema: %', history.nspname;
        FOR migration IN EXECUTE format(
            'SELECT "MigrationId", "ProductVersion" FROM %I."__EFMigrationsHistory" ORDER BY "MigrationId"', history.nspname)
        LOOP
            RAISE NOTICE '  % | %', migration."MigrationId", migration."ProductVersion";
        END LOOP;
    END LOOP;
END $diagnostic$;

COMMIT;
