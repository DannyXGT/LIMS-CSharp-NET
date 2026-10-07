# Preparaciones → Stock

Una sola ventana permite buscar y seleccionar el estándar, introducir parámetros,
registrar el peso tomado y crear Stock. La vista previa usa el mismo motor Decimal
que el backend; el servidor recalcula sobre el estándar vigente dentro de la transacción.

## Modelo y fórmulas

`ReferencePreparation` conserva origen y datos históricos, pureza utilizada,
concentración objetivo/real y sus unidades, peso a tomar/peso tomado y sus unidades,
volumen/unidad, preparación/expiración, temperatura libre, estado, preparador,
observaciones, timestamps, versión y clave de idempotencia.
No agrega un catálogo de solventes ni una regla de expiración: el usuario elige la fecha.
La diferencia porcentual se deriva y no se persiste.

Para mg/L, mL y g:

- Peso a tomar = objetivo × volumen / [1 000 000 × (pureza / 100)].
- Concentración real = peso tomado × (pureza / 100) × 1 000 000 / volumen.
- Diferencia = (real − objetivo) / objetivo × 100.

`StockUnits` convierte g/mg/µg, L/mL y g/L, mg/L, µg/L, µg/mL explícitamente.
No presume equivalencia de ppm ni concentración de estándares líquidos.
El peso vacío permite calcular el teórico; no genera concentración real ni consumo.
Es obligatorio al guardar. El consumo usa el peso tomado convertido a la unidad del saldo.
La disponibilidad se valida contra el peso real, aunque difiera del teórico.
No se redondea durante el cálculo. Peso calculado y concentración real se guardan
como numeric(40,28); los pesos medidos y el ledger conservan la precisión de seis
decimales del saldo existente. Una conversión que el saldo no puede representar
se rechaza expresamente, sin redondear silenciosamente la medición.
`StockPresentation` limita los decimales visibles según magnitud y elimina ceros finales.

## Transacción y trazabilidad

Stock, movimiento StockConsumption, saldo y auditoría se guardan juntos dentro de
CreateExecutionStrategy. Cada reintento recarga los datos. Guid Version y los CHECK
impiden sobreconsumo y saldos negativos. El actor procede de la sesión autenticada.
La pureza y el origen históricos no cambian al editar el maestro.
La clave de solicitud evita duplicar el débito tras perder una respuesta.

El código es STK-AAAAMMDD-NNN: una secuencia global creciente y un índice único
protegen la identidad. El sufijo se amplía al superar 999; no se reinicia cada día.
Puede tener saltos después de rollback/reintento, como cualquier secuencia PostgreSQL.

## Migración pendiente

Se corrigió la migración local, sin publicar, 20261007045455_CreateReferenceStock,
documentada previamente como no ejecutada en InterDB. Se regeneraron operaciones,
Designer, snapshot y artifacts/sql/reference_stock.sql. No se conecta ni ejecuta
migración alguna en InterDB. El archivo SQL queda únicamente preparado para revisión.

## Verificación

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
git diff --check
```

El script de pruebas utiliza únicamente PostgreSQL aislado en
127.0.0.1:55439/lims_stock_test, con un esquema separado por prueba.
Cubre fórmulas conocidas, unidades, teórico/real, obligatoriedad, disponibilidad,
rollback, concurrencia, retry, idempotencia, snapshot, usuario y auditoría.
Las capturas WinUI utilizan las vistas reales con datos ficticios: son evidencia
visual y de interacción, no un guardado de producción ni una validación de InterDB.
