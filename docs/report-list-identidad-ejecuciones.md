# Identidad de pruebas y Report List integrado — 19 de noviembre de 2026

El cliente existente es **WinUI 3 / .NET 10**, con ASP.NET Core y PostgreSQL. Se conservó ese cliente, su AppShell, login, sesión, tabla de Consulta, expediente y permisos. No se creó una aplicación WPF, un backend Python ni una base operativa nueva.

## Recuperación de la tabla operativa — 20 de noviembre de 2026

La revisión posterior sustituyó la lista de texto inicial por `ReportListGrid`: filas nativas de 34 unidades, encabezado fijo, selección con Ctrl/Shift, navegación con teclado, CHK y Mantener por ejecución, columnas redimensionables y ordenables. El título y la barra son compactos; Solo tabla oculta el encabezado descriptivo. Los filtros adicionales se abren en un desplegable, con filtros de columna para JOB, prueba, laboratorio, cliente, verificador, bloque y fecha. El nombre técnico y UUID están en el tooltip, copia y expediente.

`ReportListRows` carga lotes de 500 al desplazar. Esa cantidad limita cada petición, no el total navegable. La API ordena y filtra antes de paginar; cada continuación exige la revisión obtenida al comenzar. Un marcado o importación concurrente invalida la continuación y pide refrescar, evitando mezclar cortes distintos. Orden SQL limitado a columnas permitidas y UUID como desempate.

Se conservaron búsquedas por JOB/prueba/cliente y múltiples JOBs, vistas Pendientes/Chequeados/Activas/Archivadas/Todas, marcado individual desde CHK, marcado de hasta 500 seleccionadas por transacción, actor autenticado, fecha/hora, bloques, permanencia, corrección, expediente e historial. La cabecera informa el resultado real de la consulta. La captura AR se habilita y puede pausarse únicamente cuando la base anuncia tracking verificado; no se presenta como conexión física comprobada a un lector.

El reloj operativo ajustable del original está recuperado en Filtros → Fecha y hora operativas. Por defecto se usa el servidor; el operador puede fijar fecha/hora de Guatemala para manual y AR. La fecha de prueba se deriva de esa hora operativa. La fecha de auditoría y la ventana de reutilización del bloque usan el guardado real del servidor, de modo que fijar una fecha antigua no reescribe el tiempo de auditoría ni crea un bloque por cada fila. Esta separación es una adaptación del modelo original.

El expediente conserva su diseño. Se corrigió el orden de las dos columnas de nombres: Prueba muestra el nombre aprobado y Nombre técnico original conserva la fuente. Historial muestra el UUID de la ejecución y el cambio de verificador/fecha o permanencia, permitiendo distinguir dos PH.

### Auditoría de la conexión y despliegue

La implementación original se localizó en `LIMS(React)/Frontend/src/pages/report-list/ReportListPage.tsx`, `reportList.api.ts`, `reportList.scanner.ts` y `Backend/app/modules/report_list/router.py`, con reglas de modo en `jobs_cierres/service.py`. Se comprobaron sus checks, permanencia, captura AR, reloj operativo, bloques y consultas sobre `planificacion_report_check`, programación activa/archivada y tablas de tracking. La tabla antigua une por `job_prueba`; se adaptó al UUID de ejecución y no se reutiliza esa clave para colapsar pruebas.

La API .NET implementa GET `/api/planning/report-list`, POST `/report-list/check`, POST `/report-list/correction`, PUT `/report-list/keep` y POST `/report-list/scan`, todos bajo `/api/planning`, autenticación y permisos existentes.

El **20-Nov** el destino configurado `http://10.226.248.191:8080` respondió: health 200, Consulta 401 sin sesión, Report List **404**. El error corresponde a una ruta ausente en el despliegue, no a un listado vacío, un filtro o un permiso de marcado. El `--no-build` del comando original también reutilizaba otra salida Debug antigua. Ahora están compiladas Release y la salida `bin/Debug/.../win-x64` de ese comando; ejecutar un binario nuevo no actualiza la API remota.

