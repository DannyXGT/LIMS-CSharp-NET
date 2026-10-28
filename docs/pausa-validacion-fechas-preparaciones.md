# Pausa temporal de las validaciones de fechas en Preparaciones

`HistoricalEntryPolicy.EnforcePreparationDates` está en `false`. Centraliza la pausa para Stock preparado, Intermedia preparada, Curva, AQS y los ingresos comerciales de Stock/Intermedia. Se conservan las condiciones originales para reactivarlas después.

Se pausan las comparaciones con hoy, el orden preparación/expiración, la vigencia de los orígenes y del estándar interno, y los filtros que ocultaban fuentes vencidas o con preparación futura. En preparaciones comerciales también se pausa el orden ingreso/expiración y la comparación de apertura con el ingreso o con hoy. Esto incluye el mensaje «La fecha de preparación queda fuera de la vigencia del origen».

Las fechas se guardan tal como se ingresan y siguen siendo campos requeridos. Las etiquetas de vencimiento y el historial conservan la información real. El permiso, método, estado de archivo, apertura documentada cuando corresponde, saldo, concentración, aforo, versión, idempotencia y auditoría siguen funcionando. La cronología de auditoría `updated_at >= created_at` permanece en PostgreSQL. El formulario normal de Estándares conserva su regla de ingreso/expiración.

## Aplicación en la instalación

Se requiere actualizar **cliente y API** y aplicar la migración **20261028045900_PausePreparationDateValidation**. La corrección local no publica servicios ni ejecuta SQL en InterDB.

1. Revisar `artifacts/sql/preflight_preparation_date_validation.sql`, que solo consulta historia y restricciones.
2. Confirmar la cadena previa hasta **20261024173757_AllowHistoricalStockDates**. No insertar historia manualmente ni saltar migraciones pendientes.
3. Aplicar el artefacto revisado `artifacts/sql/pause_preparation_date_validation.sql`, o la migración EF equivalente, y publicar los binarios compatibles.

El SQL incluye su propia transacción `BEGIN`/`COMMIT`, es idempotente y se detiene si faltan prerrequisitos o si la historia no coincide con las restricciones esperadas. Modifica únicamente las dos restricciones de fechas y registra su migración; no reescribe fechas ni saldos.

## Reactivación posterior

Cambiar `EnforcePreparationDates` a `true` y recompilar/publicar API y cliente reactiva las comparaciones conservadas. La operación `Down` de la nueva migración restaura las restricciones de base de datos que había antes de esta pausa. Antes de ejecutarla, revisar los registros que se ingresaron con fechas invertidas: PostgreSQL rechazará la restauración si incumplen esas reglas. No se deben corregir fechas ni borrar registros automáticamente. El tratamiento de las fechas Stock previamente liberadas tiene su migración anterior independiente.

## Verificación local

Las pruebas cubren el caso de Intermedia anterior a su origen, fechas futuras, expiración anterior a preparación/ingreso, fuentes vencidas, estándar interno con fechas fuera de vigencia, fechas guardadas sin alteración, consumo compartido y reactivación de reglas de dominio. También comprueban el SQL de despliegue dos veces, sus prerrequisitos, el rechazo de una reversión incompatible y la permanencia de la auditoría. PostgreSQL se ejecuta exclusivamente en el clúster aislado de pruebas de este repositorio.
