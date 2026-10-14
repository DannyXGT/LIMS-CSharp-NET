START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013065016_PreservePreparationDecimalScale') THEN
    DO $decimal_guard$
    BEGIN
        IF EXISTS (
            SELECT 1 FROM reference_preparations p
            CROSS JOIN LATERAL (VALUES (p.calculated_weight), (p.actual_concentration)) v(value)
            WHERE v.value::text IN ('NaN', 'Infinity', '-Infinity')
               OR scale(trim_scale(v.value)) > 28
               OR abs(trim_scale(v.value)) * power(10::numeric, scale(trim_scale(v.value)))
                  > 79228162514264337593543950335::numeric
        ) THEN
            RAISE EXCEPTION 'Preparaciones contiene un valor no representable como Decimal. Revise los datos antes de aplicar esta migración.'
                USING ERRCODE = '22003';
        END IF;
    END $decimal_guard$;

    ALTER TABLE reference_preparations
        ALTER COLUMN calculated_weight TYPE numeric USING trim_scale(calculated_weight),
        ALTER COLUMN actual_concentration TYPE numeric USING trim_scale(actual_concentration);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013065016_PreservePreparationDecimalScale') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261013065016_PreservePreparationDecimalScale', '10.0.10');
    END IF;
END $EF$;
COMMIT;

