# Cierres operativos

Implementación en el cliente WinUI y la API existentes. Corrección del 25 de noviembre de 2026 según la captura de Consulta de Jobs y la indicación de eliminar Prueba2.

## Uso

Acceso único desde **Cierres en el sidebar de Planificación**. Se eliminaron el botón de Consulta de Jobs y la pestaña de Report List / Digitación. La página conserva la misma API, permisos, persistencia y expedientes.

1. **Agrupado** muestra bandas por fecha y minuto de la última prueba completa, con JOB, Buyer, Prueba, USD y Service. **Prueba2 se eliminó**. **Tabla** muestra los mismos registros e incluye fecha y hora en columnas.
2. **Fecha** ofrece selección múltiple por año, mes y día; **Hora** ofrece las horas de las fechas elegidas. Se mantienen Todas/Ninguna. Los filtros, búsqueda, conteos y exportación se aplican en servidor. **Más filtros** conserva estado de seguimiento, laboratorio, rango y orden. La vista inicial incluye todos los cierres vigentes, también los atendidos; Por atender e Historial son alcances adicionales. Los grupos que continúan en otra página indican cuántos cierres se muestran respecto al total.
3. Doble clic o Enter sobre un JOB abre sus pruebas actuales, nombres originales, digitadores y revisión de horarios. Las ejecuciones repetidas siguen siendo filas independientes. Se puede abrir cada expediente si el JOB tiene varios episodios, copiar un resumen y leer el historial.
4. **Registrar seguimiento** guarda estado «Por atender», «En seguimiento» o «Atendido», responsable, próxima fecha, notas y motivo. El motivo es obligatorio. «Atendido» retira ese cierre del alcance **Por atender**; sigue disponible en **Todos los cierres** e **Historial de seguimiento**. Las notas pueden consultarse con permiso de lectura; guardar requiere el permiso existente de revisión de Report List.
5. **Exportar** genera CSV UTF-8 por JOB o por prueba vigente, con todas las filas del filtro, incluso fuera de la página visible. La exportación por prueba conserva UUID y nombre original. En el historial se exportan los resúmenes guardados; el detalle indica que sus pruebas son las actuales. Una exportación se rechaza si la revisión cambió desde la consulta.

## Seleccionar y copiar celdas

La tabla de Cierres es de solo lectura. Arrastrar selecciona un rango rectangular; Shift + clic o Shift + flechas amplía la selección, y las flechas mueven la celda activa. Ctrl+C, el botón Copiar selección o el menú contextual copian las celdas en dos formatos simultáneos: TSV para Excel y una tabla HTML con bordes para editores con formato, como Teams. El menú también ofrece copiar con encabezados en ambos formatos. En Teams se debe pegar conservando formato (Ctrl+V), no como texto sin formato. La compatibilidad final depende del editor y de la versión de Teams; no se envían mensajes como parte de la validación. Ctrl+A selecciona los registros de la página cargada, y pulsar un encabezado selecciona su columna en esa página. Escape limpia la selección. Doble clic o Enter abre el detalle del JOB.

Las bandas de fecha y hora no se copian como filas. El HTML escapa el contenido de las celdas y conserva saltos de línea, sin interpretar nombres o valores como etiquetas. Las celdas mantienen su orden incluso al arrastrar en sentido inverso o cruzar grupos; tabulaciones, comillas y saltos de línea se escapan en el TSV. Cambiar de página, filtros o presentación limpia la selección. Un aviso de cambios remotos conserva el rango hasta actualizar manualmente. Pegar, F2 y Suprimir no modifican datos.

Validación de entradas nativas: `scripts/validate-closures-selection.ps1`, sobre el cliente aislado iniciado por `scripts/validate-closures-desktop.ps1`. Evidencia de Windows: `artifacts/validation/closures-desktop/selection-native.json`; no demuestra operación en servidor o producción. El recorrido nativo aprobó arrastre 3×3, arrastre inverso, Shift izquierdo y derecho 4×4, Ctrl+A 13×5, Ctrl+C con texto y tabla HTML al portapapeles de Windows (incluyendo offsets UTF-8 del formato nativo), ausencia de cambios con pegar/F2/Suprimir y apertura del detalle con doble clic. Captura de la selección: `artifacts/validation/closures-desktop/cierres-seleccion-celdas.png`.
## Reglas y mejoras respecto a los HTML

Se revisaron `Desktop/programas/App_web/Digitacion_23_OCT.html`, secciones `computeCierres`, `renderCierres` y exportación, y `Job_Consulta_V_final.html`, secciones de cierres completos, agrupación y seguimiento. También se consultó la bandeja existente de React.

