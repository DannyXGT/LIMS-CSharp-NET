# Consulta de Jobs aprobada

Referencia íntegra: [Consulta_Jobs_Prototipo_Interactivo.html](ui-reference/Consulta_Jobs_Prototipo_Interactivo.html), localizada en Descargas y copiada sin modificar. Se leyeron todas sus reglas CSS y eventos JavaScript antes de implementar. El producto sigue siendo WinUI 3 nativo; no incorpora un navegador ni otro frontend.

## Componentes

| Archivo | Responsabilidad |
| --- | --- |
| `Frontend/Lims.Desktop/Planning/PlanningJobsView.cs` | Pantalla compacta, búsqueda, filtros, cuatro indicadores del prototipo, revisión de pegado, editor, exportación y Solo tabla. |
| `Frontend/Lims.Desktop/Planning/PlanningSheetGrid.cs` | Filas nativas virtualizadas, celdas, selección rectangular, ratón, teclado, desplazamiento automático, encabezado y JOB congelados. |
| `Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs` | Rango, copia TSV, lectura de matrices Excel, validación y actualización de las filas guardadas. |
| `Frontend/Lims.Desktop/Planning/PlanningDossierWindow.cs` y `PlanningJobStatus.cs` | Ventana no modal Estado del JOB: Resumen y estado, Pruebas, WIP, Historial y copia para Teams. Conserva las acciones WIP autorizadas existentes. |
| `Frontend/Lims.Desktop/Views/ShellPage.Workspaces.cs`, `Planning/PlanningWorkspacePage.cs` y `Planning/WorkspaceNavigationService.cs` | Conexión al AppShell real y navegación compacta. Las pantallas originales del shell, Estándares y Preparaciones permanecen en sus archivos originales. |
| `Shared/Lims.Contracts/Planning/PlanningContracts.cs`, `Backend/Lims.Application/Planning/PlanningModels.cs` | Contratos de consulta y edición autorizada de notas. |
| `Backend/Lims.Infrastructure/Planning/PlanningNotes.cs`, `PlanningNotesSchema.cs` y `PostgresPlanningQueries.cs` | Escritura atómica, versiones de notas, auditoría y filtros/ordenamiento/conteos sobre todos los resultados en PostgreSQL. |
| `Backend/Lims.Api/Planning/PlanningEndpoints.cs`, `Frontend/Lims.Desktop/Planning/PlanningApiClient.cs` | Ruta `/api/planning/notes` y cliente autenticado existente. |
| `Backend/Lims.Infrastructure/Persistence/Migrations/20261116220000_AddPlanningExecutionNotes.cs` | Tres columnas nuevas por ejecución: `responsible`, `observation`, `note_version`. No sustituye valores anteriores ni elimina registros. |

No se rediseñaron Importaciones, Equivalencias, Accesos, el selector, Estándares ni Preparaciones. Las vistas de Planificación existentes se abren desde el menú del mismo shell, conservando sus implementaciones.

## Comparación con el HTML

Se trasladaron el tema grafito/azul, sidebar compacto, encabezado, barra de búsqueda, cuatro indicadores discretos, acciones de copia/pegado/exportación, tabla dominante, filas alternadas, rango azul, celda activa diferenciada, estados por color, pie de selección y Solo tabla. El tamaño físico sigue la escala de Windows del host; no se cambia el DPI del usuario.

El menú del prototipo contenía accesos demostrativos a módulos inexistentes. La versión nativa utiliza exclusivamente el AppShell y los módulos reales autorizados. No se añadieron Programación ni Digitación. Equivalencia y tipo de ejecución siguen disponibles en Columnas. Los UUID están en procedencia técnica del Expediente; no dominan la tabla de pruebas.

Las interacciones ahora tienen persistencia real:

- Clic, arrastre, Shift + clic y Shift + flechas seleccionan rectángulos de celdas. Flechas desplazan la celda activa; Ctrl+Espacio selecciona la columna de la página cargada. El arrastre desplaza la vista al alcanzar el borde.
- Ctrl+C, Copiar selección y menú contextual copian TSV sin etiquetas ni HTML. Fechas ISO; hora de digitación en UTC−06:00. Comillas, tabulaciones y saltos de línea de Excel se tratan como valores de celdas.
- Ctrl+V y el botón de pegado muestran celda inicial, valores anteriores/nuevos y cantidades válidas, inválidas y omitidas. El usuario confirma los cambios válidos juntos o cancela. Máximo 2.000 celdas y 4 MiB de texto por operación; lo que sale de la página cargada se identifica como omitido.
- Solo Responsable y Observación de una ejecución existente son editables. Máximos de 150 y 2.000 caracteres. El doble clic abre el editor; Enter guarda, Escape cancela y Tab guarda y continúa en la siguiente celda editable. Los datos importados y la digitación están protegidos.
- La API exige `planning.view` y `planning.wip.edit`, usando la sesión y comprobación de permisos actuales. Cada ejecución tiene una versión independiente de notas; un conflicto revierte el lote completo. Registra usuario, fecha, motivo, ejecución, lote de cambios y valores antes/después. No altera las fechas oficiales, las pruebas ni sus marcados.
- La búsqueda admite exacto, contiene, prefijo y listas separadas por coma, punto y coma, salto de línea o tabulación. La prueba busca nombre original o equivalencia. Fechas de ingreso/vencimiento, laboratorio, servicio, incidencias, digitación y vencidos se filtran en servidor; las páginas son de 100 filas. No se descarga el universo para filtrar.
- El Expediente se abre desde el botón, Enter, menú contextual o doble clic en JOB. Cerrar la ventana conserva filtros, selección y posición de Consulta. El resumen diferencia pendientes conocidos de ausencia de información; no deduce cierre oficial a partir de digitación. Teams recibe texto y el estado oficial se indica como No verificado cuando no hay fuente integrada.