El usuario indicó que utiliza GitHub y un script alojado en el servidor. Ese script no está en el checkout ni se conoce su servicio/directorio efectivo. El workflow existente `.github/workflows/publish-api.yml` se ejecuta manualmente o por etiquetas `v*`; un push ordinario no lo dispara. Publica la misma API y crea un release al usar etiqueta. No se ejecutó el script remoto ni se actualizó el servidor/base productiva. La publicación Release y SQL de preflight/migración están preparados para revisión; aplicar SQL productivo sigue requiriendo autorización del destino.

### Evidencia de esta recuperación

- **111 pruebas aprobadas, cero fallidas y cero omitidas**: 27 reglas, 34 PostgreSQL aislado, 25 autorización y 25 cliente; `artifacts/report-list/recovery/tests/*.trx`. Incluyen dos intentos simultáneos con la misma versión (un guardado/un conflicto/un evento), 2,503 ejecuciones, orden/filtros del servidor, revisión durante carga incremental y reloj operativo con auditoría real.
- API autenticada de la copia restaurada: **10,987 UUID distintos, 22 peticiones**, sin pérdidas o duplicados. Primera petición 164 ms, media 218 ms y máxima 379 ms en esta estación; no son medidas LAN/servidor productivo. `artifacts/report-list/recovery/real-volume.json`.
- WinUI Release real: **2,000 filas cargadas**, aproximadamente 90 contenedores realizados y 547 MiB de working set en la medición; `artifacts/report-list/native-thousands.json`. No se afirma comportamiento de hardware de 8 GB/HDD sin probarlo.
- Login original → Report List → dos PH → CHK individual → guardado → Pendientes/Chequeados → Consulta/expediente/Historial → reinicio de API y Desktop → mismo UUID marcado y la otra PH pendiente. `native-Mark.json`, `native-Correct.json`, `native-dossier.json` y `real-mark-persistence.json` documentan acciones sobre la copia local.
- Capturas auténticas en `artifacts/report-list/ui/`: `recovery-Overview.png`, `recovery-Pending.png`, `recovery-Checked.png`, `02-report-list-dos-ph-reales.png`, `recovery-Dossier-tests.png`, `recovery-Dossier-history.png` y `01-consulta-nombres-planificacion-dos-ph.png`.

La aceptación **del entorno configurado por Production sigue pendiente** hasta publicar la API, comprobar sus migraciones y repetir la lectura autenticada desde ese cliente. No se sustituyó su URL por localhost ni se cargaron datos de ejemplo para ocultar el 404. Tampoco se migraron por aproximación los 13 checks históricos ambiguos ni se afirmó aceptación de un lector físico sin tracking real.

## Resultado y evidencia real

La conexión efectivamente consultada fue **127.0.0.1:55441 / lims_planning_dev**, restauración local de desarrollo. Se aplicaron allí la migración previa de identidad y la migración aditiva de Report List. No se ejecutó SQL sobre InterDB, Supabase ni una base compartida/productiva.

El JOB real **GUAT26022934** conserva **17 ejecuciones**. Dos nombres técnicos distintos producen dos filas PH independientes:

| Nombre técnico de Report List | Nombre aprobado | UUID persistente |
|---|---|---|
| Determination of pH for Leather | PH | `25fdff34-301c-4593-933d-4461a385072a` |
| pH of Aqueous Extract | PH | `6f159138-56b1-417e-a0a6-0958e570e37b` |

Se marcó la primera desde la interfaz WinUI, con motivo obligatorio. La segunda siguió pendiente. Consulta y el expediente devolvieron el marcado y su historial, y la lectura autenticada posterior al reinicio de la API conservó ese resultado. Es una acción de validación en la copia local, no una afirmación de digitación productiva.

