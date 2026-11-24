# Comparación de Report List: React y .NET

Revisión de código del 23 de noviembre de 2026. React: `3b0ce2f`; .NET: `5088f0c`. Los archivos de Report List y Jobs en cierres revisados en React no tenían cambios locales. Esta revisión compara el código disponible y la captura del usuario; no certifica los binarios desplegados ni mide la fluidez del equipo del usuario.

La referencia anterior de presentación fue **Planificación** de React. Eso no demuestra equivalencia con **Report List**. El Report List nativo conserva reglas y auditoría, pero difiere en la interacción, el origen del estado operativo, la composición de la tabla y el seguimiento de cierres.

## Fuentes revisadas

Rutas React relativas a `C:/Users/danny.jimenez/Desktop/Proyectos/LIMS(React)`:

- **R1**: `Frontend/src/pages/report-list/ReportListPage.tsx`.
- **R2**: `Frontend/src/pages/report-list/reportList.grid.css`.
- **R3**: `Frontend/src/pages/report-list/reportList.api.ts`.
- **R4**: `Backend/app/modules/report_list/router.py`.
- **R5**: `Backend/app/modules/jobs_cierres/service.py`.
- **R6**: `Backend/app/modules/jobs_cierres/router.py`.
- **R7**: `Frontend/src/pages/jobs-cierres/JobsCierresPage.tsx`.
- **R8**: `Frontend/src/pages/AppShellPage.tsx`.

Rutas .NET relativas a este repositorio:

- **N1**: [PlanningReportListView.cs](../Frontend/Lims.Desktop/Planning/PlanningReportListView.cs).
- **N2**: [ReportListGrid.cs](../Frontend/Lims.Desktop/Planning/ReportListGrid.cs).
- **N3**: [ReportListTableModel.cs](../Frontend/Lims.Desktop/Planning/ReportListTableModel.cs).
- **N4**: [ReportListRows.cs](../Frontend/Lims.Desktop/Planning/ReportListRows.cs).
- **N5**: [ReportTableViewport.cs](../Frontend/Lims.Desktop/Planning/ReportTableViewport.cs).
- **N6**: [PostgresReportList.cs](../Backend/Lims.Infrastructure/Planning/PostgresReportList.cs).
- **N7**: [ReportListCatalogue.cs](../Backend/Lims.Infrastructure/Planning/ReportListCatalogue.cs).
- **N8**: [ReactPlanningIntegration.cs](../Backend/Lims.Infrastructure/Planning/ReactPlanningIntegration.cs).
- **N9**: [PostgresReportSchedules.cs](../Backend/Lims.Infrastructure/Planning/PostgresReportSchedules.cs).
- **N10**: [ReportListRules.cs](../Backend/Lims.Application/Planning/ReportListRules.cs).

## Diferencias confirmadas

