# Stock: compuesto y composición de solventes

Cambio limitado a crear y consultar Stock. Los cálculos de masa, pureza, concentración real y consumo del estándar conservan su implementación. El módulo no ofrece edición de Stock; esta entrega no introduce esa operación.

## Comportamiento

- `Compuesto` es obligatorio, independiente del nombre de Stock. Se sugiere el nombre del estándar y se conserva una corrección manual al cambiar de origen.
- Observaciones desaparece del formulario nuevo. La columna, el contrato y la visualización de observaciones históricas se conservan.
- El editor consulta `GET /api/reference-materials/preparations/stock/solvents`, con autorización `stock.create`. El catálogo se busca por nombre o abreviatura. No existe un catálogo de respaldo en la aplicación de producción.
- Cada fila conserva su ID de solvente y porcentaje; el orden es el de inserción. Agregar o quitar filas no redistribuye porcentajes.
- Se admiten hasta cuatro decimales, valores mayores que cero y hasta 100, sin duplicados ni filas incompletas. La suma decimal debe ser exactamente 100. No se redondean valores para hacer válida una mezcla.
- La barra representa composición. La parte sin asignar se muestra en gris y los errores bloquean Crear Stock. Utiliza los pinceles existentes del Design System.
- Base registrada: `CompositionPercent`. La interfaz dice «Porcentajes de composición (%) · Base masa/volumen no especificada». No se infiere % v/v, % m/m, concentración de ácidos ni pureza de reactivos; no se calculan volúmenes físicos ni movimientos de inventario de solventes.
- El resultado y el pie permanecen en el diálogo. El formulario y la lista de filas tienen desplazamiento interno.

## Persistencia y despliegue

Migración: `Backend/Lims.Infrastructure/Persistence/Migrations/20261017031716_AddStockSolventComposition.cs`, con diseñador y snapshot EF actualizados.

Script equivalente idempotente: `artifacts/sql/stock_solvent_composition.sql`. Está limitado a esta migración y usa `__EFMigrationsHistory`. Requiere el esquema previo del proyecto, hasta `20261016092426_AddCurveAndAqsPreparations`; no aplica automáticamente migraciones anteriores. Reejecutarlo no duplica catálogo ni filas. La migración no se ejecuta al iniciar la aplicación.

| Objeto | Cambio |
|---|---|
| `reference_preparations` | `compound` y `composition_basis`, ambos anulables para conservar registros históricos |
| `reference_solvents` | ID, nombre, abreviatura, activo e índice de presentación; abreviatura única |
| `stock_solvents` | UUID, preparación, solvente, `percent numeric(7,4)`, posición; claves foráneas con `RESTRICT`; solvente y posición únicos por preparación |

Los 12 registros iniciales son THF, Hxn, ACN, MeOH, Tolueno, MTBE, Cyclohexano, H2O, HNO3, HCl, Acetona y DMSO, con los nombres completos solicitados. No se cargan preparaciones de ejemplo en producción.

El servidor valida compuesto y composición antes de crear. Dentro de la transacción existente, comprueba y bloquea los registros del catálogo hasta el commit. Preparación, solventes, movimiento y decremento del estándar se guardan juntos; un fallo revierte todo. RequestId y fingerprint incluyen los campos nuevos y mantienen la idempotencia de la creación.

La lectura devuelve solventes unidos al catálogo y ordenados por posición. La ficha muestra compuesto, tabla, barra y resumen generados a partir de esas relaciones. Para registros antiguos sin composición muestra «Sin composición especificada», sin inferir valores desde observaciones ni desde el estándar.

Antes de usar esta funcionalidad en un entorno desplegado se necesita aplicar la migración revisada y desplegar el backend actualizado. La aplicación bloquea la creación si no puede consultar el catálogo. Esta entrega no aplica SQL a InterDB.

