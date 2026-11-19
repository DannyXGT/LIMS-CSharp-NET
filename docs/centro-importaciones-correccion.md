# Corrección del Centro de Importaciones — 19 de noviembre de 2026

Se corrigió el módulo existente de `Lims.sln`. El cliente de este repositorio es **WinUI 3**, no WPF. Las capturas de esta entrega proceden de ese ejecutable nativo, con su login y Design System; no se creó otra aplicación ni un prototipo HTML.

Antes de modificarlo se inspeccionaron las bandejas, consultas PostgreSQL, decisiones, expedientes, tabla de celdas, importación, revisión previa, resumen e historial. También se revisaron las capturas existentes `02-estandares-preservados.png`, `03-preparaciones-preservadas.png`, `02-revision-emergencias.png` y `07-resumen-importacion.png`. Se conservaron los cambios que ya estaban presentes en el árbol de trabajo.

## Cambios

- Siete pestañas horizontales compactas con desplazamiento, total y pendientes. El desglose de la categoría seleccionada muestra pendientes, retenidos, resueltos y excluidos. Se reutilizan los contadores de PostgreSQL, independientes de la página y de la situación mostrada. Las categorías se superponen; no se suman como casos únicos.
- Alcance por lote o histórico, buscador, situación, filtros desplegables, configuración de columnas y un único Exportar. Se retiraron los botones independientes de copia, copia de JOBs, cambio de modo de selección y la navegación duplicada del encabezado.
- Una sola tabla virtualizada con selección rectangular, Ctrl+C, Ctrl+A sobre la página cargada, Shift+flechas, Esc y Enter. Las fuentes son de solo lectura; Ctrl+V en esta tabla informa cómo abrir una corrección WIP autorizada. Los formularios y el backend existentes conservan la validación, procedencia y versiones.
- Columnas principales JOB, ingreso, vencimiento, Estado TIPS, Buyer, Vendor, Servicio, USD, Situación y Motivo. Las demás se eligen desde Columnas. Se pueden redimensionar mediante el borde y reorganizar desde el menú del encabezado; JOB permanece fijo. Se mantienen orden, filtros por columna, tooltips y desplazamiento.
- Acciones en un menú contextual según categoría, situación, selección y permisos. Resolver Solo Summary requiere verificar pruebas manuales existentes. Las acciones deshabilitadas muestran su explicación. Retener, resolver, excluir y reabrir solicitan motivo y confirmación.
- Detalle lateral cerrable con los campos persistidos de cada archivo, lote, hoja y fila; diferencias entre campos homónimos, WIP, pruebas manuales e historial con actor y fecha. Indica expresamente las fuentes ausentes. El estado oficial TIPS se presenta por separado de la revisión operativa. Cerrar el panel conserva filtros y selección.
- Agregar una prueba cuyo nombre ya está en el episodio exige confirmar que corresponde otra ejecución legítima. Se conserva el UUID independiente de repeticiones y reanálisis y la protección de concurrencia existente; no se usa JOB + prueba como identidad universal.
- Se conservan las ventanas independientes «Revisa antes de agregar» y «Resumen de importación», el catálogo, NO PROGRAMAR, decisiones auditadas, confirmación explícita e historial. El Exportar de categoría completa elimina los filtros adicionales de forma explícita y conserva el alcance elegido; la exportación filtrada conserva todos los criterios y todas las páginas.

## Archivos de esta corrección

| Archivo | Cambio |
|---|---|
| `Frontend/Lims.Desktop/Planning/PlanningImportReviews.cs` | Pestañas, tabla única, selección, contadores y acciones |
| `Frontend/Lims.Desktop/Planning/PlanningReviewDetail.cs` | Detalle, fuentes, historial y paneles de filtros/columnas |
| `Frontend/Lims.Desktop/Planning/PlanningImportReviewTools.cs` | Adaptador de celdas, alcance y exportación |
| `Frontend/Lims.Desktop/Planning/PlanningImportsView.cs` | Retiro de navegación duplicada e integración del módulo |
| `Frontend/Lims.Desktop/Planning/PlanningSheetGrid.cs` | Atajos, modificadores, color de situación y configuración opcional de columnas |
| `Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs` | Seleccionar la página completa |
| `Frontend/Lims.Desktop/Planning/PlanningManualTestWindow.cs` | Confirmación de una ejecución repetida |
| `Frontend/Lims.Desktop/Planning/PlanningImportWindows.cs` | Revisión previa sin botón redundante de copia; atajos conservados |
| `Tests/Frontend/Lims.Desktop.Tests/PlanningSheetTests.cs` | Regresión de Ctrl+A, matriz copiada y protección de fuentes |
| `scripts/validate-planning-review-correction.ps1` | Recorridos del ejecutable real mediante UI Automation |
| `scripts/verify-planning-review-correction.ps1` | Integridad PostgreSQL e identidad de la compilación validada |