| Aspecto | React | .NET actual | Consecuencia |
| --- | --- | --- | --- |
| Columnas iniciales | BLOQUE, JOB, PRUEBA, Laboratorio, CLIENTE, MANTENER EN MUESTREO, VERIFICADO POR, FECHA, HORA, CHK (R1:1447–1588). | Fecha de ingreso, Due Date, JOB, Prueba, Laboratorio, Cliente, Hora, Fecha de prueba, Digitado por, CHK. Bloque y Mantener son opcionales (N3:14–23). | La tabla inicial no reproduce la composición operativa de React. Las fechas de ingreso/vencimiento ocupan espacio que React dedica a otras acciones. |
| Distribución | Columnas de datos con anchos mínimos y `flex`; bloque y checks fijos (R1:1429–1547). | Anchos fijos guardados por usuario; suma del encabezado y filas, con ajuste manual (N2:137–195, N3:14–23). | No reparte automáticamente el espacio libre. Cliente y Prueba tienen proporciones distintas. |
| Densidad y aspecto | AG Grid con filas 34 y encabezados 38 en las props activas; CSS declara 32/36. Texto base 11, encabezados 9.5, alineación central, bordes de baja opacidad, hover y alternancia tenue (R1:1775–1780, R2). | Filas 32 y encabezado 40; texto de celdas 12, encabezados hasta dos líneas, alineación principalmente izquierda, nombre en seminegrita y bordes del tema en cada celda (N2:33, 137–189, 234–283). | La sensación de tabla tosca también es de composición y contraste. Reducir la altura de fila por sí solo no reproduce React. |
| Color de cliente | Cliente tipo A en dorado (R1:1490–1491, R2). | No hay ese tratamiento en el renderizador ni `tipo_cliente` en su modelo de tabla. | Falta trasladar la clasificación desde su fuente real; no debe deducirse del nombre del cliente. |
| Completados y bloques | Filas completas verdes, bloque con número, contador, continuidad y bordes alternos; completados organizados por bloque (R1:286–353, 1601–1609; R2). | Bloque como texto opcional; acentos en determinadas celdas guardadas, sin renderer de agrupación equivalente (N2:224–283, N3:21). | Aunque el número se persiste, falta su presentación operativa. |
| Significado del CHK | Pulsar guarda inmediatamente; doble clic o Enter alternan el check. Bloquea solo la fila que está guardando, actualiza esa fila y la retira de la vista cuando corresponde (R1:1262–1427). | CHK marca una selección temporal; después se pulsa Guardar digitación y se confirma en un diálogo. Enter navega, Espacio selecciona; corregir exige menú y motivo (N1:213–231, 249–261; N2:286–311). | Una diferencia de flujo, no un detalle de estilo. Selección temporal y digitación deben seguir siendo estados distintos aunque se simplifique la interacción. |
| Vista después de guardar | Mantiene Pendientes/Checkeados y modifica el registro afectado (R1:1337–1367). | Cambia a Todas y vuelve a cargar después de guardar digitación (N1:229–232). | Pierde continuidad de trabajo y posición. |
| Mantener en Muestreo | Checkbox visible, guardado por fila, sin diálogo de motivo; invalida Programación (R1:1206–1260, 1494–1531). | Columna opcional; abre diálogo, requiere permiso de revisión y motivo y después recarga (N1:263–272, N3:21). | La función existe, pero está menos accesible y tiene más pasos. |
| Permanencia entre aplicaciones | Guarda en `public.planificacion_report_check`; al completar archiva la fila activa, y al mantener/corregir puede restaurarla. Si se mantiene, la copia operativa permanece pendiente para Muestreo (R4:859–900, R5:578–654). | CHK y permanencia escriben `planning.executions` y `planning.report_checks`. Esas operaciones no escriben `public.planificacion_report_check` ni trasladan activa/archivada (N6:99–184, 203–213). | Compartir catálogo y tablas de importación no sincroniza el estado de Report List. Hay una brecha de integración funcional. |
| Origen de los listados | Pendientes delega en `planificacion.listar_activa` y agrega checks de React; Checkeados consulta `public.planificacion_report_check` (R4:964–1153). | Lee episodios/ejecuciones nativas, añade catálogo actual para ciertos pendientes y filtra admisiones cuando existe el puente (N6:29–66, N7). | No está demostrado que ambos devuelvan el mismo conjunto de filas o estado. |
| Identidad | `job_prueba` es clave de fila y del check; frontend elimina repeticiones de esa clave y backend hace `ON CONFLICT(job_prueba)` (R1:178–184, 252–267; R4:806). | UUID por ejecución; conserva repeticiones con distinta identidad (N2:28, N6:105–121). | Tres filas CADMIUM / LEAD no se deben borrar por tener el mismo nombre. Primero hay que relacionar ejecuciones técnicas con la fila operativa de React. |
| Habilitación manual/scanner | CHK deshabilitado desde la celda cuando el modo no es manual, salvo administrador (R1:1565–1578). | `CanMark` revisa equivalencia, retención y programación, pero no el modo; el backend rechaza al guardar si requiere scanner (N3:50, N2:218, N6:127–129). | Se puede seleccionar una prueba que luego no se permite digitar manualmente. La captura debe orientar al usuario antes de guardar. |
| Scanner | Conexión serial Electron con reconexión y estado; captura USB/teclado y pegado de AR a nivel de ventana, con validación y guardado (R1:736–852, 976–1160). | Entrada AR dentro de un diálogo con foco y Enter; comando condicionado a disponibilidad de tracking. Este flujo no implementa la escucha serial/global equivalente (N1:146–159, 274–280). | El lector no tiene la misma experiencia de uso. La validación de un código y la conexión física son límites distintos. |
| Reloj | Visible en toolbar, actualiza cada segundo, permite pausa/edición (R1:503–505, 559–571, 716–725; R8:3060–3098). | Campos inicializados al construir la vista, visibles al seleccionar o abrir scanner; no existe reloj que los avance cada segundo (N1:22–23, 138–159). | La hora por defecto puede quedar antigua durante una sesión larga. |
| Búsqueda y filtros | Debounce de búsqueda 650 ms, laboratorio/vista 180 ms; laboratorio canónico y reglas combinadas con cinco operadores, incluido modo de cierre y permanencia (R1:32–33, 75–115, 536–538, 581–586). | Buscar/Enter y Aplicar; laboratorio libre y filtros de columna. No ofrece el constructor equivalente con operadores ni modo de captura (N1:59–84, 286–298). | Semántica y accesibilidad distintas. Los filtros locales de React se aplican a las filas cargadas, por lo que tampoco equivalen a un filtro global del servidor. |
| Carga | Primera página de 1,000; solicita 1,001 para detectar continuidad y usa botón Ver 1,000 más. Actualiza estado por conjuntos y usa IDs estables. AG Grid mantiene virtualización de filas; desactiva explícitamente la de columnas (R3:10–12, 207–226; R1:859–892, 1764–1803). | Páginas de 500 automáticas mediante `ISupportIncrementalLoading`, con 500 llamadas `Add` a la colección por página (N1:175–176, N4). | Se introduce trabajo al llegar al final del scroll. Ambos acumulan los datos cargados; el tamaño de página no demuestra por sí solo mejor rendimiento. |
| Refresco | Modifica la fila afectada; cancela peticiones previas al cambiar consulta (R1:674–714, 1337–1367). | Sondeo cada 15 segundos. Si cambia la revisión y no hay selección/detalle, vuelve a cargar. `SetSource` limpia selección y lleva ambos ejes a cero (N1:40, 299–313; N2:79). | Cambios de otro usuario pueden interrumpir lectura y desplazamiento. |
| Jobs en cierres | Módulo separado con alerta del último pendiente, estados pendiente/visto/quitado, notas, responsable y fecha de seguimiento; bandeja actual/histórica y acción Quitar (R5:456–500, R6:106–170, 198–352, R7). | Pestaña calculada de JOBs completamente digitados; alerta nativa y reapertura al corregir, pero la tabla no ofrece seguimiento equivalente (N1:115–117, N6:165–181, N9:130–159). | No sustituye a la bandeja React. Ninguno de estos estados debe confundirse con cierre oficial de TIPS. |