## Verificación reproducible

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-stock-solvents.ps1
```

La primera verificación compila el ejecutable WinUI y las pruebas. La segunda ejecuta pruebas contra PostgreSQL aislado en `127.0.0.1:55439`, base `lims_stock_test`, con esquemas independientes. La tercera utiliza los fuentes reales del formulario y la ficha WinUI con un adaptador de datos de prueba separado del producto. Las capturas no acreditan autenticación, persistencia en InterDB ni despliegue; la persistencia se acredita por las pruebas PostgreSQL.

Las pruebas de Stock cubren 100 %, 50:50, 30:20:50, decimales, orden, guardado y reapertura, idempotencia, catálogo, búsquedas, inactivos, IDs inexistentes, duplicados, proporciones inválidas, registros históricos, conservación de notas y rollback. Las pruebas del ViewModel comprueban agregar/quitar filas, bloqueo del guardado, sugerencia editable y conservación del cálculo.

Las capturas y el resultado definitivo se registran en `artifacts/validation/stock-solvents/verification.json`.

## Resultado de la entrega

Compilación de la solución y del ejecutable de validación WinUI: 0 errores, 0 advertencias. Pruebas: **376 aprobadas, 0 fallidas, 0 omitidas** (134 aplicación, 46 API, 67 infraestructura y 129 escritorio). Entre ellas hay 25 casos de Stock en PostgreSQL. El servidor aislado se detuvo al finalizar.

Capturas reales de los fuentes WinUI con datos de prueba, tema oscuro y escala 125 %. Se verificaron ambas mezclas a 1366×768 y 1920×1080; el pie permanece visible. También se inspeccionó la ficha 30:20:50 tras guardar en el adaptador visual. No acredita guardado de esa captura en PostgreSQL; la persistencia PostgreSQL se verificó separadamente en las pruebas de integración.

- [detail-30-20-50-1920x1080.png](../artifacts/validation/stock-solvents/detail-30-20-50-1920x1080.png)
- [mixture-30-20-50-1366x768.png](../artifacts/validation/stock-solvents/mixture-30-20-50-1366x768.png)
- [mixture-30-20-50-1920x1080.png](../artifacts/validation/stock-solvents/mixture-30-20-50-1920x1080.png)
- [mixture-50-50-1366x768.png](../artifacts/validation/stock-solvents/mixture-50-50-1366x768.png)
- [mixture-50-50-1920x1080.png](../artifacts/validation/stock-solvents/mixture-50-50-1920x1080.png)

## Archivos agregados o modificados

- `.gitignore`
- `Backend/Lims.Api/ReferenceMaterials/StockEndpoints.cs`
- `Backend/Lims.Application/ReferencePreparations/IStockRepository.cs`
- `Backend/Lims.Application/ReferencePreparations/IStockService.cs`
- `Backend/Lims.Application/ReferencePreparations/StockService.cs`
- `Backend/Lims.Domain/ReferencePreparations/ReferencePreparation.cs`
- `Backend/Lims.Domain/ReferencePreparations/ReferenceSolvent.cs`
- `Backend/Lims.Infrastructure/Persistence/LimsDbContext.cs`
- `Backend/Lims.Infrastructure/Persistence/Migrations/20261017031716_AddStockSolventComposition.Designer.cs`
- `Backend/Lims.Infrastructure/Persistence/Migrations/20261017031716_AddStockSolventComposition.cs`
- `Backend/Lims.Infrastructure/Persistence/Migrations/LimsDbContextModelSnapshot.cs`
- `Backend/Lims.Infrastructure/Persistence/StockRepository.cs`
- `Backend/Lims.Infrastructure/Persistence/StockSolventModel.cs`
- `Frontend/Lims.Desktop/Http/IStockApiClient.cs`
- `Frontend/Lims.Desktop/Http/StockApiClient.cs`
- `Frontend/Lims.Desktop/ViewModels/StockSolventRow.cs`
- `Frontend/Lims.Desktop/ViewModels/StockViewModel.cs`
- `Frontend/Lims.Desktop/Views/StockCompositionVisual.cs`
- `Frontend/Lims.Desktop/Views/StockDetailDialog.xaml`
- `Frontend/Lims.Desktop/Views/StockDetailDialog.xaml.cs`
- `Frontend/Lims.Desktop/Views/StockEditorDialog.xaml`
- `Frontend/Lims.Desktop/Views/StockEditorDialog.xaml.cs`
- `Shared/Lims.Contracts/ReferencePreparations/StockContracts.cs`
- `Shared/Lims.Contracts/ReferencePreparations/StockSolventComposition.cs`
- `Tests/Backend/Lims.Api.Tests/StockEndpointsTests.cs`
- `Tests/Backend/Lims.Infrastructure.Tests/Persistence/StockPostgresTests.cs`
- `Tests/Frontend/Lims.Desktop.Tests/StockViewModelTests.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/App.xaml.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/FixtureStockApi.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj`
- `artifacts/sql/stock_solvent_composition.sql`
- `artifacts/validation/stock-solvents/verification.json`
- `docs/stock-compuesto-solventes.md`
- `scripts/validate-stock-solvents.ps1`

Se agregan además las cinco capturas PNG enlazadas arriba. Los registros de compilación y TRX permanecen en `artifacts/` como evidencia local.