La reimportación del Summary y Report List reales del 17-Nov-2026 devolvió el lote ya aplicado y `alreadyApplied=true`. Se conservaron **15,685 JOBs y 10,987 ejecuciones**. El JOB real `GUAT25009340-REV7-REV8` permaneció como WIP de Summary, con ingreso `2026-07-23`, vencimiento `2026-07-28` y ejecución nula.

Una cuenta sintética local con `planning.view` pudo consultar, recibió **HTTP 403** al intentar marcar una PH real y no cambió la versión ni su estado. La cuenta se desactivó al terminar la prueba.

Capturas del cliente real:

- [Consulta: nombres operativos y dos PH](../artifacts/report-list/ui/01-consulta-nombres-planificacion-dos-ph.png).
- [Report List conectado: dos PH con nombres técnicos y UUID](../artifacts/report-list/ui/02-report-list-dos-ph-reales.png).
- [Marcado de una ejecución desde WinUI](../artifacts/report-list/ui/03-report-list-Mark.png).

Evidencia estructurada sin credenciales: [inspección y reimportación](../artifacts/report-list/real-api-inspection.json), [marcado nativo](../artifacts/report-list/native-Mark.json), [persistencia](../artifacts/report-list/real-mark-persistence.json) y [autorización efectiva](../artifacts/report-list/real-authorization.json).

La [aceptación final](../artifacts/report-list/acceptance-final.json) registra las 103 pruebas aprobadas y la coincidencia SHA-256 entre el cliente compilado y el cliente aislado ejecutado. El paquete [ReportList-Identidad-2026-11-19.zip](../ReportList-Identidad-2026-11-19.zip) reúne fuentes cambiadas, este informe, capturas, TRX y evidencias seleccionadas con manifiesto SHA-256; excluye configuración, credenciales y datos de runtime.

## Inspección del sistema anterior

Se verificaron los archivos disponibles en `C:\Users\danny.jimenez\Desktop\Proyectos\LIMS(React)`:

- `Frontend/src/pages/report-list/ReportListPage.tsx`, `reportList.api.ts`, `reportList.scanner.ts` y su prueba de código AR.
- `Backend/app/modules/report_list/router.py` y `Backend/app/modules/jobs_cierres/service.py` / `router.py`.
- La tabla restaurada `public.planificacion_test_map`, `public.planificacion_no_programar`, `public.planificacion_report_check`, `public.planificacion_activa` y `public.planificacion_archivada`.

El esquema real del mapa incluye `nombre_tecnico`, `test_name_tecnico`, `nombre_planificacion`, `nombre_prueba_plan`, `lab`, `activo`, `nombre_tecnico_norm` y `planificacion_nombre_id`. El importador de catálogo ya existente valida alias incompatibles y conserva NO PROGRAMAR. El catálogo local contiene 2,655 entradas; 2,243 entradas programables corresponden a 276 nombres operativos. Esto acredita relaciones de varios nombres técnicos a un nombre operativo; no acredita que deban fusionarse ejecuciones.

`planificacion_report_check.job_prueba` tiene unicidad en el original. Representa una combinación operativa, y no se usó como clave de ejecución en el nuevo módulo. El código fuente del router principal de `planificacion` no está disponible completo en esta versión; parte permanece compilada. No se afirma haber verificado todas sus transformaciones internas ni migrado todo su historial.

## Correspondencia funcional

