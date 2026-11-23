> Actualización de usabilidad: la descripción vigente de Digitación, columnas predeterminadas y selección CHK está en [report-list-usabilidad.md](report-list-usabilidad.md). Los apartados siguientes conservan evidencia anterior.

# Report List / Digitación por horarios

## Corrección prioritaria de la tabla y los horarios

20 de noviembre de 2026. Evidencia nueva en [artifacts/report-list-correction](../artifacts/report-list-correction). El apartado posterior conserva la evidencia de la implementación anterior y sus límites; los cambios de este apartado prevalecen sobre su descripción visual.

Se recuperaron las once columnas en el orden solicitado: Fecha de ingreso, Due Date, JOB, Prueba, Laboratorio, Cliente, Hora de digitación, Fecha de prueba, Estado de digitación, Digitado por y Selección. UUID, prueba original, tipo, bloque histórico, permanencia y servicio siguen disponibles como columnas opcionales. Fecha de ingreso y Due Date no se pueden ocultar accidentalmente. La tabla ocupa la altura restante y conserva carga incremental y virtualización nativa.

Orden y ancho se cambian desde el menú contextual de cada cabecera. Las preferencias se guardan por ID de usuario en `LocalAppData/Lims/ReportList/columns-{id}.json`; no son preferencias globales compartidas entre usuarios. La selección de celdas es independiente del marcado operativo. Se corrigió la interferencia del teclado nativo y se comprobó Ctrl+C con un rango de **2 filas × 3 columnas**, fechas reales y saltos/tabuladores correctos en el portapapeles.

Chequeo de horarios ahora agrupa por fecha y minuto operativo en el servidor. Una hora puede contener varias confirmaciones sin perder sus UUID, miembros, revisores previos ni auditoría. El chequeo valida todas las sesiones y versiones en una transacción: una nueva confirmación o una ejecución cambiada invalida la solicitud abierta. Los endpoints conservan el comportamiento por sesión para clientes anteriores; el cliente actual solicita `GroupByClock=true`.

Se verificaron **104 pruebas sin fallos ni omisiones**: 41 PostgreSQL en bases efímeras, 34 de autorización/conexión API y 29 de cliente/modelos. Los TRX están en `artifacts/report-list-correction/tests`. La aceptación real se documenta aparte:

| Criterio | Evidencia de esta corrección |
|---|---|
| Tabla con ambas fechas | [Captura del cliente](../artifacts/report-list-correction/ui/01-report-list-fechas.png) |
| Pruebas repetidas y estados distintos | [Dos PH de GUAT26022934](../artifacts/report-list-correction/ui/02-ph-repetidas.png); una digitada y otra «Sin información», sin modificar sus datos históricos. |
| Selección de celdas y Ctrl+C | [Captura](../artifacts/report-list-correction/ui/03-celdas-ctrl-c.png) y `cells.json`. |
| Selección múltiple y fecha/hora | [Selección](../artifacts/report-list-correction/ui/05-seleccion-multiple-fecha-hora.png), [confirmación](../artifacts/report-list-correction/ui/06-confirmacion.png) y [guardado](../artifacts/report-list-correction/ui/07-digitacion-guardada.png). |
| Horarios, detalle y chequeo | [Lista](../artifacts/report-list-correction/ui/08-chequeo-horarios.png), [detalle](../artifacts/report-list-correction/ui/09-detalle-horario.png), [chequeado](../artifacts/report-list-correction/ui/10-horario-chequeado.png). |
| Persistencia / Consulta / exportación | `persistence.json` y `chequeados.csv`: dos ejecuciones reales guardadas desde WinUI, a las 11:01 del 20 de noviembre de 2026, chequeadas y releídas mediante API autenticada. La exportación contiene 5 ejecuciones chequeadas, incluidas las dos nuevas. |
| Referencias visuales | [Estándares](../artifacts/report-list-correction/ui/11-estandares-nativos.png) y [Preparaciones](../artifacts/report-list-correction/ui/12-preparaciones-nativas.png), abiertas en el mismo host y tema. Están vacías en la restauración usada; prueban composición y recursos visuales, no datos operativos de esos módulos. |
| Columnas por usuario | [Orden y ancho guardados](../artifacts/report-list-correction/ui/04-preferencias-columnas.png), `user-layout.json` y `native-Layout.json`. Ancho de JOB cambiado a 200 px mediante diálogo de cabecera; al finalizar se restauraron las once columnas predeterminadas. |
| Varias confirmaciones a la misma hora | [Horario agrupado](../artifacts/report-list-correction/ui/13-horario-varias-confirmaciones.png) y `combined-clock.json`: dos sesiones, tres miembros independientes, un horario. |
| Filtros y volumen nativos | [Filtros de fechas/estado](../artifacts/report-list-correction/ui/14-filtros-fechas-estado.png); [3,000 registros cargados, 16 filas visibles](../artifacts/report-list-correction/ui/15-volumen-virtualizado.png). Prueba local de virtualización, sin extrapolar rendimiento a LAN o hardware mínimo. |

