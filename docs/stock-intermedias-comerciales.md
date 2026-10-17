# Stock e Intermedias comerciales

El selector Tipo de ingreso vive dentro de los diálogos existentes. La modalidad interna conserva sus cálculos y guardado. La modalidad comercial registra un frasco o asocia uno existente, con datos mínimos del producto y componentes certificados editables. Referencia del certificado, matriz, nominal, incertidumbre, pureza, trazabilidad y apertura son opcionales. Los métodos, ubicaciones y unidades se obtienen de los catálogos activos existentes. No hay un catálogo químico independiente compatible en este modelo; cada componente conserva identidad certificada dentro de su frasco, sin generar estándares físicos adicionales. El pegado desde Excel es una acción explícita TSV de cuatro columnas: Compuesto, CAS, Concentración, Unidad. Valida toda la tabla antes de agregar filas; admite punto o coma decimal y CAS vacío. No hay importación automática de ejemplos.

## Inventario y relaciones

Se reutilizan reference_materials, reference_certified_components y reference_material_movements. La tabla aditiva reference_commercial_roles clasifica el material físico como Stock o Intermedia, guarda código STK/INT, actor y fecha, RequestId único y huella para reintentos. Su clave es (material_id, kind); contiene cero cantidades. Las dos vistas consultan el saldo de reference_materials. Registrar no crea reference_preparations, componentes de preparación ni movimientos de consumo. Asociar exige versión vigente y no altera el frasco. Un producto individual puede tener ambas clasificaciones sin duplicarse.

Los listados combinan preparaciones internas y roles comerciales en el servidor, aplicando búsqueda, filtros, orden y paginación comunes. La ficha comercial consulta el material real y muestra Registrado por, componentes, vencimiento y movimientos con una sola barra de disponibilidad. No construye una preparación ficticia para reutilizar una ficha interna.

Los selectores de Intermedia, Curva y AQS reutilizan PreparationSources.Commercial y el mismo identificador físico. PreparationSources.Debit agrupa consumo por origen físico; el número de analitos no multiplica el débito. El snapshot conserva IDs físicos y de componentes, concentraciones y unidades certificadas. La transacción, concurrencia optimista, disponibilidad y vigencia siguen siendo autoridad del backend. No se modificaron las fórmulas de dilución por este cambio.

## Permisos y rutas

GET/POST /api/reference-materials/preparations/stock/commercial usa stock.view / stock.create según corresponda. Las rutas de intermediate usan intermedia.view / intermedia.create. options requiere permiso de registro. El actor proviene de la sesión del servidor. No se requiere permiso de alta en Estándares para registrar desde el módulo de Preparaciones autorizado.

## Migraciones preparadas

No se aplicó SQL en InterDB ni producción. El despliegue debe incluir API y escritorio compatibles, y las migraciones pendientes en orden, revisando primero el historial de la base:

1. 20261017031716_AddStockSolventComposition (si aún pendiente).
2. 20261017063950_AddCertifiedCommercialMaterials (modelo comercial anterior).
3. 20261017162400_AddCommercialPreparationRoles (esta ampliación).

Script aditivo idempotente de esta ampliación: artifacts/sql/commercial_preparation_roles.sql. Solo crea la tabla de clasificación, índices y claves foráneas. No recrea tablas ni borra historia. El saldo continúa exclusivamente en reference_materials.

## Archivos principales

- Shared/Lims.Contracts/ReferencePreparations/CommercialPreparationContracts.cs y CommercialComponentPaste.cs.
- Backend/Lims.Domain/ReferenceMaterials/ReferenceCommercialRole.cs.
- Backend/Lims.Infrastructure/Persistence/CommercialPreparationService.cs, CommercialPreparationQuery.cs, CommercialMaterialModel.cs, PreparationSources.cs y los listados StockRepository/IntermediateRepository.
- Backend/Lims.Api/ReferenceMaterials/CommercialPreparationEndpoints.cs y registro en StockEndpoints/IntermediateEndpoints/Program.
- Frontend/Lims.Desktop/Http/CommercialPreparationApiClient.cs e interfaz; DI en App.
- Frontend/Lims.Desktop/Views/StockEditorDialog, IntermediateEditorDialog, CommercialPreparationEditor, CommercialCertificateEditor y CommercialPreparationDetailDialog; páginas y ViewModels para apertura por origen comercial.
- Pruebas: CommercialMaterialsPostgresTests, CommercialComponentPasteTests y StockEndpointsTests.
- Harness visual: FixtureCommercialPreparationApi y scripts/validate-commercial-preparations.ps1.

## Evidencia

Las pruebas PostgreSQL usan únicamente el servidor local 127.0.0.1:55439 y esquemas aislados de lims_stock_test. Las capturas del harness ejecutan controles WinUI reales con servicios ficticios y se identifican fixtureOnly. Estas capturas no prueban un guardado autenticado contra el backend desplegado. El despliegue y la validación de una sesión real quedan pendientes de la aplicación de migraciones y publicación de la API.


Las capturas y solicitudes ficticias están en artifacts/validation/commercial-preparations: stock/intermedia-{1366,1920}-form.png, -components.png y -detail.png. Los informes JSON verifican que el footer esté dentro del viewport, que la modalidad interna esté colapsada y que los componentes pertenezcan a un solo ingreso. Las capturas corresponden a ventanas de 1366×768 y 1920×1080 con escala de Windows al 125 %. Los campos inferiores se alcanzan mediante scroll; el footer permanece fijo y accesible.


## Resultado final de validación

- Solución Debug x64: compilación correcta, 0 advertencias y 0 errores. Harness WinUI: compilación correcta, 0 advertencias y 0 errores.
- 402 pruebas aprobadas, 0 fallos, 0 omitidas: Application 141, API 50, Infrastructure 80, Desktop 131.
- La suite de infraestructura incluye los escenarios PostgreSQL de registro directo sin datos avanzados, Mix de 30 componentes, asociación de un mismo frasco a Stock/Intermedia, listas combinadas paginadas, consumo en Intermedia/Curva/AQS, disponibilidad compartida, rollback tras escribir el rol, reintentos concurrentes sin duplicados y aplicación doble del SQL idempotente conservando inventario y movimientos.
- Se probaron los permisos stock.create e intermedia.create sin conceder permisos de alta en Estándares, y se verificó el actor autenticado.
- Informes y referencias a los TRX de esta ejecución: artifacts/validation/commercial-preparations/validation-summary.json.
