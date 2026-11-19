# Estado e importación real unificada en Planificación

Implementado en el LIMS WinUI existente. Validación local del 17–18 de noviembre de 2026. Sin otra aplicación, prototipo ni rediseño general.

## Revisión previa

La matriz se preparó antes de modificar código; esta versión añade resultados. Se inspeccionó el HTML original extraído confirmado por el usuario: `C:/Users/danny.jimenez/Desktop/programas/App_web/Job_Consulta_V_final.html`, Summary → WIP (4088–4230) y cargas de pruebas (4611–4940). No se encontró el ZIP en la ruta indicada. No se ejecutó su JavaScript ni se accedió a Supabase.

| Fuente o función | Implementación encontrada | Corrección realizada |
|---|---|---|
| Summary: Sample Status | Archivado, sin columna | Estado principal de solo lectura, original y con procedencia |
| Status Sample / Status Job / Job Status | Sin dimensiones estructuradas | Alias explícitos; estado de muestra y de JOB separados; conflicto entre alias bloquea |
| Customer_status | Solo fuente | Se conserva separado: Hold Payment no activa On Hold |
| Close Date | Descartaba episodios nuevos cerrados | Conserva fecha, episodios y pruebas; no cierra ni elimina datos locales |
| PI Currency / PI ExRate | Esperaba PI currency, sin tasa estructurada | Encabezados observados; USD explícito o GTQ / tasa positiva según HTML; nunca tasa predeterminada 1 |
| Summary + Report List | Importador conjunto transaccional | Reutilizado; diferencias por campo, filtros, bandejas paginadas e historial |
| Report List sin Summary | Filas sin episodio no consultables | Episodio sin WIP inventado; Summary posterior conserva episodio y ejecuciones |
| Repeticiones / reanálisis | Conciliación ExecutionIdentity | Conserva GUID independientes; no fusiona múltiples filas o candidatos por JOB + prueba |
| Solo Summary con pruebas manuales | MissingTests se resolvía al agregar una prueba | Bandeja independiente; resolución explícita, autorizada y auditada |
| Ausencia en un archivo | No eliminaba registros | Se conserva; los cortes son metadatos, nunca filtros artificiales |
| Consulta y WIP | episodes + executions | Misma API/base, actualizadas al confirmar |

Se revisaron proyectos, DbContext, migraciones, endpoints, importador, consultas y vistas. El host conserva login, permisos y navegación. Planificación usa Npgsql y migraciones EF aditivas: `revision`, `batches`, `episodes`, `executions`, `source_rows`, `equivalences`, `incidents`, `history`, `job_controls`.

## Fuentes reales y autoridad

| Archivo | Filas | Cobertura comprobada |
|---|---:|---|
| Summary 13, Desktop/Backup | 15,480 | Reg Date: 2026-06-01 a 2026-11-12 |
| Report List 13, Desktop/Backup | 8,614 | No tiene fechas para acreditar cobertura temporal |
| Summary 17, Downloads | 15,675 | Reg Date: 2026-06-01 a 2026-11-16 |
| Report List 17, Downloads | 9,400 | No tiene fechas para acreditar cobertura temporal |

Nombres y encabezados completos: `source-inspection.json` en la evidencia. SHA256 del Summary 17: `BE938046FC5196DFDDE6ACD5C84EF2CA33D37834D91FDA6DA2571F8733D2F56F`. Report List 17: `3BC9952773B1AEDD0CC8C686FF32D0B667BDAEEB3B2E34A99E8D2341BEDA939A`.

Summary tiene 51 encabezados. Es autoridad para Buyer, Vendor, Service, ingreso, Due Date, importe, moneda, tasa y estados oficiales. Sample Status contiene 15 valores originales. No hay Job Status en estos ejemplos. Customer_status es comercial y se conserva aparte. Close Date no se confunde con digitación.

Report List tiene Report_no, Applicant_Name, Buyer_Name, Service_Type, Reporter, Coordinator, NOTE_FROM_COORDINATOR, NOTE_FROM_LAB y Test_Name. Se archivan todos los encabezados y valores; no sobrescribe WIP del Summary. La equivalencia y el laboratorio del catálogo conservan su procedencia y permanecen separados del nombre original.

El HTML descartaba sufijos y cierres y agrupaba JOB + prueba. Esas reglas destructivas se sustituyeron por conservación, GUID y conciliación explícita.