| Regla verificada del original | Implementación actual |
|---|---|
| Pendientes y comprobadas; filtros JOB, laboratorio y búsqueda | `GET /api/planning/report-list`, paginación de servidor, vistas Pendientes/Completadas/Activas/Archivadas/Todas; filtros por nombre operativo, servicio y Buyer |
| Búsqueda individual y múltiple | JOBs separados por coma, punto y coma, tabulación o salto de línea; máximo 500 |
| Selección individual/múltiple | ListView nativo con selección múltiple; selección de página, copia y apertura del expediente |
| Check manual con usuario, fecha y hora | API usa el actor autenticado y reloj de servidor, fecha de Guatemala; actualiza la ejecución y escribe auditoría en una transacción |
| Desmarcar limpia campos y restaura la prueba | Endpoint de corrección con permiso propio, control de versión y auditoría; conserva la identidad |
| Reutilización de bloque | `router.py::_completion_block`: último actor, ventana de 15 minutos. La implementación serializa las mutaciones y reutiliza solo el bloque más reciente del mismo actor; crea otro al cambiar actor o vencer la ventana |
| Mantener en programación | `report_checks.keep_in_planning`; una completada puede seguir en Activas. Archivada se deriva de completada y no mantener; no se elimina ni copia/destruye una ejecución |
| Alertar al completar la última pendiente | Cuenta ejecuciones distintas del JOB, excluye NO PROGRAMAR y bloquea el aviso si hay filas fuente sin conciliar; persiste aviso y evento histórico. Desmarcar o incorporar otra pendiente reabre el aviso |
| Modos manual/escáner | Reglas de `jobs_cierres/service.py`, incluidos patrones analíticos, exclusiones PH/formaldehído/fibra, alias y tolerancia de nombres de laboratorio |
| Escáner AR | Parser/checksum del original en C#; resuelve tracking en tablas locales verificadas o vínculo persistido; rechaza códigos desconocidos y relaciones ambiguas |
| Excepción administrativa del modo | Checkbox explícito en el diálogo para administradores; comprobación de rol en la API y motivo auditado |
| Autorización | Login y sesión existentes; consulta exige `planning.view`, marcado/escáner `planning.report.mark`, corrección/permanencia `planning.report.review`; administración de accesos sigue siendo de administrador |

Las vistas activa/archivada aplican la regla de visibilidad dentro del modelo nuevo. No escriben las tablas legacy ni pretenden implementar un módulo de Muestreo que no existe en esta vertical .NET.

## Identidad, equivalencias y procedencia

- La columna principal **Prueba** utiliza `PlanningRow.PlanningTest`, con equivalencia aprobada, `Sin equivalencia`, `NO PROGRAMAR` o `WIP sin pruebas`. El nombre técnico nunca se utiliza como sustituto de una equivalencia ausente.
- El nombre técnico sigue disponible en la columna opcional, Report List, expediente y procedencia. El UUID identifica la ejecución en marcado, exportación y auditoría.
- El catálogo persistido se aplica a la importación y sus cambios actualizan ejecuciones de origen Report List. Las clasificaciones manuales independientes se conservan. Las decisiones quedan en historial; la fila fuente no se modifica.
- Dos nombres técnicos que se traduzcan a PH siguen siendo dos UUID. No se agrupan las filas operativas por nombre técnico ni por equivalencia.
- Si un documento no distingue dos apariciones del mismo nombre técnico, ambas filas quedan conservadas en Identidad por conciliar. El operador puede crear o vincular ejecuciones explícitamente en el flujo existente.
- Después de esa resolución, otra carga del **mismo documento Report List** reutiliza los vínculos persistidos de SHA-256 + hoja + fila, incluso si cambian Summary o el corte. La identidad no se calcula solo con el número de fila. Un archivo diferente con repeticiones no conciliables requiere nueva revisión.
- La confirmación conserva su transacción, fingerprint, revisión y control de lote ya aplicado. El marcado no toca los archivos importados ni elimina las ejecuciones manuales.

## Matriz de las diez comprobaciones solicitadas