- Se conserva la regla de todas las pruebas con fecha y hora. La fecha y hora de última digitación provienen de un mismo timestamp; no se combinan la fecha máxima de una prueba y la hora máxima de otra.
- El conjunto requerido usa todas las ejecuciones persistidas del JOB y todos sus episodios. Ni una búsqueda, una selección, un filtro de laboratorio ni una página reducen ese conjunto para decidir si está completo.
- Se conservan exclusiones de `NO PROGRAMAR` y bloqueos por retención, incidencias abiertas, identidades sin conciliar y admisiones inválidas. Los JOBs con guion o REV continúan fuera de las listas operativas. El marcador explícito `Sin equivalencia` también bloquea el cierre.
- Digitación, revisión de horarios y seguimiento son independientes. Un JOB completamente digitado puede tener horarios pendientes de revisión; la cantidad se muestra expresamente. Atender el cierre no marca sus horarios como chequeados ni modifica pruebas.
- USD y Service proceden del WIP del episodio más reciente del JOB; el importe no se suma por ejecución. Si falta un dato, se presenta Sin información. Buyer conserva los valores de los episodios y sus expedientes siguen accesibles.
- El seguimiento queda asociado a una huella del conjunto de UUID, versiones y contenido operativo de las ejecuciones. Una corrección o cambio de ese conjunto invalida la vigencia del seguimiento anterior. Si el JOB vuelve a quedar completo, aparece por atender con una nueva huella, conservando el historial previo.
- El historial de seguimiento reúne cierres que recibieron una acción de seguimiento. No reconstruye cierres históricos inexistentes ni importa las tablas de cierres de los HTML o React.
- La escritura verifica de nuevo elegibilidad y versión bajo el bloqueo transaccional existente. Dos usuarios no pueden sobrescribir el mismo seguimiento sin detectar conflicto. Los reintentos de la misma solicitud son idempotentes.
- Un fallo de guardado conserva abierto el formulario con notas, estado, responsable y fecha. El refresco automático no reemplaza un detalle abierto ni un formulario en edición.
- La auditoría se registra en los expedientes de todos los episodios del JOB, con actor autenticado, fecha de servidor, motivo y estados anterior/posterior. El responsable de seguimiento es un texto operativo; no cambia permisos ni asigna automáticamente usuarios.
- Este módulo registra atención interna. El estado oficial importado permanece disponible en el expediente.

## Contratos y publicación

- `GET /api/planning/closures`: bandeja actual, historial y filtros.
- `GET /api/planning/closures/detail?job=…&fingerprint=…`: pruebas actuales e historial.
- `GET /api/planning/closures/export`: mismo filtro, CSV completo; `IncludeTests=true` para pruebas vigentes.
- `PUT /api/planning/closures/follow-up`: seguimiento con `RequestId`, huella y versión esperada.
- Se conserva `GET /api/planning/report-list/closures`, compartiendo la misma elegibilidad.

La migración `20261124150000_AddReportClosureFollowUp` agrega `planning.report_closure_followups` y `planning.report_closure_requests`. No reescribe digitaciones ni inventa cierres anteriores. Publicar requiere actualizar API y cliente, y aplicar la migración mediante el flujo establecido del servidor. Los cambios de esquema se probaron exclusivamente en bases efímeras locales; no se aplicaron en producción.

## Ajuste visual del 25 de noviembre

La página tiene un encabezado con el mismo lenguaje visual de Consulta de Jobs: icono azul, título destacado, descripción y acciones Actualizar/Exportar a la derecha. La bandeja usa una barra compacta con contador, selector Agrupado/Tabla, fecha, hora, filtros y búsqueda. En ventanas estrechas la búsqueda pasa a una segunda fila. Los grupos muestran fecha y hora en bandas verdes, con filas y encabezados de menor altura y las cinco columnas JOB, Buyer, Prueba, USD y Service.

El filtro de fecha reemplaza las tarjetas anidadas por una lista plana con sangrías, casillas pequeñas, selección por año/mes/día y acciones Todas, Ninguna y Aplicar. La validación nativa comprueba que la selección del año se propaga al día y que el desplegable no supera 260 unidades de ancho. Captura: `artifacts/validation/closures-desktop/cierres-calendario-compacto.png`, con 13 registros ficticios para revisar varios grupos. Esta captura no contiene datos de producción.
## Validación

- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-closures.ps1`: diez casos PostgreSQL aislados aprobados, incluyendo pruebas repetidas, revisión independiente, persistencia, auditoría en Consulta, corrección y redigitación, historial, retenciones, incidencias, identidades sin conciliar, filtros, exportación completa, paginación, idempotencia y concurrencia. Evidencia: `artifacts/validation/closures/tests/closures-postgres.trx`.
- 37 casos de autorización aprobados, incluyendo las nuevas rutas. Evidencia: `artifacts/validation/closures/tests/closures-authorization.trx`.
- `scripts/validate-closures-desktop.ps1` ejecuta el cliente WinUI con transporte HTTP en memoria: detalle con dos PH independientes, expediente, conservación del formulario ante fallo, reintento con la misma identidad, permisos de lectura y revisión de controles en 1366×768 y 1280×800. Este recorrido es una validación con fixtures, independiente de PostgreSQL y del servidor autenticado.
- API y cliente x64 Debug compilados con cero errores y advertencias. El recorrido visual genera `artifacts/validation/closures-desktop/closures.json`, `consulta-sin-acceso-cierres.png` y `cierres-modulo-independiente.png`. Windows rechazó inicialmente el registro AppX desde el sandbox con `0x80073D19`; el paquete aislado se inició fuera del sandbox. No se modificó el registro del cliente de producción.
- Recorrido final aprobado: `SidebarOnly`, `ConsultationEntryRemoved`, `ReportListEntryRemoved`, `ModuleHeading`, `StandaloneModule` y `NoSecondTestColumn` en `true`, junto con preservación de pruebas repetidas, notas ante fallo, reintento idempotente y permisos de lectura. Se comprueban las dos resoluciones y el módulo conserva el seguimiento guardado.

Los resultados locales no certifican publicación, operación por LAN ni aceptación de producción.
