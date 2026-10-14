# Corrección de lectura Decimal en Preparaciones

El log con referencia `4d8f55f4d3944f488c74ac32aa52ec73` muestra un
`OverflowException` en Npgsql, al materializar `StockRepository.GetAsync` dentro
de la transacción de creación. Se reprodujo con pureza 99,11 %, objetivo
100 mg/L, aforo 1000 mL y peso medido 100 mg. El peso teórico periódico puede
representarse como Decimal, pero `numeric(40,28)` agrega ceros hasta una escala
fija que desborda el coeficiente intermedio utilizado por el lector de Npgsql.

Se cambian únicamente `calculated_weight` y `actual_concentration` a `numeric`
sin escala fija. Cada escritura conserva la escala real de su Decimal.
La migración usa `trim_scale` para quitar ceros finales de filas existentes,
sin redondear ni cambiar el valor numérico. Pesos medidos, saldos y movimientos
conservan su modelo y precisión. Un guard rechaza valores históricos que no
puedan representarse exactamente como Decimal, para exigir su revisión.

- Migración EF: `20261013065016_PreservePreparationDecimalScale`.
- Preflight de solo lectura: `artifacts/sql/preflight_reference_preparation_decimal_scale.sql`.
- SQL idempotente de esta corrección: `artifacts/sql/reference_preparation_decimal_scale.sql`.
  Contiene exclusivamente esta migración; no aplica las migraciones de permisos.

Antes de aplicar en InterDB, revisar el preflight, contar con respaldo y autorización.
El cambio se prueba con PostgreSQL aislado, incluyendo la excepción previa, rollback,
igualdad exacta de resultados, datos históricos, lectura/escritura con el modelo
anterior de la API, reutilización en Intermedia y ejecución idempotente del SQL.
La validación local no sustituye la confirmación de guardado en el servidor desplegado.

Después de recoger el error, desactivar nuevamente `stdoutLogEnabled` en IIS;
ese diagnóstico temporal no tiene rotación automática.
