# Corrección operativa de Estándares y soluciones comerciales — 19/10/2026

Nuevo estándar vuelve a registrar exclusivamente el material físico habitual: nombre, CAS, catálogo, método, lote, marca, pureza, fechas, presentación, unidad, número de unidades, ubicación y temperatura. Se retiraron el selector comercial y el editor de certificados. La consulta histórica conserva sus datos; los productos comerciales se asocian desde Preparaciones y no se convierten en sólidos al editarlos.

El alta comercial está en Preparaciones → Stock → Nuevo Stock → Comercial / comprado, y Preparaciones → Intermedia → Nueva Intermedia → Comercial / comprada. El Stock usa el nombre del producto y una concentración documentada; la Intermedia usa una tabla de componentes con búsqueda dentro del Mix, agregar, quitar y pegado TSV desde Excel. No se exige un estándar de origen, pureza, peso ni aforo. Los datos técnicos avanzados no aparecen en estos ingresos. Los formularios internos conservan sus motores de cálculo y consumo.

El resumen reacciona a nombre, concentración, volumen, cantidad de frascos y vencimiento. Se coloca al lado del formulario cuando hay espacio y debajo en ventanas estrechas, con el footer fuera del contenido desplazable. Las fichas comerciales priorizan código, producto, insignia Comercial, lote, fabricante, vigencia, concentraciones, saldo y movimientos.

## Persistencia e inventario

Se conservan `reference_materials`, `reference_certified_components`, `reference_material_movements` y `reference_commercial_roles`. El rol no contiene cantidades. Registrar crea un solo material comercial con sus componentes reales, sin insertar una preparación artificial ni sólidos ficticios. «Usar producto ya registrado» asocia el mismo ID y versión sin cambiar cantidades, componentes, metadatos certificados ni movimientos.

La cantidad de frascos representa unidades del mismo producto/lote en un inventario común: volumen por frasco × número de frascos. No crea un inventario por analito ni identifica por separado aperturas de cada unidad. Productos de distinto lote deben registrarse por separado. El motor existente conserva el débito único por origen físico y operación, la idempotencia, las transacciones y la protección frente a concurrencia/saldos negativos.

La concentración certificada permanece intacta. Si se documenta una nominal diferente, el analista puede elegir usarla según el método. `UseNominalConcentration` se conserva en el JSON existente y `ComponentsForUse()` se utiliza en listas y fuentes de Intermedia/Curva/AQS. La incertidumbre certificada no se atribuye a la concentración nominal. La fórmula de dilución no cambia.

Solvente/matriz se selecciona del catálogo activo existente; se conserva su ID en el JSON y el servidor resuelve el nombre, sin inferir porcentajes ni base masa/volumen.

## Adaptación de esquema preparada

Nueva migración: `20261019232052_AllowCommercialBottleCounts`. Solo retira la condición `package_count = 1` de la restricción comercial; conserva los controles de presentación, certificado, fechas y el número positivo de unidades. No actualiza filas, saldos ni relaciones.

SQL transaccional e idempotente: `artifacts/sql/commercial_bottle_counts.sql`. Requiere las ampliaciones anteriores `AddStockSolventComposition`, `AddCertifiedCommercialMaterials` y `AddCommercialPreparationRoles` según el historial real de la base. Aplicar esta adaptación después de ellas. No se ejecutaron migraciones ni SQL en InterDB/producción. El script de conciliación anterior `certified_commercial_materials.sql` reconoce también la definición exacta de esta restricción posterior sin sustituirla; sigue rechazando otras definiciones incompatibles.

## Verificación

La evidencia visual se genera con los controles WinUI de producción y servicios de prueba aislados en `artifacts/validation/commercial-fix-20261019/`. Los productos están rotulados PRUEBA VISUAL. Las capturas no son registros reales de producción ni demuestran el despliegue de la API. Las pruebas de persistencia usan exclusivamente PostgreSQL local en `127.0.0.1:55439`, base `lims_stock_test`, con esquemas independientes.