## Datos y entorno de aceptación

Todas las escrituras se realizan en **127.0.0.1:55441 / lims_planning_dev**, la copia independiente autorizada en la integración anterior. No se modifican InterDB productiva, Supabase, respaldos originales ni `planificacion_migracion_test`. Los 770 episodios y 8.445 ejecuciones de esta base proceden de los dos XLSX TIPS reales del 13 de noviembre de 2026. El JOB de aceptación es **GUAT25038091-REV3**, con 24 ejecuciones y WIP fuente de CARHARTT / Apparel Links S.A.

Las diez observaciones de aceptación se identifican expresamente con `VALIDACION LOCAL ... no operativo`. Son escrituras de prueba en la copia local sobre ejecuciones de archivos reales; no son decisiones operativas ni usuarios ficticios presentados como datos reales. Los campos protegidos se comparan antes y después mediante relectura autenticada de la API.

La migración nueva se aplicó únicamente en esa base. La identidad `Lims.Planning.Validation` utiliza una copia de la salida compilada para separar sesión y credenciales de la aplicación habitual; sigue siendo el mismo código del host.

## Reproducir la aceptación

Con el entorno local ya preparado según [planificacion-integracion.md](planificacion-integracion.md):

```powershell
dotnet build Lims.sln --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
.\scripts\migrate-planning-development.ps1
.\scripts\start-planning-development.ps1 -Desktop
.\scripts\validate-planning-desktop.ps1 -Action LoginAndNavigate
.\scripts\validate-consulta-jobs.ps1 -Step All
```

El último guion interactúa con el cliente WinUI real mediante UI Automation y entrada nativa de Windows, comprueba el portapapeles y relee las escrituras desde la API local. El arrastre usa eventos de entrada de ratón; mover solamente el cursor con `SetCursorPos` no acredita un arrastre en WinUI. Requiere escritorio interactivo disponible. Los tests automatizados usan bases temporales del mismo clúster, identificadas como datos sintéticos de prueba.

Las evidencias se guardan en `artifacts/planificacion-dev/consulta-evidence/`: `01-consulta-real.png`, `02-seleccion-5x4.png`, revisión/resultado del pegado, las cuatro pestañas del Expediente, `09-solo-tabla.png`, `seleccion-5x4.tsv`, `pegado-persistido.json`, `estado-para-teams.txt` y el registro de comprobaciones `acceptance-All.json`. Los TRX están en `artifacts/planificacion-dev/test-results/consulta-*.trx`. No contienen las credenciales locales de los archivos de configuración.

## Límites de validación

Resultado del 16/11/2026: solución x64 y cliente final compilados sin errores ni advertencias; **501 tests aprobados, 0 fallidos, 88 omitidos**. De los omitidos, 87 requieren el entorno propio de módulos anteriores y uno corresponde a la aceptación de importaciones TIPS, ejecutada en la entrega anterior. Incluye 10 pruebas PostgreSQL de Planificación, 8 de selección/TSV y la autorización de la nueva ruta.

La pasada `-Step All` aprobó 19 comprobaciones agrupadas que cubren los 20 escenarios obligatorios: apertura e identidad del host, JOB real, selección/copia de celda, rango 5×4 exacto, columna cargada, arrastre/Shift, pegado de 10 valores, confirmación/relectura persistida, protección de datos fuente, Escape/Enter, búsqueda múltiple, Expediente/pestañas/Teams, conservación de selección, Solo tabla, filtros/páginas y ausencia de checks. Una pasada adicional `-Step Edit` verificó **Tab**, guardado y continuación a otra celda editable. Registros completos en `acceptance-All.json` y `acceptance-Edit.json`.

La selección y copia operan sobre las filas cargadas; el filtro, ordenamiento, conteos y CSV operan sobre la consulta completa en el servidor. CSV conserva el límite existente de 50.000 filas. El Expediente conserva los límites existentes de 1.000 eventos y 2.000 filas fuente; no borra historia adicional.

La base histórica no acredita el estado operativo actual del laboratorio ni el cierre oficial TIPS. El Report List observado no informa la digitación de estas ejecuciones; se muestra Sin información. No se inventaron pendientes, usuarios ni fechas para llenar estados.

La aceptación local no certifica LAN con más de 50 usuarios, equipos de 8 GB/HDD ni Windows 10 físico. La implementación mantiene paginación, virtualización de filas, consultas asíncronas cancelables, actualización de las filas modificadas y protección transaccional de concurrencia. Las mediciones sobre ese hardware y la validación con operadores/productivo quedan pendientes.