Esta corrección no agrega migraciones ni cambia las reglas de clasificación del backend. Los archivos de backend que ya aparecían modificados pertenecían al trabajo previo.

## Validación

Destino autorizado: API `https://localhost:7441`, PostgreSQL `127.0.0.1:55441/lims_planning_dev`. Las pruebas de infraestructura crean bases desechables `lims_planning_ci_*`; no escriben en producción ni en las fuentes TIPS. Se leen los XLSX originales sin modificarlos.

- `dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1`: compilación Debug correcta, sin advertencias ni errores.
- `scripts/test-planning.ps1`: 12 pruebas de aplicación y 22 de PostgreSQL aprobadas. Incluyen resolución conservada en cargas posteriores, identidad manual, concurrencia, decisiones con autor/motivo, equivalencias y exportación fuera de la primera página.
- Pruebas `PlanningSheetTests` y `PlanningMultipleLookupTests`: 14 aprobadas, incluida la nueva regresión de selección de 100 filas y protección de las columnas de fuente.
- `PlanningAuthorizationTests`: 20 aprobadas; los permisos siguen siendo exigidos en la API.
- `scripts/validate-planning-definitive-api.ps1`: nueve comprobaciones autenticadas satisfactorias, con alcance por lote, histórico, resueltos, catálogo, conservación manual y exportación completa de 14,722 casos.

Son **68 casos automatizados distintos**, además de las nueve comprobaciones de API. No se cuentan ejecuciones repetidas como pruebas nuevas.

La evidencia de los recorridos nativos y sus capturas está en `artifacts/validation/centro-importaciones-correccion-2026-11-19`. Cada resultado JSON registra el hash del ensamblado realmente ejecutado; `final.json` comprueba su correspondencia con la compilación y la conservación de las identidades anteriores.

El CSV guardado por el cliente real en `Downloads/Consulta de Jobs.csv` contiene 14,722 filas y tiene SHA-256 `D3FD9D7849AF063A69F69187ED9F5551A54E794C425D1D76481EBA05623CD0B1`, idéntico al resultado completo de la API. El selector Guardar se completó por separado; se verificaron automáticamente la fecha nueva del archivo, sus filas, hash y el mensaje de éxito del cliente. El CSV con todos los registros permanece local y no se incluye en el paquete de capturas.

Los once recorridos nativos aprobados son: distribución sin controles redundantes; teclado; detalle y expediente; cero pendientes con un resuelto; filtros y paginación; tamaños 1280/1366/1920; configuración, orden y ancho de columnas; decisiones y apertura del formulario manual; historial; exportación guardada; y ventanas previa/posterior de importación. Las capturas son del cliente real en ejecución.

El lote `48d898e9-5770-429c-86fe-7f85f7acbbc2`, corte Mediodía del 19 de noviembre, se confirmó desde la ventana nativa con los archivos originales. Produjo cero JOBs y ejecuciones nuevos, porque ya estaban incorporados. La comprobación posterior conserva los **15,685 episodios**, **10,987 ejecuciones**, todos sus UUID previos, la única ejecución manual y su resolución Solo Summary. Se conserva la aceptación explícita de 119 nombres pendientes de equivalencia; no se inventaron clasificaciones.

El ensamblado ejecutado y la compilación final tienen SHA-256 `51E3FF6A2B1F64D4D355800F6720A41B56BCEF052B9EC567D530BA7982897353`. Las pruebas de frontend usan propiedades de inicialización diferentes; después de ejecutarlas se restauró la compilación normal y se comprobó nuevamente la igualdad con el ejecutable validado.

## Límites

La evidencia es local y aislada. No acredita despliegue productivo, LAN, 50 usuarios concurrentes ni rendimiento en 8 GB con HDD. La exportación reutiliza CSV UTF-8 y el límite explícito de 50,000 casos del servicio, que rechaza el exceso sin truncar silenciosamente. La cobertura temporal de Report List permanece no verificable.

Las pruebas de persistencia y de reconciliación manual incluyen escrituras en bases CI aisladas. El recorrido nativo abre el formulario actual de asignación y verifica la conservación de la prueba manual ya existente; no presenta esa prueba anterior como una nueva alta efectuada en esta entrega.