## Resultado funcional

Vista principal: Fecha Ingreso | Due Date | Estado | JOB | Prueba | Laboratorio | Hora | Fecha Prueba | Estado de digitación | Digitado por.

Estado admite filtro individual, ordenamiento y copia. Está protegido frente a pegado/edición; sin fuente muestra No informado. El expediente separa Sample Status, Job Status, Close Date, Customer_status y PI ExRate, con lote, fila, actor, fecha e historial antes/después. Los indicadores operativos mantienen sus reglas explícitas: el estado oficial es independiente de hora y fecha de digitación.

El centro permite seleccionar ambos XLSX, corte y fecha; validar; conciliar; revisar diferencias por campo; confirmar; consultar bandejas e historial. Solo Summary conserva Pendiente de completar, Retenido con motivo y actor auditado, y Resuelto manualmente. Agregar una prueba no resuelve automáticamente esta bandeja. Vendor vacío tiene su propia incidencia.

La confirmación comprueba creador, revisión global y vigencia del preview. Archivos y SHA se conservan; las escrituras son transaccionales. Los campos manuales y las identidades sobreviven. La repetición del mismo lote es idempotente; otro corte registra una observación sin duplicar ejecuciones. Se amplió el escritor COPY existente con tabla temporal y upsert protegido, evitando búsquedas cuadráticas.

## Aplicación aislada e integridad

Único destino operativo de aceptación: **127.0.0.1:55441 / lims_planning_dev**. Respaldo previo: `artifacts/planificacion-dev/pre-estado-importacion-20261117-232253.backup`. Migración aditiva: `20261118010000_AddPlanningOfficialState`.

| Comprobación | Resultado |
|---|---:|
| Episodios iniciales conservados, mismo UUID | 770 de 770 |
| Ejecuciones iniciales conservadas, mismo UUID | 8,445 de 8,445 |
| Episodios después de los lotes | 15,685 |
| Ejecuciones después de los lotes | 10,986 |
| Episodios con Sample Status real | 15,675 |
| Registros en Consulta histórica | 25,709 |
| Incidencias Solo Summary pendientes | 14,723 |
| Pruebas manuales agregadas a esta copia operativa | 0 |

Se confirmaron los archivos reales del 13 y 17. El segundo lote incorporó 195 episodios y 2,372 ejecuciones. Los mismos archivos del 17 se seleccionaron y confirmaron desde WinUI con corte Mediodía: **0 JOBs nuevos y 0 pruebas nuevas**.

Se encontraron **393 cambios reales de Sample Status** entre ambos Summary. Ejemplo: GUAT26016315, Report Closed → Invoice Rejected, con antes/después y fuente. GUAT25038091-REV3 conserva sus **24 ejecuciones**.

No se escribieron datos en InterDB, producción, Supabase ni planificacion_migracion_test. Las pruebas PostgreSQL usan bases CI efímeras del servidor local permitido. No se afirma validación LAN ni de producción.

## Escenarios comprobados

| Escenario | Evidencia y límite |
|---|---|
| En ambos archivos | API/WinUI: GUAT25038091-REV3, 24 pruebas |
| Solo Summary | API/bandeja: GUAT25002016-REV4-REV5-RE, expediente y Close Date conservados |
| Solo Report List | API: GUAT26010723, pruebas consultables sin inventar Summary |
| Pruebas manuales existentes | CI con filas reales y decisiones de prueba: conserva GUID, clasificación y origen manual |
| Reimportación | Mismo lote AlreadyApplied en CI; otro corte real confirmado desde WinUI no duplica |
| Modificación de estado | 393 cambios reales; API, auditoría y tabla nativa de diferencias |
| Repetición / reanálisis | Sufijos REV reales conservados. XLSX del 17 sin duplicados JOB+prueba; repeticiones explícitas probadas en CI, también con filas reales |
| Diferentes coberturas | Archivos completos aceptados; aviso no bloqueante |
| Vendor vacío | GUAT26011849, incidencia independiente |
| Consulta actualizada | Confirmación nativa, recarga de Consulta y expediente sobre la misma base |

La prueba CI con datos reales usa un subconjunto explícito de los XLSX originales y clasificación restaurada. Sus asignaciones manuales son decisiones de prueba, no información oficial ni decisiones históricas recuperadas. No deja pruebas ficticias en la copia operativa.