La lectura posterior al reinicio quedó comprobada también directamente en PostgreSQL: [SQL de solo lectura](../artifacts/report-list-correction/persistence-read-only.sql) y [resultado](../artifacts/report-list-correction/postgres-reread.json). La consulta selecciona las versiones activas, manteniendo separadas las membresías históricas anteriores. La comparación completa de los registros del JOB original contra `baseline.json` no encontró cambios.

La base utilizada sigue siendo exclusivamente `127.0.0.1:55441/lims_planning_dev`, restaurada localmente. No se desplegó ni se modificó producción. Las capturas mencionadas en el texto adjunto no estaban adjuntas; sí se inspeccionó directamente `Desktop/programas/App_web/Digitacion_23_OCT.html`. La aceptación de instalación productiva, hardware/escáner físico y LAN sigue fuera de esta validación local.

---

## Evidencia anterior conservada

20 de noviembre de 2026 · Cliente nativo WinUI 3 del LIMS existente.

Implementado dentro de Planificación, con su autenticación y recursos visuales actuales. La validación usa **exclusivamente la copia restaurada `127.0.0.1:55441/lims_planning_dev`**. No se desplegó ni se migró producción. Los cambios locales previos en `Program.cs`, `appsettings.json` y los archivos ajenos a esta tarea se conservaron.

## Comportamiento entregado

- Tres vistas en la misma página: Digitación, Chequeo de horarios y Cierres. Se reutilizan `LimsTabStrip`, estilos de Estándares/Preparaciones, superficies y `Motion`, con accesibilidad del sistema.
- Búsqueda por JOB/prueba/cliente en servidor, búsqueda múltiple exacta de hasta 500 JOBs, Pendientes/Digitadas/Todas, filtros desplegables, columnas configurables y modo Solo tabla.
- Selección temporal por ejecución. La fecha y hora de Guatemala se editan junto a la selección; una confirmación muestra las pruebas originales y sus identidades. No se persiste la selección como CHK.
- Digitación transaccional con UUID de sesión, versiones, actor autenticado, hora operativa y hora de guardado independientes. Se mantienen las pruebas repetidas; dos sesiones con idéntica hora no se fusionan. Reintentar la misma solicitud no duplica datos.
- Chequeo posterior independiente, con revisor, fecha y auditoría por episodio. El detalle muestra todas las ejecuciones, identidad, prueba original, equivalencia, laboratorio y estado. Incluye excepciones, historial y exportación CSV de chequeados.
- La revisión exige el conjunto completo y las versiones que se abrieron. Corregir una ejecución invalida el chequeo del grupo completo; conserva la revisión anterior como historia. Los grupos inválidos no se exportan como chequeados.
- Consulta y expediente leen las mismas ejecuciones, muestran digitación y chequeo por separado y conservan el historial. Las columnas nuevas de Consulta son configurables para preservar su disposición inicial.
- Cierres evalúa todos los episodios persistidos del JOB, no solamente el resultado filtrado. Excluye retenciones, pendientes, equivalencias faltantes, incidencias y filas fuente sin conciliar. La vista identifica el límite de cobertura disponible y no modifica el cierre oficial de TIPS.
- Tabla principal con virtualización nativa de filas, creación de celdas visibles horizontalmente, carga incremental y orden estable por identificador. Consultas obsoletas se cancelan; sincronización cada 15 segundos y conflictos de revisión evitan sobrescrituras silenciosas. Una selección o revisión abierta se conserva frente a cambios de otro usuario.
- Captura AR conserva las reglas y vínculos de tracking existentes. El encabezado no afirma que el hardware esté conectado; su estado está en un tooltip.

