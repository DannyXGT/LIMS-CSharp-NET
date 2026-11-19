# Consulta de Jobs: matriz funcional previa a cambios

Fecha: 17/11/2026. Esta matriz se escribió **antes de modificar el código de esta corrección**.

## Referencia inspeccionada

Ruta confirmada por el usuario: `C:\Users\danny.jimenez\Desktop\programas\App_web`. Se inspeccionó directamente `Job_Consulta_V_final.html` (310.404 bytes, SHA256 `BEF2939A1797AC88F6B65F45993D040DBF9005397BE67AFE0976FEB9CACBC52E`). La carpeta ya está extraída; no se encontró ni se afirma haber abierto otro ZIP. No se ejecutó su JavaScript ni se accedió a Supabase.

Funciones leídas: `COLUMNS` (838–851), `jobEnHold`/persistencia On Hold (893–951), etiquetas (960–1019), `applyFilters` (1201–1264), `loadAll` (1671–1688), `buscarJobsEnNube` (1690 en adelante), `pruebaCompleta` y `estadoVence` (3346–3358).

## Matriz de correspondencia

| Función original | Campo o regla original | Implementación actual del LIMS, antes de esta corrección | Diferencia | Corrección necesaria |
| --- | --- | --- | --- | --- |
| Registros | `DB.length`; muestra por separado `view.length` cuando hay filtros | Encabezado con JOBs, ejecuciones, digitadas y desconocidas | No reproduce el conjunto de indicadores solicitado | Registros del conjunto **filtrado completo** calculados en API; indicar alcance. Se sigue la petición actual de que los cuatro indicadores respeten filtros, evitando el total sin filtrar del HTML |
| Prueba completa | `pruebaCompleta`: hora **y** fecha no vacías | `digitized_at IS NOT NULL` se toma como digitada | Una hora sin fecha se considera completa | Exigir hora y fecha para completitud operativa; conservar ambos campos y la información de digitación por separado |
| Pendientes | En `view`: excluir On Hold, excluir completas; contar las restantes | Contador cuenta solo filas sin hora **con** fecha; filtro Pending cuenta sin hora y excluye NO PROGRAMAR | Contador y filtro discrepan; desconocidas se pierden del pendiente operativo | Un único predicado de pendiente: ejecución existente, sin On Hold y sin hora+fecha completas. NO PROGRAMAR no se excluye implícitamente porque el HTML no lo hace |
| Vencidas | Pendiente no retenida y `estadoVence(d)==='vencido'`; Due Date **hoy o anterior** | Filtro vence antes de hoy; no existe indicador | Falta hoy y falta excluir On Hold | API: pendiente operativo y `due_date <= hoy`, en zona de Guatemala; no inventar vencimiento si falta fecha |
| Pruebas On Hold | Contar **todas** las filas retenidas antes de comprobar completitud; `onHold` procede de `jobs_on_hold` | Solo hay incidencias con estado Held; no son una lista de JOBs On Hold | Retener una incidencia no equivale a retener todo un JOB | Registro persistente por JOB, independiente de incidencias, auditado y con concurrencia; contar pruebas y JOBs distintos dentro de filtros/alcance. Distinguir la cobertura heredada no integrada |
| JOBs On Hold entre paréntesis | El HTML usa `onHold.size` global, aunque haya filtros | No existe | El global del HTML puede mezclar otros períodos | La petición actual exige filtros: mostrar JOBs retenidos del **mismo conjunto**; no mezclar globales |
| Período operativo | `loadAll`: `due_date >= fecha de hace 20 días`; sin límite superior | API/UI cargan todo el histórico sin anunciarlo | Alcance ambiguo | Selector operativo/histórico, límite y fecha de referencia explícitos en respuesta y encabezado; paginar en servidor |
| Histórico y búsqueda múltiple | Histórico completo opcional; búsqueda explícita puede incorporar JOBs fuera de memoria | Búsqueda múltiple autenticada y paginada; histórico implícito | No informa si se está consultando histórico o período | Mantener normalización y ejecución independiente. Hacer explícito el alcance; buscar JOBs fuera del período mediante histórico, sin mezclar silenciosamente estadísticas |
| Orden de columnas | Fecha Ingreso, Due Date, JOB, Prueba, Laboratorio, Hora, Fecha Prueba, marcado, Digitado por | JOB primero; Servicio, Responsable, Observación y Origen se imponen en vista inicial | Orden distinto y exceso de campos principales | Adoptar las nueve columnas solicitadas; marcado como estado de solo lectura. Extras configurables y disponibles en expediente |
| Prueba / equivalencia | `prueba` es el valor mostrado; clasificación puede traducirlo | `original_test` y `equivalence` se almacenan separados | Debe conservarse identidad original al cambiar presentación | Mantener Prueba original como Prueba principal; equivalencia separada y opcional; nunca colapsar ejecuciones por JOB+nombre |
| Filtro individual | `applyFilters`: AND entre columnas; fechas exactas normalizadas, texto contiene y JOB incluye etiqueta | Ocho cabeceras abren el mismo panel; Hora, Fecha Prueba y Digitado por no tienen filtro | No existe filtro individual en las nueve columnas | Menú nativo por cabecera, calendario para fechas, filtros combinados aplicados en SQL al conjunto completo |
| Ordenamiento | Tipo de columna; vacíos al final | SQL con lista blanca y desempate por episodio/ejecución | Estado ordena por hora, no por estado completo | Preservar orden estable, ampliar solo los campos necesarios y ordenar el estado realmente mostrado |
| Selección / copia / pegado | Selección y clipboard de matriz | Modelo Excel virtualizado con notas autorizadas, auditoría y versiones | Depende de índices antiguos JOB=0 en algunos controles/guiones | Conservar modelo y atajos; adaptar JOB fijo a su nueva posición y pruebas de aceptación al nuevo orden |
| Etiquetas | `jobs_etiquetas`, texto por JOB, normalización, búsqueda de JOB también encuentra etiqueta | Sin representación ni filtro | Etiquetas heredadas no integradas | Persistencia nativa por JOB con permiso WIP, motivo e historial; identificación visible, filtro y consulta. No inventar etiquetas desde Buyer, OEKO u otros campos sin equivalencia validada |
| Estado del JOB | Resumen, pruebas, WIP y datos adicionales | Expediente nativo no modal con historial, fuente y Teams | Falta estado On Hold/etiqueta | Conservar expediente y añadir consulta/gestión autorizada de control operativo; no convertir completitud en cierre TIPS |
| Exportar | Exporta resultados filtrados | CSV de API con límite de 50.000 y protección contra fórmulas | Faltarán alcance y controles nuevos | Reutilizar SQL de filtros/alcance, conservar original y equivalencia, añadir On Hold/etiqueta sin exportar solo la página |
| Permisos y datos oficiales | HTML comprueba `puedeDigitar` en cliente; marcado editable | API autentica y autoriza edición de notas/WIP | Debe mantenerse la frontera del nuevo LIMS | On Hold/etiquetas autorizados en servidor, con motivo y versión. No añadir edición de hora/fecha/digitador ni checks en Consulta |
| Datos completos | HTML combina tablas independientes sin validar cobertura del respaldo del LIMS | Lectura local: 8.445 ejecuciones, ninguna con hora/fecha; 4 filas Summary sin pruebas; sin tablas `jobs_on_hold`/`jobs_etiquetas` | No se puede afirmar equivalencia de datos históricos ni ausencia de retenciones | Declarar cobertura parcial, filas incompletas y fuentes no integradas desde API. Los nuevos controles nativos son persistentes; su aceptación local no se presenta como recuperación de las listas históricas |