**62 pruebas automatizadas aprobadas, 0 fallos y 0 omisiones**: 11 de aplicación, 17 PostgreSQL (incluye XLSX reales), 18 de selección/copia/compatibilidad y 16 de autorización. API y cliente compilan con 0 errores y 0 advertencias. Logs incluidos en la entrega.

Además, **21 comprobaciones nativas** aprobaron selección de una celda y rango 5 × 4, copia del Estado original, columna completa, arrastre, Shift+clic, menú contextual, desplazamiento de selección, Ctrl+V, protección de campos oficiales, Escape, Enter, Tab, expediente, Teams, paginación y filtros. Las diez observaciones temporales se restauraron por API con sus versiones y valores previos; se conservó la auditoría.

La verificación x64 se completó en dos sesiones: 18 comprobaciones de interacción y 5 de filtros, con 2 comunes (21 distintas). La compilación final añade la sincronización del día y corte al consultar un lote histórico; se volvió a ejecutar la prueba de filtros y se capturaron Consulta, expediente, diferencias, historial y bandeja. `acceptance-combined.json` distingue las compilaciones y `assembly-evidence.json` comprueba que el ejecutable capturado coincide con la salida x64 normal.

## Información que los archivos no permiten completar

- Report List no informa ID oficial de ejecución, laboratorio, hora, fecha de prueba ni digitador. No se inventan; la ambigüedad requiere conciliación explícita.
- No se puede acreditar el rango temporal de Report List.
- No hay Job Status en estos ejemplos; se conserva separado cuando se informe.
- Tres importes del Summary 17 no permiten obtener USD con moneda explícita o GTQ y tasa positiva. Se mantienen desconocidos con aviso y originales conservados.
- On Hold y etiquetas heredados no vienen en estos XLSX. Se preservan los controles persistidos del LIMS; Customer_status no los sustituye.

## Código y reproducción

Contratos: `Shared/Lims.Contracts/Planning/PlanningContracts.cs`. Lectura/conciliación: `Backend/Lims.Application/Planning/PlanningImportReader.cs`, `PlanningReconciler.cs`. Persistencia: `PostgresPlanningStore.cs`, `PlanningImportBulkWriter.cs`, `PostgresPlanningQueries.cs`, `PostgresPlanningDecisions.cs`, `PlanningColumnFilters.cs`, `PlanningOfficialSchema.cs`. API: `PlanningEndpoints.cs`.

Cliente: `PlanningImportsView.cs`, `PlanningImportTables.cs`, `PlanningSheetModel.cs`, `PlanningSheetGrid.cs`, `PlanningDossierWindow.cs`, `PlanningDossierControls.cs`, `PlanningJobStatus.cs`, `PlanningWorkspacePage.cs`, `PlanningApiClient.cs`.

Reproducción: `scripts/validate-planning-real-import.ps1`, `verify-planning-real-results.ps1`, `validate-planning-real-desktop.ps1`, `scripts/planning/inspect-tips-real.py`. Los scripts comprueban el destino aislado y leen las credenciales de la configuración local ignorada por Git; no las incluyen.

## Entrega de evidencias

Paquete: `artifacts/validation/Planificacion-Estado-Importacion-Real-2026-11-18.zip`. Carpeta abierta: `artifacts/validation/planificacion-estado-importacion-real-2026-11-18`.

- `capturas/07-consulta-estado-24-pruebas.png`: diez columnas principales, indicadores y pie integrado; sin paginación innecesaria.
- `capturas/06-cambios-estado-reales.png`: diferencias originales antes/después y metadatos correctos del lote histórico.
- `capturas/08-expediente-estado-fuente.png`: dimensiones oficiales separadas, fecha y procedencia.
- `capturas/04-confirmacion-nativa.png`: confirmación real del corte Mediodía sin duplicados.
- `capturas/11-bandeja-solo-summary.png`: bandeja persistida y paginada desde el servidor.

Incluye matriz, resultados de API, aceptación nativa, logs de compilación/pruebas y manifiesto SHA256. No incluye credenciales, notas privadas, archivos fuente completos ni el respaldo de PostgreSQL. Las capturas del flujo de importación son del 17; las de consulta y revisión son del 18. Son imágenes directas del cliente nativo.
