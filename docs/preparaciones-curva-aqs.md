# Preparaciones: Curva y AQS

Implementación nativa dentro de **Preparaciones → Stock | Intermedia | Curva | AQS**. Las dos pestañas añaden búsqueda, filtro por vigencia y método, paginación, selección, apertura por doble clic/Enter, registro de grupos y ficha con soluciones, orígenes, cálculos y consumos. Cada pestaña conserva su propia instancia de página/ViewModel y sus filtros y selección.

## Reglas recuperadas de TrazaLab

Se consultaron, sin ejecutar Python ni conectar con MySQL:

- `Trazalab/view/FormIngresoIntermediaWorks.py`: `_iter_right_groups`, `_rebuild_tube`, `guardar_ingreso_works`. Curva registra un código común, compuesto, temperatura, fechas y familias de Intermedia con volumen tomado y aforo propios. Se pueden añadir familias; no hay regresión instrumental ni un número fijo de puntos. El valor inicial de expiración es preparación + 188 días y puede editarse.
- `Trazalab/view/form_AQS.py`: `_on_fecha_prep_changed`, `_on_estandar_interno_changed`, `_rebuild_tube`, `guardar_ingreso_aqs`. AQS registra familias de Intermedia bajo un código, con volumen tomado y aforo independientes, y selecciona explícitamente otro grupo como estándar interno. Expiración inicial: preparación + 2 días, editable.
- `Trazalab/logic/logica.py`: `guardar_lote_datos_aqs`, y consultas de consumos de `tabla_aqs` y `tabla_intermedia_works`. Se conserva el guardado agrupado, se sustituye la contabilidad separada por el libro común de .NET y se evita el guardado parcial de Works.

La fórmula preservada es **C₂ = C₁ × V₁ / V₂**, aplicada independientemente a cada analito de cada solución. Cada fila representa una dilución con aforo propio; no representa una mezcla conjunta entre filas. No se suman concentraciones de soluciones distintas. No se añaden regresión, ecuación instrumental ni R². Las unidades de concentración se validan con `StockUnits` y se conservan; los volúmenes se convierten entre mL/L antes del cálculo. Se usa `decimal` sin redondeo intermedio; las concentraciones se persisten como números en JSONB sin escala decimal artificial.

Una familia multianalito de TrazaLab corresponde a una Intermedia completa de .NET: se consume una alícuota física, no una por analito. Una Intermedia puede aparecer en varias soluciones del mismo grupo; se conservan todos sus aforos y resultados y se acumula su débito para validar el saldo real. El estándar interno se persiste como dato explícito de la solución, independiente de la posición o cantidad de filas.

## Persistencia y concurrencia

Se reutilizan `ReferencePreparation`, `ReferencePreparationComponent` y `ReferenceMaterialMovement`, en `reference_preparations`, `reference_preparation_components` y `reference_material_movements`. No se crean tablas paralelas de inventario.

- Cabecera: tipo Curva/AQS, código único, nombre/compuesto opcional, método, fechas, responsable obtenido de la sesión del servidor, observaciones, temperatura opcional y huella de solicitud.
- `ResultsJson`: cálculo histórico completo, orden de soluciones, fuente real y su versión, lotes/CAS/materiales individuales, concentraciones iniciales y finales, aforo y unidad de cada solución y rol explícito de estándar interno.
- Componentes: una relación FK por origen físico, con su consumo acumulado y snapshot. Movimientos: un débito por ese componente, con saldos anterior/posterior, actor y fecha.
- El mismo `ReferencePreparation.AvailableVolume` y token optimista `Version` se actualizan tanto desde Intermedia derivada como desde Curva/AQS. Creación, componentes, movimientos y actualización de todos los orígenes se confirman en una sola transacción. Un conflicto concurrente o error revierte todo el grupo. No se recalcula el saldo sumando de nuevo los movimientos: así se evita el doble descuento.
- `RequestId` + SHA-256 del payload/tipo/actor permiten reintentar una respuesta perdida sin duplicar consumos. La interfaz bloquea guardados simultáneos y conserva exactamente la solicitud pendiente ante incertidumbre de red.
- Curva/AQS no tienen inventario posterior en el procedimiento verificado. No se muestran barras ni saldos propios inventados. El `final_volume` de cabecera es el total producido en mL para almacenamiento; los aforos operativos están en cada solución. El saldo de cabecera es cero y esos tipos no se ofrecen como fuentes de otras preparaciones.

No se cambian estados históricos en segundo plano. La ficha muestra la vigencia calculada por fecha y los movimientos históricos; el selector vuelve a consultar el saldo actual. Los formularios inicializan las fechas como TrazaLab y permiten corregirlas. El servidor rechaza orígenes inexistentes, no elegibles, futuros, vencidos, incompatibles o sin volumen, y vuelve a validar versiones y fechas al guardar.

## API y permisos

Rutas base:

- `/api/reference-materials/preparations/curve`
- `/api/reference-materials/preparations/aqs`