## Capturas auténticas

Son capturas de la ventana abierta y autenticada, no maquetas ni datos de ejemplo:

1. [Report List](../artifacts/report-schedules/ui/01-report-list.png).
2. [Selección con fecha y hora](../artifacts/report-schedules/ui/02-seleccion-fecha-hora.png).
3. [Confirmación cancelable](../artifacts/report-schedules/ui/03-confirmacion.png).
4. [Chequeo de horarios](../artifacts/report-schedules/ui/05-chequeo-horarios.png).
5. [Detalle de un horario](../artifacts/report-schedules/ui/06-detalle-horario.png).
6. [Historial](../artifacts/report-schedules/ui/08-historial.png).
7. [Consulta con identidad y chequeo](../artifacts/report-schedules/ui/10-consulta-chequeo.png).
8. [Cierres](../artifacts/report-schedules/ui/09-cierres.png).

La captura de selección final muestra la PH que sigue pendiente; la primera PH ya está digitada y chequeada. La confirmación de esa selección se canceló para conservar el escenario A.

## Escenarios A–F y evidencia

| Escenario | Resultado y límite | Evidencia |
|---|---|---|
| A. PH repetidas | `GUAT26022934` conserva dos PH: `25fdff34-301c-4593-933d-4461a385072a` digitada y `6f159138-56b1-417e-a0a6-0958e570e37b` pendiente. Se restauró únicamente el marcado de la validación anterior antes de repetir el caso, con corrección auditada. | `real-baseline.json`, `real-AfterRestart.json` |
| B. Fecha y hora | Desde WinUI se guardó una PH a `2026-11-20 14:54 -06:00`. Un segundo lote contiene dos ejecuciones reales en el mismo horario, con otra sesión. Reintentos no duplican; otra solicitud con versión obsoleta recibe 409. | `real-exercise-performance.json`, `postgres-AfterRestart.json` |
| C. Chequeo | Se chequeó desde WinUI; se reiniciaron API y cliente y se volvió a autenticar. El horario y el revisor siguen guardados. | `real-AfterRestart.json`, `native-Review.json`, `native-History.json` |
| D. Consulta | La misma ejecución y su chequeo aparecen en Consulta sin importar archivos. El expediente conserva digitación, revisiones y correcciones. | `real-AfterRestart.json`, `ui/10-consulta-chequeo.png` |
| E. Simultáneos | Dos usuarios autenticados intentaron digitar la misma ejecución real: 200 y 409. Otro usuario revisó al ganador; una corrección invalidó el grupo y una revisión obsoleta recibió 409. La cuenta adicional fue explícitamente de validación local y se desactivó al terminar. | `real-two-user-concurrency.json` |
| F. Históricos | Migración aditiva sin actualización, renombrado o conversión de históricos. Se contrastó el respaldo previo con la auditoría original: bloque 2 y hora `2026-11-20 08:39:12.252927 -06:00` siguen trazables. Ese marcado pertenecía a la validación anterior y luego se corrigió explícitamente para A. La preservación de un registro histórico sin sesión también se prueba en PostgreSQL efímero. | `real-historical-compatibility.json`, pruebas PostgreSQL |

Los JSON y CSV están en [artifacts/report-schedules](../artifacts/report-schedules). No contienen tokens ni contraseñas. El respaldo completo se conserva localmente y no se incluye en el paquete de entrega.

La exportación nativa produjo **3 pruebas chequeadas**, con sus revisores, y coincidió byte por byte con el CSV de la API (`native-checked-export.json`). El selector Guardar como se completó por separado; la comprobación del archivo guardado, su hash y el mensaje de éxito del cliente fue posterior. El intento inicial de automatizar ese selector no se cuenta como una prueba aprobada.

## Compilación, integración y rendimiento