| Caso | Evidencia y límite |
|---|---|
| 1. Dos PH reales | WinUI + API + PostgreSQL: GUAT26022934, dos nombres técnicos distintos y dos UUID. El caso literal `GUAT-PRUEBA-001 / PH / PH / CROCKING` pasó como fixture PostgreSQL, con conciliación explícita y tres filas |
| 2. Dos traducciones técnicas | Los dos nombres PH anteriores en el catálogo real; prueba automatizada adicional de dos técnicos con un mismo nombre y cambio de catálogo |
| 3. Sin equivalencia | Dos ejecuciones reales del JOB en la bandeja, comprobadas por API; prueba de ausencia de sustitución por nombre técnico |
| 4. Nueva ejecución en JOB existente | Pruebas PostgreSQL de nuevas filas y conservación de UUID; prueba existente con filas reales del XLSX de GUAT25038091-REV3 que incorpora 23 pruebas a una asignación manual |
| 5. Reimportar sin duplicados | Archivos reales completos, confirmación repetida y conteos antes/después iguales; fixture adicional de documento previamente conciliado |
| 6. Marcado actualiza Consulta | Marcado real desde WinUI y lectura autenticada de Consulta/expediente |
| 7. Marcar una repetida | Una PH real marcada, la otra sin marca; fixture de dos PH con el mismo nombre técnico |
| 8. Usuario sin permiso | Login real de cuenta local sintética, consulta permitida, POST 403, versión intacta; cuenta luego inactiva |
| 9. Solo Summary | GUAT25009340-REV7-REV8 conserva fechas WIP y ejecución nula |
| 10. Manual sobrevive importación | Pruebas PostgreSQL con marcado manual y nueva fuente, y prueba con filas reales de GUAT25038091-REV3; no se añaden pruebas ficticias a los datos reales de desarrollo |

Resultados automatizados focalizados: **103 aprobadas, 0 fallidas, 0 omitidas**: 27 reglas Application, 31 PostgreSQL, 25 autorización API y 20 del cliente. Los escenarios PostgreSQL usan bases `lims_planning_ci_*` desechables en el mismo servidor local; los datos sintéticos y las decisiones controladas están identificados en cada prueba. No se presenta ese conjunto como una prueba productiva.

Los TRX están en [artifacts/report-list/tests](../artifacts/report-list/tests). La validación de interfaz es **WinUI automatizada mediante UI Automation**, incluyendo selección y diálogo de marcado reales. No se afirma aceptación manual WPF ni validación de escáner físico, LAN o equipo de otro usuario.

## Archivos cambiados

| Archivos | Cambio |
|---|---|
| `Shared/Lims.Contracts/Planning/PlanningContracts.cs`, `ReportListContracts.cs` | Nombre operativo, permisos y contratos de consulta/marcado/versiones |
| `Backend/Lims.Application/Planning/PlanningModels.cs`, `PlanningReconciler.cs` | Contrato del almacén y reutilización de conciliaciones por documento; conservación de ejecuciones |
| `Backend/Lims.Application/Planning/ReportListRules.cs` | Modos y códigos AR verificados del original |
| `Backend/Lims.Infrastructure/Planning/PostgresReportList.cs` | Consultas, marcado transaccional, bloques, permanencia, alertas, correcciones y tracking |
| `Backend/Lims.Infrastructure/Planning/ReportListSchema.cs`, `Persistence/Migrations/20261119180000_AddExecutionReportList.cs` | Esquema aditivo sin eliminación de datos |
| `Backend/Lims.Infrastructure/Planning/PostgresPlanningStore.cs`, `PostgresPlanningDecisions.cs` | Vínculos de documento e invalidación de avisos al incorporar nuevas pendientes |
| `Backend/Lims.Infrastructure/Planning/PostgresPlanningQueries.cs`, `PlanningColumnFilters.cs` | Orden/filtro/exportación por nombre operativo y conservación del técnico |
| `Backend/Lims.Api/Planning/PlanningEndpoints.cs` | Endpoints bajo la misma API, autorización efectiva y separación de marcado/revisión |
| `Frontend/Lims.Desktop/Planning/PlanningReportListView.cs` | Módulo nativo conectado, selección, filtros, check/corrección, permanencia, escáner y expediente |
| `PlanningApiClient.cs`, `PlanningWorkspacePage.cs` | Cliente HTTP existente, navegación y actualización de Consulta/expedientes |
| `PlanningSheetModel.cs`, `PlanningJobsView.cs`, `PlanningJobStatus.cs` | Prueba operativa en la tabla y expediente; técnico opcional; nombres de eventos históricos |
| `Tests/Backend/Lims.Application.Tests/ReportListRulesTests.cs` | Modos y checksum AR |
| `Tests/Backend/Lims.Infrastructure.Tests/ReportListPostgresTests.cs`, `PlanningPostgresTests.cs` | Repeticiones, idempotencia, catálogo, versiones, bloque, corrección, escáner, manuales y filas reales |
| `Tests/Backend/Lims.Api.Tests/PlanningAuthorizationTests.cs` | Lecturas autenticadas y prohibición de escritura con permiso de consulta |
| `Tests/Frontend/Lims.Desktop.Tests/PlanningSheetTests.cs` | Copia/tabla con nombre operativo y ausencia explícita de equivalencia |
| `scripts/planning/upgrade-execution-report-list.sql` | Equivalente SQL transaccional e idempotente de la migración aditiva |
| `scripts/start-report-list-validation.ps1`, `validate-report-list-api.ps1`, `validate-report-list-desktop.ps1`, `validate-report-list-permissions.ps1`, `package-report-list-evidence.ps1` | Reproducción de validación local y empaquetado verificable, sin imprimir credenciales |