Cada base ofrece `GET` listado (`search`, `status`, `method`, `page`, `pageSize`), `GET /{id}`, `GET /options`, `POST /preview` y `POST` registro. El servidor fija el tipo según la ruta; no lo acepta del payload.

Permisos nuevos: `curva.view`, `curva.create`, `aqs.view`, `aqs.create`. Se integran en el catálogo existente y su interfaz de administración sin rediseñarla. Las políticas HTTP comprueban el permiso específico o el rol Administrador. No se amplía la compatibilidad de permisos heredados: consultar/registrar estándares no concede Curva/AQS a usuarios ordinarios. La migración solo añade códigos al catálogo, nunca asignaciones individuales.

## Migración para revisión

`20261016092426_AddCurveAndAqsPreparations` amplía el CHECK de tipos y añade cuatro códigos al catálogo. No crea ni borra tablas ni transforma registros existentes. `artifacts/sql/reference_curve_aqs.sql` es el SQL idempotente generado por EF para esa migración, a partir de `PreservePreparationDecimalScale`. Revisar/aplicar únicamente durante el despliegue autorizado, después de las migraciones previas. El rollback de esquema no debe intentarse si existen registros o asignaciones que usan los tipos/permisos nuevos.

**Esta implementación no ejecuta migraciones en el arranque ni aplica cambios en InterDB.** Es necesario desplegar la API y aplicar la migración revisada antes del uso operativo contra producción.

## Evidencia

Las pruebas PostgreSQL usan únicamente `127.0.0.1:55439/lims_stock_test`, con esquema exclusivo por prueba y guardia de conexión; los datos de prueba nunca van a InterDB. Las capturas del harness ejecutan las mismas vistas WinUI de producción con servicios fixture y se identifican como `fixtureOnly`. Pruebas de persistencia aislada, validación HTTP y capturas visuales son evidencias distintas; ninguna demuestra despliegue o guardado en InterDB.

Validación realizada el 16 de octubre de 2026:

| Proyecto | Correctas | Fallidas | Omitidas |
| --- | ---: | ---: | ---: |
| Application | 134 | 0 | 0 |
| API | 46 | 0 | 0 |
| Infrastructure, incluyendo PostgreSQL aislado | 63 | 0 | 0 |
| Desktop ViewModels/HTTP/presentación | 114 | 0 | 0 |
| **Total** | **357** | **0** | **0** |

Incluye creación y lectura de grupos de ambas clases, varios orígenes, aforo independiente, alícuota multianalito, origen repetido entre soluciones, saldo 100 → 90 → 75 mL, consumo posterior por Intermedia derivada, concurrencia Curva/AQS y Curva/Intermedia, rollback de varios orígenes tras fallos inyectados después de las escrituras, idempotencia, códigos duplicados, búsqueda/paginación, unidades, permisos individuales y ausencia de concesiones automáticas. El SQL de migración se aplicó dos veces en un esquema aislado y conservó Stock y los permisos existentes.

La solución completa, la aplicación Desktop y el harness compilaron sin advertencias ni errores. Comandos de reproducción:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/validate-curve-aqs.ps1 -Action Start
# En la misma sesión de escritorio: Run, Responsive y Finish.
```

Las vistas WinUI se ejecutaron a **1920×1080, 1600×900 y 1366×768**, con **DPI real 120 / escala 125 %**. Los informes comprueban que Guardar permanece dentro de la ventana; los componentes y resultados usan desplazamiento interno. No se cambió la escala de Windows. La validación a 100 % no se ejecutó como sesión DPI independiente.

`scripts/validate-curve-aqs-popups.ps1` también abrió mediante UI Automation los calendarios y selectores de Intermedia de ambos editores a 1366×768. El calendario quedó dentro de la ventana y por encima de Guardar; el selector buscable abrió y cerró correctamente. [Informe de popups](../artifacts/validation/curve-aqs/popup-verification.json).

Capturas nativas, tomadas por HWND para excluir ventanas superpuestas:

- [Curva: formulario compacto a 1366×768](../artifacts/validation/curve-aqs/Curva-form-1366x768.png).
- [AQS: formulario compacto a 1366×768](../artifacts/validation/curve-aqs/AQS-form-1366x768.png).
- [Curva: componentes](../artifacts/validation/curve-aqs/Curva-components-1366x768.png) y [ficha](../artifacts/validation/curve-aqs/Curva-detail.png).
- [AQS: componentes](../artifacts/validation/curve-aqs/AQS-components-1366x768.png) y [ficha](../artifacts/validation/curve-aqs/AQS-detail.png).
- [Intermedia con 75 mL después de AQS y Curva](../artifacts/validation/curve-aqs/Intermediate-shared-75mL.png).
- [Informe visual](../artifacts/validation/curve-aqs/verification.json) y [saldos y movimientos fixture](../artifacts/validation/curve-aqs/shared-balance.json).

No quedan fórmulas pendientes por inaccesibilidad del código original. Quedan pendientes el despliegue autorizado de API/migración, la validación con una sesión real y registros reales de InterDB, y cualquier prueba a escala distinta del 125 %.
