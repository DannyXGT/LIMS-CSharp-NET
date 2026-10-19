# Soluciones comerciales certificadas y Mix

La ampliación reutiliza Estándares, Stock, Intermedia, Curva y AQS. El frasco comprado se registra una sola vez en `reference_materials`. No se crean preparaciones Stock ni estándares de inventario por analito.

## Registro y trazabilidad

`presentation` distingue `Solid`, `CommercialSolution` y `CommercialMix`. Los registros existentes conservan `Solid` por defecto. La solución individual exige un componente certificado y el Mix exige dos o más, sin un máximo fijo de analitos. Cada frasco comercial se registra como una unidad física, con volumen en mL o L.

Se conservan fabricante, catálogo, lote, método, ingreso, vencimiento del fabricante, almacenamiento, ubicación y responsables existentes. El certificado agrega matriz literal, referencia, trazabilidad, concentración nominal y su unidad, cobertura documentada, apertura y periodo de uso en días o meses. Las expresiones certificadas alternativas permanecen separadas; no se infiere densidad ni la base de una matriz porcentual.

`reference_certified_components` contiene los datos individuales y una FK al frasco. Sus identificadores son propios de cada producto físico, incluso al reemplazar un frasco. No tienen saldo, movimientos ni relación con estándares individuales del catálogo. Los valores certificados usan `numeric` sin escala fija. La metadata del certificado conserva su orden de componentes y las expresiones alternativas; los valores de composición se leen de las filas relacionadas.

El volumen inicial y el saldo se mantienen en `presentation_quantity` y `available_quantity`, con el control de versión existente. La pureza puede ser nula para un producto comercial; no se sustituye por un valor ficticio. Los sólidos siguen requiriendo su pureza documentada.

## Preparaciones y cálculos

El selector de Intermedia permite Stock preparado, solución comercial y Mix comercial. Se conserva la selección de una Intermedia previa para dilución. Curva y AQS incluyen Intermedias y orígenes comerciales compatibles con el método activo.

Un origen comercial entrega identidad del producto, certificado, matriz, fabricante, catálogo, lote, volumen, vigencia y todos sus analitos. Se usa la concentración certificada de cada componente. Las diluciones emplean los calculadores existentes y `StockUnits`, con `decimal`, sin redondear valores intermedios. Las fórmulas de Stock por pesada permanecen intactas. Las expresiones sin conversión volumétrica sustentada, por ejemplo µg/g, se conservan pero no habilitan un origen de dilución volumétrica.

Los componentes de preparación conservan dos posibles FKs: `source_preparation_id` o `source_material_id`. Una restricción exige exactamente una, junto con el tipo compatible. Los movimientos conservan la FK al producto y al componente de preparación. `CommercialConsumption` representa un solo débito físico por frasco; Curva/AQS acumulan primero las alícuotas de sus soluciones que comparten ese origen.

La preparación, sus componentes, movimientos y saldo se confirman en una transacción. Se reutilizan versiones optimistas e idempotencia por solicitud. Una repetición no descuenta dos veces; una escritura fallida o un conflicto concurrente revierte toda la operación. Los snapshots guardan las concentraciones de origen y los resultados históricos.

Los resultados comerciales conservan sus unidades certificadas. El comportamiento anterior de resultados internos en mg/L se mantiene. La incertidumbre certificada se conserva en el origen; su escala por la dilución representa únicamente esa contribución, no una evaluación completa de incertidumbre del procedimiento.

## Vigencia y permisos

El vencimiento efectivo es el menor entre el fabricante y apertura más periodo documentado. Si el periodo requiere apertura y falta la fecha, el frasco no se ofrece ni se acepta como origen. Una apertura futura se rechaza. Los comerciales vencidos no pueden aprovechar la excepción de carga histórica de Stock.

`reference_material_opening_history` conserva fecha anterior/nueva, usuario y momento de cada modificación de apertura. Se usan las acciones existentes de Estándares para registro y edición, y los permisos actuales de Intermedia/Curva/AQS para sus preparaciones. Las rutas mantienen autorización del backend; no se añaden roles ni se conceden permisos.