Se conservaron los cambios previamente presentes en `Program.cs`, `appsettings.json` y `scripts/planning/install-planning-interdb.sql`; no forman parte de esta implementación.

## Reproducción local

1. Aplicar primero las migraciones previas de Planning, incluida `SeparatePlanningIdentity`, en el destino de desarrollo autorizado. Aplicar `AddExecutionReportList` con EF o su SQL equivalente. No ejecutar instaladores de cero sobre un Planning existente.
2. Compilar API: `dotnet build Backend/Lims.Api/Lims.Api.csproj --no-restore -m:1 -o artifacts/report-list/api-final`.
3. Compilar cliente: `dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false`.
4. Iniciar la API local con `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start-report-list-validation.ps1 -Restart`; el script comprueba runtime y proceso registrado antes de reiniciar.
5. Usar `scripts/start-planning-development.ps1 -Desktop` y el login original. En Consulta seleccionar Histórico completo y buscar `GUAT26022934`. En Report List filtrar el JOB, prueba PH, laboratorio Chemical y vista Todas.
6. `validate-report-list-api.ps1 -Step Inspect` comprueba reimportación y WIP. `-Step Verify` comprueba el marcado registrado durante esta validación. Los pasos Mark/Restore escriben únicamente en la copia local.

En esta sesión `dotnet tool restore` no pudo acceder a NuGet, por lo que la migración local se aplicó con el SQL equivalente transaccional y su registro en `__EFMigrationsHistory`. Las compilaciones usaron paquetes restaurados previamente.

## Límites verificados

- Los 59 Report List XLSX disponibles no contienen dos filas del mismo JOB con el mismo nombre técnico. La repetición literal del mismo técnico se probó como fixture; el caso real sí contiene dos PH operativas, y también varias AZO y CADMIUM / LEAD que permanecen separadas.
- El escáner está integrado y probado con tracking controlado. La restauración disponible no contiene las tablas de tracking analítico verificadas por el original; un AR sin correspondencia devuelve una validación y no marca por aproximación. Falta aceptación con tracking real y dispositivo físico.
- La copia tiene 15 registros legacy de `planificacion_report_check`, 13 completos. No se trasladaron automáticamente a UUID, porque `job_prueba` no acredita identidad física. Se conservaron intactos. El historial de los nuevos marcados sí está persistido por ejecución. No se afirma una migración completa de registros históricos React/Python.
- No se verificaron todas las transformaciones del router de Planificación que solo está disponible compilado. No se crearon/consolidaron pruebas por suposición.
- La aceptación conseguida es local con copia restaurada. No equivale a despliegue productivo ni a aceptación manual por el operador final.