## Reglas recuperadas y diferencias que requieren una decisión consciente

- Los patrones y exclusiones de modo manual/scanner están trasladados en N10 frente a R5:14–63 y 197–218. Una comparación automática de los literales confirmó los mismos 37 patrones y 9 exclusiones, sin diferencias de conjuntos. Esto no certifica todos los casos de normalización ni significa que el lector físico o el CHK tengan la misma interacción.
- Ambos tienen bloques persistentes con reutilización por el último actor dentro de 15 minutos. React calcula la ventana usando la fecha/hora operativa solicitada (R4:185–235, 709); .NET usa el momento real de guardado del servidor (N6:135–143). Al digitar con una hora pausada o histórica pueden asignar bloques diferentes.
- Prueba en .NET ya usa el nombre de planificación y consulta maestro/mapa; no hace falta volver a añadir una columna técnica. React conserva fallbacks técnicos cuando faltan los nombres (R1:161–168); el requisito explícito actual del usuario para .NET es mostrar solo planificación y señalar Sin equivalencia.
- .NET añade auditoría, UUID, versiones, idempotencia de digitación y revisión independiente de horarios. Deben mantenerse. Igualar la experiencia no exige perder esas garantías ni equiparar selección temporal, digitación, revisión y cierre.
- El método React `listar_activa` está en un módulo cuyo checkout solo contiene `router.pyc`. Se comprobó la delegación desde Report List, pero no se certificaron todos los filtros internos de ese método. Sin respuesta autenticada de ambos servidores no se puede explicar exhaustivamente la cantidad de filas del JOB de la captura.

## Rendimiento: hechos y puntos pendientes de medir

El nativo ya tiene virtualización de filas, reciclado de controles y un solo ScrollViewer para ambos ejes. La corrección no consiste en añadir virtualización como si no existiera.

