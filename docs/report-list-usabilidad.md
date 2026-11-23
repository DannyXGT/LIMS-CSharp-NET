# Report List: corrección de usabilidad

## Ajuste posterior de la cabecera

Se sustituyó la franja de título, subtítulo y pestañas separadas por una superficie compacta: identidad de Report List y pestañas en la misma fila, búsqueda debajo y tabla inmediatamente después. El subtítulo redundante se retiró. Se reutilizan las superficies, iconos y pestañas nativas de LIMS; a menor ancho la composición pasa a varias filas, sin superponer controles.

Configuración ahora abre un menú pequeño; «Columnas» contiene el submenú de preferencias. Conserva mostrar/ocultar, orden y ancho. «Varios JOBs» abre la búsqueda múltiple existente. No cambian contratos, guardado, permisos ni datos.

Evidencia actual: [cabecera amplia](../artifacts/report-list-header/ui/01-cabecera-compacta.png), [selección contextual](../artifacts/report-list-header/ui/02-barra-contextual.png), [1366×768](../artifacts/report-list-header/ui/07-vista-1366x768.png) y [1024×768](../artifacts/report-list-header/ui/07-vista-1024x768.png). El chequeo geométrico sitúa la cabecera de la tabla a 133 píxeles del título en la vista amplia. Las dos PH de GUAT26022934 permanecieron idénticas antes y después de estas pruebas visuales; se abrió y canceló la confirmación, sin guardar digitaciones adicionales.

Compiladas ambas salidas Debug, `bin/x64/Debug` y `bin/Debug`, con cero errores y advertencias. Esta última corresponde al comando WinAppCLI original sin `Platform=x64`; ya no requiere elegir otra ruta para ver la cabecera actual. Para cargarla hay que cerrar el cliente anterior y volver a abrirlo. El entorno Production no se ejecutó durante esta validación.

Los registros nuevos están en `artifacts/report-list-header`. Las pruebas de persistencia y las 31 pruebas de modelos descritas a continuación pertenecen a la corrección anterior; se conservan como evidencia separada.

---

20 de noviembre de 2026. Esta descripción y las capturas de `artifacts/report-list-usability` sustituyen la descripción visual de entregas anteriores. El cambio se limita al encabezado y a la tabla del módulo existente; conserva el host WinUI, autenticación, permisos, servicios, sesiones y auditoría.

## Abrir la versión corregida

Ejecutar [Abrir-Report-List-Pruebas.cmd](../Abrir-Report-List-Pruebas.cmd) en este checkout. Compila el cliente x64 actual y abre el entorno de pruebas existente, restringido a la base restaurada `127.0.0.1:55441/lims_planning_dev`. Requiere la preparación local de Planificación ya presente; no distribuye credenciales ni un respaldo de la base.

Se encontró abierta una compilación anterior en `Frontend/Lims.Desktop/bin/Debug/.../AppX`, mientras la compilación actual se genera en `bin/x64/Debug/...`. El lanzador nuevo verifica el hash de la DLL antes de dar por preparado el cliente. Solo sustituye sus instancias de validación; conserva la instalación habitual y la configuración productiva.

## Operación de la tabla