## Evidencia anterior a los cambios

Lecturas únicamente de `127.0.0.1:55441/lims_planning_dev`: 8.445 ejecuciones, 0 con `digitized_at`, 0 con `tested_on`; 4 episodios con incidencia MissingTests pendiente. La base no contiene `jobs_on_hold` ni `jobs_etiquetas`. Las fuentes Report List importadas solo incluyen Buyer_Name, Coordinator, NOTE_FROM_COORDINATOR, NOTE_FROM_LAB, Report_no, Reporter, Service_Type y Test_Name. No se debe inferir digitación real ni On Hold heredado a partir de esas fuentes.

Se conservarán la identidad de ejecución, las fuentes, el historial, la interfaz aprobada y el pie compacto. Las escrituras de aceptación se limitarán a la base local aislada; cualquier JOB real puesto On Hold para comprobar persistencia se identificará como validación local y se devolverá a su estado previo, conservando auditoría.

## Corrección implementada

La API utiliza una consulta filtrada única para obtener filas, contadores y exportación. El conteo y la página se leen en una transacción `RepeatableRead`. No hay estadísticas calculadas sobre las 100 filas visibles.

Reglas explícitas:

```text
Completa = ejecución existente con hora Y fecha de prueba
Pendiente = ejecución existente, no On Hold y no completa
Vencida = pendiente y Due Date <= hoy en Guatemala
On Hold = registro de control persistido para el JOB; incluye pruebas completas o incompletas
Sin información de digitación = ejecución sin hora y sin fecha
```