Compilación de API y cliente x64: **0 errores y 0 advertencias**. Se verificó que la DLL del cliente abierto coincide con la compilada; el registro de aceptación contiene sus hashes.

**125 pruebas únicas aprobadas, 0 omitidas:**

| Grupo | Pruebas | Frontera |
|---|---:|---|
| Reglas de Planificación y Report List | 27 | Reglas / fixtures |
| PostgreSQL de Planificación | 38 | Bases efímeras aisladas con fixtures |
| Autorización y conexión API | 34 | API de prueba; permisos vigentes y protección de escrituras |
| Cliente de Planificación | 26 | Contratos, selección, continuación, cancelación y consultas |

Los archivos TRX finales están en `artifacts/report-schedules/tests`. La prueba de integración de filtros/exportación se repitió de forma dirigida después de añadir los campos de chequeo; no se suma dos veces.

Mediciones contra datos reales restaurados:

| Operación | Resultado |
|---|---|
| Lectura completa | 10,987 ejecuciones distintas en 22 páginas, sin duplicados |
| Páginas de 500 | Media 257 ms; máximo 388 ms en el recorrido registrado |
| 50 solicitudes simultáneas de lectura | 50/50 correctas; lote completo en 3.86 s. Una cuenta, no 50 usuarios distintos |
| Consulta de horarios, 8 muestras | Mediana 26.75 ms |
| Cierres, 8 muestras | Mediana 117.74 ms; 0 JOBs elegibles en los datos evaluados |
| Desplazamiento nativo | 2,000 ejecuciones cargadas y 16 filas visibles. Recorrido automatizado de 9.07 s, incluidas pausas e inspección UI Automation |
| Memoria observada del cliente | Aproximadamente 294 MiB privados / 407 MiB de trabajo en esa muestra |

Evidencia: `real-volume.json`, `real-exercise-performance.json`, `native-scroll-volume.json`. Son medidas de localhost en esta estación, sin atribuirlas a LAN, HDD o hardware mínimo.

## Persistencia y operación

Migración `20261120120000_AddReportScheduleReview`: nuevas tablas `report_sessions`, `report_session_members` y vista `report_schedule_status`. Los campos de `report_checks` y `report_blocks` conservan su significado previo. No se usa localStorage ni se asignan horas a bloques históricos.

Rutas aditivas:

- `POST /api/planning/report-list/digitize`
- `GET /api/planning/report-list/schedules`
- `GET /api/planning/report-list/schedules/{id}`
- `POST /api/planning/report-list/schedules/review`
- `GET /api/planning/report-list/schedules/export`
- `GET /api/planning/report-list/closures`

Se conservan los endpoints anteriores para compatibilidad. Digitación exige `planning.report.mark`; corrección y chequeo exigen `planning.report.review`, además de sesión válida y permisos vigentes. El servidor resuelve identidad y auditoría.

Los scripts `start-report-schedules-development.ps1` y `validate-report-schedules-*.ps1` restringen su destino a la restauración local. El lanzador espera a que HTTPS esté escuchando antes de abrir el cliente, para evitar que el lanzador anterior intente iniciar una segunda API.

## Pendientes y límites explícitos

- Validación en Windows 10 y en equipos reales de 8 GB RAM con HDD; LAN y carga de más de dos usuarios distintos. Las 50 lecturas concurrentes locales no sustituyen esa prueba.
- Conexión y aceptación con escáner físico. Solo se conserva el flujo AR con tracking validado; no se declara hardware conectado.
- Certificación del universo externo de ejecuciones TIPS. Cierres informa completitud entre los registros persistidos y bloquea las excepciones conocidas; no certifica ni realiza cierre oficial.
- Conciliación operativa de digitaciones históricas sin sesión con nuevos grupos de revisión. Permanecen consultables con su información y bloques originales; no se agruparon ni se declararon chequeadas automáticamente.
- Despliegue a la instalación habitual y aceptación del operador. La entrega ejecutada es el cliente aislado local; no se sustituyó la instalación de producción.

Estos límites impiden presentar esta evidencia como aceptación de producción, aunque los flujos implementados y los escenarios locales indicados se hayan ejecutado correctamente.
