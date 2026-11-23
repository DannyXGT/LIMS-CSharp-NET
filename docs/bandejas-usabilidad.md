# Bandejas de revisión: composición y lectura con datos

20 de noviembre de 2026. Ajuste de presentación dentro del Centro de Importaciones existente. Conserva autenticación, permisos, consulta al servidor, tabla virtualizada, fuentes y decisiones auditadas.

## Cambios

- Cabecera compacta con nombre del módulo, sus tres pestañas y alcance del lote. Se retiraron el subtítulo y el título de sección repetido, y el espacio reservado entre filas colapsadas.
- Las siete bandejas pasan a una navegación lateral con nombre completo, total de casos y pendientes legibles. En ventanas estrechas se sustituyen visualmente por un selector con las mismas siete opciones. Los números proceden de los contadores del servidor; no se suman entre categorías, pues pueden superponerse.
- Búsqueda, situación, filtros, columnas y exportación comparten una barra. El resumen muestra las situaciones de la bandeja activa sin repetir el identificador del lote ni sus totales en el pie. Detalle y Acciones conservan su flujo y autorización.
- JOB, Motivo y Situación aparecen al inicio de la tabla. Siguen presentes las diez columnas predeterminadas: también Fecha ingreso, Due Date, Estado TIPS, Buyer, Vendor, Servicio y USD. Las restantes se muestran desde Columnas. Se conservan orden, anchos y JOB fijo durante el desplazamiento.
- El pie distingue el intervalo/página filtrada de la selección de filas y celdas. Se corrigieron los singulares. Las instrucciones de teclado quedan en la ayuda accesible y en Columnas; ya no aparece un tooltip de toda la tabla encima de los datos al mover el puntero.
- Al volver a consultar se conservan visibles las filas anteriores mientras llegan los nuevos datos; sus interacciones permanecen deshabilitadas durante esa consulta. El mensaje central de carga se reserva a una tabla todavía vacía. Las decisiones y sus versiones continúan validadas por el servidor.

## Evidencia y alcance

La validación usa exclusivamente el cliente LIMS autenticado contra la restauración local `127.0.0.1:55441/lims_planning_dev`, mediante `https://localhost:7441`. El lote seleccionado tiene **14,722 casos pendientes de Solo Summary**, con 100 registros en la primera página; sus cifras pueden diferir de la captura del servidor del usuario.

`artifacts/import-review-usability/baseline.json` conserva los contadores y la primera página originales. Cada recorrido vuelve a consultar y compara casos, contadores y total. No se importan archivos ni se confirman, retienen, resuelven o excluyen casos durante esta revisión visual. No hay cambios de esquema, contratos ni backend.

Las capturas y registros nativos están en `artifacts/import-review-usability`. El script reproducible es `scripts/validate-import-review-usability.ps1`. Las imágenes se capturan de la ventana abierta; cuando la captura del escritorio no está disponible se utiliza `PrintWindow` y se registra ese modo por imagen. No son maquetas.

Se compilan tanto `bin/x64/Debug` como `bin/Debug` para que el comando WinAppCLI habitual encuentre los cambios. El cliente que ya estaba abierto conserva su DLL anterior hasta cerrarlo y volverlo a abrir. Compilar localmente no despliega la API ni modifica producción.

## Aceptación nativa

Pasaron los recorridos de las siete bandejas, búsqueda de JOB real, selección rectangular y copia de dos filas por tres columnas mediante teclado, paginación, filtros, configuración de columnas, apertura de los tres alcances de exportación, detalle con procedencia y acciones según permisos. También se verificó el selector de bandejas a 1366×768 y 1024×768. No se ejecutaron decisiones sobre casos ni una exportación completa nueva.

Después de esos recorridos se ajustó únicamente el ancho del contenido de las bandejas para alinear nombres y contadores. Con esa compilación final se repitieron la apertura con datos, la captura principal y la comparación de datos persistidos. `acceptance.json` distingue ambas compilaciones. Los dos destinos Debug compilaron con cero errores y cero advertencias; la sintaxis del script y `git diff --check` pasaron.

- [Vista final con datos](../artifacts/import-review-usability/ui/01-bandejas-con-datos.png)
- [Detalle y procedencia](../artifacts/import-review-usability/ui/09-detalle-con-procedencia.png)
- [Ventana de 1366×768](../artifacts/import-review-usability/ui/11-ventana-1366x768.png)
- [Registro de aceptación](../artifacts/import-review-usability/acceptance.json)