Sin información puede coexistir con Pendiente o con On Hold. NO PROGRAMAR se conserva en un campo independiente y una columna opcional; no se presenta como estado de digitación ni se excluye implícitamente de los pendientes de la regla original. Las filas Summary sin ejecución se cuentan como registros y se declaran en cobertura; no se inventa una prueba para ellas.

El encabezado conserva su diseño y muestra Registros, Pendientes, Vencidas y Pruebas On Hold con JOBs entre paréntesis. El subtítulo indica **Período operativo desde una fecha** o **Histórico completo**, además de **Cobertura parcial**. El período utiliza Due Date desde hoy menos 20 días, sin límite superior; los filtros elegidos siguen aplicándose. Histórico significa todos los registros importados en el nuevo LIMS, no una recuperación automática de las fuentes externas ausentes. Búsqueda múltiple anuncia y consulta histórico completo de forma explícita.

Cada una de las nueve cabeceras tiene su filtro propio. Las fechas ofrecen calendario y comparación exacta; los filtros se combinan en SQL con las condiciones del panel. Las columnas opcionales conservan original, equivalencia, Servicio, Buyer, Vendor, USD, Responsable, Observación, On Hold, etiqueta, NO PROGRAMAR y procedencia. Fecha Ingreso, Due Date y JOB permanecen fijos al desplazar horizontalmente, como las tres columnas iniciales del HTML. La copia contiene valores originales, sin añadir etiquetas al número de JOB.

El expediente permite consultar y, con `planning.wip.edit`, gestionar On Hold/etiqueta. `planning.job_controls` conserva la decisión por JOB, versión, responsable, fecha y motivo de retención. Cada decisión se audita en los episodios del JOB, sin unir ni sustituir sus ejecuciones. Se comprueba concurrencia en servidor y se rechaza el pegado de notas a JOBs retenidos, de forma atómica. Las incidencias Held existentes conservan su significado independiente. No se agrega ninguna ruta de marcado ni de edición de información oficial TIPS.

## Comprobación posterior

| Capa | Resultado |
| --- | --- |
| Compilación API y cliente | Sin errores ni advertencias |
| Reconciliación / aplicación | 8 pruebas correctas |
| PostgreSQL aislado | 13 pruebas correctas: completitud, hoy incluido, On Hold, etiquetas, concurrencia, fechas, paginación, exportación, hora de Guatemala y fuentes |
| Tabla / compatibilidad / búsqueda múltiple | 17 pruebas correctas |
| Autorización de API | 16 pruebas correctas, incluida denegación directa de gestión de On Hold/etiquetas a usuarios de solo consulta |
| Cliente nativo 1920×1080 y 1366×768, 125 % | 8 verificaciones específicas correctas por tamaño; indicadores leídos de la UI y contrastados con API; calendario abre, navega y aplica fecha real |
| Regresión nativa Excel | 20 comprobaciones correctas: celda, rango 5×4, arrastre, Shift, TSV, menú, desplazamiento, Ctrl+V de 10 valores, persistencia, campos protegidos, doble clic, Enter, Escape, Tab, expediente, Teams, Solo tabla, búsqueda múltiple y páginas |
| JavaScript original contra API | Cuatro conjuntos contrastados sobre las 8.449 filas descargadas de la API autenticada: histórico, período, JOB fuente y fecha de ingreso; repetidos con retención persistida y después de restaurarla |