## Migración

- Migración: `20261017063950_AddCertifiedCommercialMaterials`.
- SQL idempotente: `artifacts/sql/certified_commercial_materials.sql`.
- Depende del esquema previo, incluida `20261017031716_AddStockSolventComposition`, que ya estaba preparado en el árbol de trabajo.
- La aplicación agrega columnas, dos tablas, FKs, índices y amplía restricciones; no elimina ni recrea tablas actuales ni modifica los valores históricos.
- El rollback rechaza su ejecución si existen materiales comerciales, para no borrar su información.
- La generación del SQL no conecta ni aplica cambios a InterDB.

## Verificación reproducible

Compilación serial:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
```

Pruebas de aplicación, API, escritorio y PostgreSQL aislado:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
```

El provisionador exige `127.0.0.1:55439/lims_stock_test` y crea esquemas separados. Los casos comerciales comprueban 30 analitos, persistencia de unidades e incertidumbres, nominal/certificada, débito único, reintento idempotente, consumos directos de Curva y AQS, consumo posterior desde Intermedia, vencimiento, apertura auditada, unidades no convertibles, rollback, concurrencia entre módulos y aplicación idempotente de la migración con Stock previo.

Validación visual:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-commercial-materials.ps1
```

Usa los formularios y fichas WinUI reales con servicios de prueba aislados. Captura registro individual, Mix, composición, fichas y dilución de Mix a 1920×1080 y 1366×768. Los resultados y capturas se guardan en `artifacts/validation/commercial-materials/`. La persistencia de estas capturas es simulada; PostgreSQL se acredita por separado mediante las pruebas anteriores. No son evidencia de API desplegada ni de operación real en InterDB.

El certificado vencido de PlasmaCAL se utiliza solamente como referencia de estructura; no hay inserciones de esos ejemplos en producción.

## Resultado de la validación local

La solución y el arnés WinUI compilaron con cero errores y cero advertencias. La batería completa pasó 388 pruebas: 136 de aplicación, 48 de API, 75 de infraestructura y 129 de escritorio. La repetición final de los ocho casos comerciales en PostgreSQL y de las pruebas de aplicación/escritorio también pasó.

Se guardaron 20 capturas de los controles WinUI reales, en ambas resoluciones, con escala de Windows de 125 %. Se revisaron las filas compactas, la tabla de certificados, la ficha individual y los resultados de Intermedia: 5, 4.99 y 5.01 µg/mL con una única alícuota física de 1 mL. Los formularios conservan desplazamiento y acciones accesibles.

`artifacts/validation/commercial-materials/verification.json` registra los resultados, los informes TRX y los hashes de las capturas. La migración queda preparada para su aplicación controlada y posterior despliegue; InterDB no se modificó.


## Corrección de historial EF ausente

Los scripts incrementales de composición Stock, materiales comerciales y roles comerciales crean ahora `__EFMigrationsHistory` si falta, dentro de su transacción. No insertan IDs de migraciones anteriores ni deducen que el esquema ya esté migrado. Solo registran su propia migración al completar todas sus operaciones.

Si una ejecución anterior dejó la sesión en estado de transacción abortada, ejecutar `ROLLBACK;` antes de volver a ejecutar el archivo completo actualizado. Confirmar primero base y esquema con `SELECT current_database(), current_schema(), current_setting('search_path');` y localizar cualquier historial existente con `SELECT schemaname, tablename FROM pg_catalog.pg_tables WHERE tablename = '__EFMigrationsHistory';`. Si el historial está en otro esquema, ajustar el search_path al esquema correcto antes de ejecutar. No crear un historial paralelo para el mismo esquema de aplicación.

La ausencia del historial no demuestra cuáles migraciones se aplicaron manualmente. Los scripts siguen requiriendo el esquema previo documentado. Si aparecen columnas o tablas duplicadas, detener la aplicación y conciliar el estado real; no insertar IDs ficticios ni borrar objetos para forzar el avance. Esta corrección se prepara y valida localmente; no aplica cambios a InterDB.