Comandos reproducibles:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
powershell -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
powershell -ExecutionPolicy Bypass -File scripts/validate-commercial-preparations.ps1 -SkipBuild
```

La validación de una sesión autenticada contra el backend desplegado queda fuera de esta evidencia local; requiere publicar API/escritorio compatibles y aplicar las migraciones pendientes de manera autorizada.

## Resultado final

- Solución Debug x64 y harness WinUI: 0 errores, 0 advertencias.
- 411 casos únicos aprobados: Application 142, API 50, Desktop 131 e Infrastructure 88; sin omisiones. Infrastructure se repitió después de corregir la compatibilidad de la restricción y pasó completa. Los cuatro TRX finales están referenciados en `artifacts/validation/commercial-fix-20261019/verification.json`.
- PostgreSQL verifica registro comercial directo, asociación de históricos sin cambio de saldo, Mix de 30 componentes, dilución individual, débito único, concurrencia, reintentos, rollback, selección nominal/certificada y aplicación doble de la adaptación conservando la ficha persistida completa.
- Veinte capturas reales de controles WinUI en ventanas de 1366×768 y 1920×1080. Los cuatro informes de geometría confirman el footer visible y los campos internos ocultos en modalidad comercial. Se revisaron visualmente los formularios y las fichas. La conservación de datos en PostgreSQL se prueba por separado; el guardado de las capturas usa servicios de prueba en memoria.
- La validación no aplicó SQL ni migraciones en producción y no insertó ejemplos en su inventario.

| Pantalla | 1366×768 | 1920×1080 |
|---|---|---|
| Nuevo estándar restaurado | [Formulario](../artifacts/validation/commercial-fix-20261019/standard-1366-form.png) | [Formulario](../artifacts/validation/commercial-fix-20261019/standard-1920-form.png) |
| Stock comercial | [Formulario](../artifacts/validation/commercial-fix-20261019/stock-1366-form.png) · [Inventario](../artifacts/validation/commercial-fix-20261019/stock-1366-inventory.png) | [Formulario](../artifacts/validation/commercial-fix-20261019/stock-1920-form.png) · [Inventario](../artifacts/validation/commercial-fix-20261019/stock-1920-inventory.png) |
| Intermedia comercial | [Componentes](../artifacts/validation/commercial-fix-20261019/intermedia-1366-components.png) · [Inventario](../artifacts/validation/commercial-fix-20261019/intermedia-1366-inventory.png) | [Componentes](../artifacts/validation/commercial-fix-20261019/intermedia-1920-components.png) · [Inventario](../artifacts/validation/commercial-fix-20261019/intermedia-1920-inventory.png) |
| Ficha Stock comercial | [Ficha](../artifacts/validation/commercial-fix-20261019/stock-1366-detail.png) | [Ficha](../artifacts/validation/commercial-fix-20261019/stock-1920-detail.png) |
| Ficha Intermedia comercial | [Ficha](../artifacts/validation/commercial-fix-20261019/intermedia-1366-detail.png) | [Ficha](../artifacts/validation/commercial-fix-20261019/intermedia-1920-detail.png) |

Todos los productos comerciales de las capturas son PRUEBA VISUAL. No son registros reales de producción. La carpeta de evidencia conserva las solicitudes verificadas; `verification.json` reúne los resultados agregados y los SHA256 de las capturas, del harness y del SQL de adaptación.

## Diagnóstico de requisitos no visibles

Si `commercial_bottle_counts.sql` devuelve SQLSTATE 55000 antes de aplicar la adaptación, una o varias de estas relaciones no se resuelven en la conexión actual: `reference_materials`, `reference_certified_components`, `reference_commercial_roles`. Puede faltar la ampliación previa, o las tablas pueden estar en otra base/esquema fuera del `search_path`. Ejecutar primero `ROLLBACK;` en esa sesión abortada y después `preflight_commercial_materials.sql`, que consulta únicamente metadatos e historial. Conciliar los requisitos según esos resultados; este error no autoriza crear tablas vacías ni registrar migraciones como aplicadas. El diagnóstico del script ahora informa los nombres faltantes y la base, esquema y ruta de búsqueda.

## Adaptación de la restricción histórica de cantidad

Definición confirmada en InterDB por el usuario: `ck_reference_materials_presentation = CHECK (presentation_quantity > 0::numeric)`, validada. El nombre colisiona con el control del tipo comercial que introduce la ampliación.

El archivo actualizado `artifacts/sql/certified_commercial_materials.sql` reconoce exclusivamente esa expresión histórica mediante la normalización de PostgreSQL. Conserva `presentation_quantity > 0` con el nombre `ck_reference_materials_presentation_quantity`: renombra el control existente si falta el nombre correcto, o conserva el control correcto ya existente tras verificar su definición. Después agrega el control comercial. Una definición distinta del control de cantidad provoca rollback. La adaptación no modifica materiales, componentes, preparaciones, movimientos ni saldos y no omite una migración registrada que tenga estructura inconsistente.

Validación localizada: build de Infrastructure.Tests con 0 errores y 0 advertencias; 7 pruebas PostgreSQL aprobadas, 0 fallos, 0 omisiones. Se cubren ambos casos de nombres, tabla certificada inicialmente ausente, Stock y movimientos existentes, aplicación doble, conflicto con otra definición y rechazo de restricciones desconocidas. Resultado: `artifacts/validation/quantity-constraint-fix/quantity-constraint.trx`.

En la sesión que falló: ejecutar `ROLLBACK;`, volver a abrir el archivo actualizado (el contenido de una pestaña SQL abierta puede ser anterior), ejecutarlo completo y, cuando finalice correctamente, ejecutar `commercial_bottle_counts.sql`. Esta validación fue local; no se aplicó esta reparación a InterDB desde el agente.