El oráculo `scripts/planning/verify-consulta-html.cjs` extrae exclusivamente `COLUMNS`, `applyFilters`, `toISO`, `pruebaCompleta`, `estadoVence`, `jobEnHold` y `jobEtiqueta` del HTML confirmado. Ejecuta esas funciones con datos de API y un receptor mínimo de contadores, sin ejecutar conexiones, importaciones, almacenamiento o eventos del HTML. Las cuatro filas Summary sin ejecución se distinguen explícitamente de las pruebas.

En la copia local, restaurada la prueba de On Hold:

| Alcance | Registros | Pendientes | Vencidas | Pruebas / JOBs On Hold |
| --- | ---: | ---: | ---: | ---: |
| Histórico LIMS | 8.449 | 8.445 | 7.884 | 0 / 0 |
| Operativo desde 2026-10-28 | 6.221 | 6.217 | 5.656 | 0 / 0 |
| GUAT25038091-REV3 | 24 | 24 | 24 | 0 / 0 |
| Mismo JOB, retenido mediante UI para aceptación local | 24 | 0 | 0 | 24 / 1 |

La retención y la etiqueta `VALIDACION LOCAL` se guardaron **en PostgreSQL**, se releeron desde API y se compararon con el JS original. El JOB se devolvió después a su control previo, conservando historial. No son una retención histórica acreditada ni datos operativos productivos. Las filas, identidades y valores importados permanecieron intactos.

## Archivos de esta corrección

- Contratos y API: `Shared/Lims.Contracts/Planning/PlanningContracts.cs`, `Backend/Lims.Application/Planning/PlanningModels.cs`, `Backend/Lims.Api/Planning/PlanningEndpoints.cs`.
- Persistencia y consultas: `PostgresPlanningQueries.cs`, `PlanningColumnFilters.cs`, `PlanningJobControls.cs`, `PlanningControlsSchema.cs`, `PlanningNotes.cs`, en `Backend/Lims.Infrastructure/Planning/`; migración `20261117220000_AddPlanningJobControls.cs`.
- Cliente: `ConsultationHeader.cs`, `PlanningJobsView.cs`, `PlanningConsultationFilters.cs`, `PlanningSheetModel.cs`, `PlanningSheetGrid.cs`, `PlanningApiClient.cs`, `PlanningWorkspacePage.cs`, `PlanningDossierWindow.cs`, `PlanningDossierControls.cs`, `PlanningJobStatus.cs`, `PlanningMultiSearchWindow.cs`, en `Frontend/Lims.Desktop/Planning/`.
- Pruebas: `PlanningPostgresTests.cs`, `PlanningAuthorizationTests.cs`, `PlanningSheetTests.cs`.
- Aceptación: `scripts/validate-consulta-html.ps1`, `scripts/validate-consulta-html-api.ps1`, `scripts/planning/verify-consulta-html.cjs`; adaptación de `scripts/validate-consulta-jobs.ps1` al nuevo orden y columnas opcionales.

## Límite de datos y entorno

Solo se aplicó la migración aditiva de controles a `127.0.0.1:55441/lims_planning_dev`, después de respaldar el esquema y sus datos en `artifacts/planificacion-dev/pre-consulta-html-20261117-221455.backup`. No se modificaron InterDB, Supabase ni otros módulos.

La cobertura heredada **no está completa**. No existen las listas originales `jobs_on_hold`/`jobs_etiquetas` en el respaldo. Las 8.445 ejecuciones importadas no tienen hora/fecha de digitación; una lectura adicional de las 7.465 filas de `public.planificacion_activa` también devuelve cero valores en `report_completado_at`, `report_fecha_prueba` y `report_horario`. La ausencia no se convierte en una certificación de no digitación, ni en ausencia probada de On Hold/etiquetas históricas. La API y el encabezado advierten cobertura parcial; no se reconstruyen esas fuentes por similitud de JOB+prueba.

Las capturas son del ejecutable WinUI real mediante `PrintWindow`, con ventanas de los tamaños indicados, sin imágenes generadas ni reescalado. Se reúnen con informes y hashes en `artifacts/validation/consulta-jobs-equivalencia-html-2026-11-17/`. Esta evidencia corresponde a una copia local con datos históricos y no valida producción, LAN, equipos físicos mínimos ni el cierre oficial TIPS.