1. **Trabajo durante el scroll, confirmado en código:** `ViewChanged` mueve el encabezado en el hilo de UI y dispara recálculo del CHK visible al variar el eje vertical (N5:47–57). Ese cálculo transforma las coordenadas de cada fila realizada; además se solicita al reciclar contenedores (N2:103–112, 197–205). Se debe medir su coste antes de atribuirle todos los tirones.
2. **Inserción de páginas, confirmada:** N4 usa una notificación por fila. Conviene desacoplar descarga y aplicación visual, conservar un ancla estable y evitar 500 notificaciones consecutivas; no usar un Reset indiscriminado que destruya el reciclado o la posición.
3. **Recargas completas, confirmadas:** N1 reemplaza la colección y N2 reinicia el scroll. Tras guardar, corregir, cambiar permanencia o detectar cambios externos, habría que aplicar cambios por UUID y conservar fila superior/offset cuando el filtro siga siendo el mismo.
4. **Trabajo de servidor, confirmado; latencia sin medir:** cada página, incluido el sondeo de una fila, relee todo el mapa/maestro como JSON, normaliza/agrupa, calcula huellas, envía arrays al CTE, cuenta resultados y consulta disponibilidad del scanner (N6:53–81, N7:11–34, N8:48–80). Debe separarse una consulta ligera de revisión de una consulta de datos y evitar rehacer el catálogo completo sin invalidación fiable. Un TTL aislado no basta para garantizar detección de cambios hechos desde React.
5. **No hay virtualización horizontal de celdas:** el nativo construye todas las columnas de cada fila realizada. React también la desactiva expresamente. Con diez columnas no es evidencia suficiente para justificar por sí sola reemplazar toda la tecnología.

La captura ya muestra una tabla tosca con seis filas: el diseño debe corregirse independientemente del volumen. Las pruebas anteriores con fixtures verificaban reciclado y contenido, no fluidez del cliente conectado ni tiempos de fotograma en este equipo.

## Orden recomendado de corrección

1. **Estado operativo e identidad.** Resolver cómo `planning.executions.id` se relaciona con `public.planificacion_report_check.job_prueba`, incluyendo varias ejecuciones para una misma prueba de planificación. Definir y probar propagación transaccional de CHK, permanencia, archivo/restauración y corrección; impedir divergencias entre aplicaciones. No colapsar UUID ni propagar un check a todas las ejecuciones por coincidencia de texto.
2. **Interacción de Report List.** Adoptar columnas/proporciones, bloques, Mantener en Muestreo visible, reloj y elegibilidad manual/scanner. Si se ofrece CHK inmediato, registrarlo como operación de digitación persistente con concurrencia y auditoría; la selección múltiple debe tener una representación propia. Conservar revisión de horarios y permisos.
3. **Continuidad y fluidez.** Cambios puntuales por UUID, misma vista después de guardar, conservar ancla de scroll, insertar páginas sin una ráfaga de notificaciones y sacar el trabajo prescindible del desplazamiento. Optimizar catálogo/sondeo sin perder invalidación por cambios externos.
4. **Bandeja de cierres y lector.** Completar seguimiento de alertas, actual/histórico y captura física equivalente, manteniendo separado el cierre oficial.

## Aceptación necesaria

- Comparar un JOB real en las dos aplicaciones: conjunto de filas, nombre de planificación, laboratorio, estado, permanencia y motivo de cada diferencia. Mantener las ejecuciones repetidas identificables.
- Guardar/corregir/mantener con datos de prueba y comprobar lectura coherente desde las dos aplicaciones; verificar archivo/restauración sin pérdida de observaciones ni historia.
- Probar modo scanner/manual, permisos, dos usuarios simultáneos y reintento; bloquear antes del guardado las acciones no permitidas.
- Medir por separado tiempo de API, llegada/aplicación de páginas y fotogramas del scroll con los datos y tamaños de ventana usados por el usuario. Verificar rueda, barra arrastrada, horizontal, filtros, guardado y refresco externo sin saltos de posición.
- Confirmar reloj, bloques de 15 minutos y hora histórica; los casos no son equivalentes entre las implementaciones actuales.

En esta revisión no se modificó código de ejecución, configuración ni bases de datos. No se ejecutaron pruebas de compilación porque no hay cambios de código. No se declara resuelto el problema visual ni la integración operativa.
