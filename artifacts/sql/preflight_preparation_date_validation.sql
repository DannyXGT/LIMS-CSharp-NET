-- Read-only inspection. Does not change records, constraints or migration history.
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT conrelid::regclass AS table_name, conname, pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE conname IN ('ck_reference_preparations_dates', 'ck_reference_materials_dates');
SELECT kind, count(*) AS inverted_dates FROM reference_preparations
WHERE expiration_date < preparation_date GROUP BY kind;
SELECT presentation, count(*) AS inverted_dates FROM reference_materials
WHERE expiration_date < received_date GROUP BY presentation;