- Una superficie de búsqueda, selector Pendientes/Digitadas/Todas y acciones compactas de filtros, actualización y configuración. Las pestañas siguen dentro de Report List.
- Predeterminadas: Fecha de ingreso, Due Date, JOB, Prueba de Planificación, Laboratorio, Cliente, Hora, Fecha de prueba, Digitado por y CHK. Las nueve columnas obligatorias están protegidas frente a ocultación. Cliente es opcional.
- Estado de digitación continúa en los datos y el expediente. Se puede mostrar expresamente desde configuración; las preferencias antiguas se migran para quitarlo del predeterminado conservando orden y anchos.
- CHK vacío indica ausencia de marcado; el tooltip conserva el estado real de origen. Una selección temporal muestra check y fondo de selección. Un marcado persistido muestra check bloqueado, candado y fecha/hora/autor guardados. Los datos parciales tienen check indeterminado, sin atribuirles una digitación completa.
- La cabecera CHK marca o desmarca únicamente pendientes elegibles completamente visibles. Shift + clic extiende el marcado entre filas visibles. No existe «Seleccionar cargadas». Si se desplaza la tabla y quedan marcadas filas fuera de vista, guardar exige una casilla explícita que indica su cantidad.
- Al marcar pendientes aparece la barra contextual con cantidad, fecha, hora y «Guardar digitación». Guardar confirma las pruebas y muestra inmediatamente los valores persistidos en la misma búsqueda. Las ejecuciones repetidas mantienen sus UUID independientes.
- Para corregir un guardado: clic derecho en su fila, «Corregir digitación». Exige permiso `planning.report.review`, motivo y confirmación; el servidor registra el cambio e invalida el chequeo afectado conservando su historia. El check guardado no desmarca datos por sí solo.
- Configuración permite mostrar u ocultar columnas opcionales y restablecerlas. Clic derecho en una cabecera permite moverla o establecer su ancho; también conserva el tirador de ancho. Preferencias versionadas por usuario en `LocalAppData/Lims/ReportList/columns-{id}.json`.
- Se conservan selección de celdas, Ctrl+C, navegación por teclado, virtualización nativa y carga incremental. Chequeo de horarios sigue en su pestaña, sin columna permanente en Digitación.

## Captura AR identificada

`ReportListRules` valida el formato/checksum AR y resuelve el modo por prueba/laboratorio. `PostgresReportList.ScanReportAsync` utiliza el vínculo de tracking para identificar una ejecución; rechaza asociaciones ambiguas de pruebas repetidas y utiliza la persistencia/auditoría existente. Por eso se conservó la función, trasladada a «Capturar código AR» en configuración, disponible con permiso y tracking validado. Abre un diálogo de código, fecha y hora que registra con Enter a través del mismo endpoint. No se declara conectado ni validado un escáner físico.

## Aceptación ejecutada

Cliente original abierto y autenticado contra datos reales restaurados localmente. JOB **GUAT26022934**, dos PH de Chemical:

| Ejecución | Estado inicial de esta comprobación | Resultado |
|---|---|---|
| `25fdff34-301c-4593-933d-4461a385072a` | Digitada | Registro completo idéntico al inicio; no se modificó. |
| `6f159138-56b1-417e-a0a6-0958e570e37b` | Sin información | Seleccionada y guardada desde WinUI con fecha/hora; horario chequeado desde WinUI; persistencia confirmada tras reiniciar API y cliente. Después se revirtió exclusivamente este marcado de prueba mediante la corrección autorizada, conservando auditoría. |

Evidencia de persistencia: `persistence-after-restart.json`. Corrección y estado final: `correction-audit.json`. El estado final conserva una PH digitada y la otra pendiente; no se borraron ejecuciones ni historia.

La prueba de alcance marcó 12 filas visibles entre 500 cargadas. Tras desplazarlas fuera de vista, la confirmación impidió guardar sin consentimiento explícito; se canceló sin escribir (`selection-scope.json`). Las capturas de selección múltiple no representan un guardado masivo ejecutado.

Se comprobó mostrar Estado de digitación, ocultar Cliente, mover JOB y guardar su ancho de 200 px mediante los controles nativos; después se restauró la disposición predeterminada (`column-preferences.json`). La carga incremental llegó a 3,000 ejecuciones con 17 filas visibles (`volume.json`), sin interpretar esta medida local como rendimiento de LAN.

La revisión visual detectó que la animación de ocultación podía dejar transparente la barra contextual después de desmarcar. Al volver a mostrarla ahora se restablece su entrada visual; la aceptación repite marcar, desmarcar y volver a marcar antes de guardar.

Compilación final x64: **0 errores y 0 advertencias**. **31 pruebas de cliente/modelos aprobadas, 0 omitidas**, con TRX y logs en `artifacts/report-list-usability`. Los registros `native-*.json` identifican la DLL del cliente probado. Las pruebas de backend y autorización de la entrega anterior se conservan separadas; no se suman como una ejecución nueva en esta corrección visual.

Las capturas son de la aplicación, no maquetas. La galería está en [evidencia.html](../artifacts/report-list-usability/evidencia.html). La validación acredita el entorno local restaurado; no acredita despliegue productivo, LAN, hardware mínimo ni escáner físico. Producción permanece sin cambios.
