-- SELECT-only preflight. Run against the intended database before reviewing the migration.
BEGIN READ ONLY;
SET LOCAL search_path TO public;

SELECT current_database() AS database_name;

SELECT column_name, data_type, numeric_precision, numeric_scale
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'reference_preparations'
  AND column_name IN ('calculated_weight', 'actual_concentration')
ORDER BY column_name;

SELECT "MigrationId" FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%ReferenceStock'
   OR "MigrationId" LIKE '%IntermediatePreparations'
   OR "MigrationId" LIKE '%PreservePreparationDecimalScale'
ORDER BY "MigrationId";

-- Expected: no rows. Values are examined as numeric/text, never converted to .NET Decimal.
SELECT p.code, p.kind, v.column_name, v.value::text AS stored_value
FROM reference_preparations p
CROSS JOIN LATERAL (VALUES
    ('calculated_weight', p.calculated_weight),
    ('actual_concentration', p.actual_concentration)
) v(column_name, value)
WHERE v.value::text IN ('NaN', 'Infinity', '-Infinity')
   OR scale(trim_scale(v.value)) > 28
   OR abs(trim_scale(v.value)) * power(10::numeric, scale(trim_scale(v.value)))
      > 79228162514264337593543950335::numeric;

ROLLBACK;
