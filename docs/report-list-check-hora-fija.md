# Report List: CHK directo, permanencia y hora fija

Cambios del 23 de noviembre de 2026 en el cliente WinUI y su API existente. Se revisaron `Desktop/programas/App_web/Digitacion_23_OCT.html` y Report List de LIMS React.

- **Fijar Hora** toma la fecha y el minuto de Guatemala y los deja visibles. Se pueden ajustar explícitamente. No avanzan mientras se digitan las siguientes pruebas; otro clic en Fijar Hora toma el momento actual. Sin fijarlos no se permite un nuevo CHK ni una captura AR. Al cerrar la sesión se descarta esa hora fija.
- **CHK** guarda directamente la ejecución. También se puede usar Enter o doble clic en una celda de datos. Desmarcar es una corrección con permiso de revisión e historial; no elimina la prueba. Las pruebas que requieren scanner tienen deshabilitado el CHK manual para usuarios sin excepción administrativa.
- **Mantener en Muestreo** queda visible y se guarda desde la celda. Su concurrencia se controla por separado de la versión de digitación; cambiarla no vuelve inválido un horario ya revisado. La operación conserva su permiso de revisión existente.
- **Grupos del HTML:** `checkGroups()` usa fecha y hora, y `computeCierres()` exige ambas en todas las pruebas del JOB. Los CHK individuales siguen creando sesiones auditables independientes, pero Chequeo de horarios las reúne por fecha y minuto operativo, incluso si las guardaron distintos usuarios. El número histórico de 15 minutos no sustituye ese horario.
- **Cierres** sigue usando todas las ejecuciones requeridas persistidas del JOB y conserva los bloqueos por incidencias, retención o identidades sin conciliar. No se calcula sobre las filas visibles o una selección. El horario fijo guardado determina la fecha/hora operativa de esas digitaciones; la auditoría conserva el momento real del servidor. No constituye cierre oficial TIPS.
- **Captura AR:** entrada visible y botón Leer AR, con foco para un lector que emule teclado. El servidor valida checksum, tracking e identidad y no elige una ejecución ambigua. Este cambio no implementa conexión serial COM ni certifica un lector físico.
- **Una pantalla:** las nueve columnas operativas se distribuyen con mínimos y proporciones según el ancho. Ingreso, vencimiento, bloque histórico y selección temporal quedan opcionales. La selección temporal tiene su propia columna y guardado múltiple, separados del CHK directo y de la revisión posterior.
- **Continuidad:** guardar una fila no cambia a Todas ni reemplaza la colección completa. Se consulta por UUID y se actualiza o retira solo esa fila según la vista. La continuación usa el número actual de filas cargadas para no saltar pendientes después de un CHK. Las páginas ceden el hilo de UI cada 32 inserciones. El sondeo externo informa cambios sin reiniciar automáticamente el desplazamiento de una tabla ya cargada.

La persistencia de estas acciones sigue siendo la de `planning.executions`, `planning.report_checks`, sesiones e historial nativos. Este cambio no instala un puente de archivo/restauración hacia `public.planificacion_report_check` ni modifica las tablas públicas de React. El catálogo de nombres de React y el puente de importación ya existente se conservan; esa integración de fuentes no debe presentarse como sincronización bidireccional de checks.

## Validación

La evidencia se guarda en `artifacts/validation/report-list-direct`:

- `tests/desktop.trx`: 22 pruebas aprobadas del modelo de tabla, hora fija, permisos de captura/corrección, actualizaciones por ejecución y compatibilidad del cliente.
- `tests/direct-postgres.trx`: seis casos en bases PostgreSQL efímeras locales: agrupación del horario, reintento, permanencia con concurrencia independiente, revisión y corrección, cierre completo, continuación de páginas y captura AR real del backend con tracking de prueba.
- `tests/authorization.trx`: 33 pruebas aprobadas de permisos de API.
- `tests/final-postgres.trx`: dos casos aprobados al repetir las operaciones con las revisiones de datos y catálogo finales. `tests/scanner.trx`: captura AR aprobada con hora fija 08:45, checksum, tracking y reintento idempotente.
- `report-list-direct.json`: página WinUI y cliente HTTP reales con transporte en memoria, sin login, API desplegada ni base de producción. Verifica fijar hora, CHK, permanencia, corrección, elegibilidad de scanner y geometría de la pantalla.
- Comprobación visual final del 24 de noviembre: CHK visibles de PH corregido, CROCKING digitado y APEOS capturado por AR; hora fija 08:45; cero desbordamiento horizontal en 1366×768 y 1280×800. `final-page.png` se genera dentro de esa comprobación con datos ficticios.
- `report-list-scroll.json`, repetido el 24 de noviembre: 3.500 filas cargadas de un total ficticio de 9.038, siete páginas y máximo 73 filas realizadas por la UI. Se verificaron reutilización de controles, nombre de planificación, alineación y horarios. Su medición incluye esperas e inspección de controles; no demuestra 60 FPS ni rendimiento en el equipo de producción.

La primera corrida de PostgreSQL (`tests/postgres.trx`) pasó 62 de 63 casos: el nuevo caso de cierre conservaba incidencias intencionales del Summary de fixture. Se corrigió la preparación de ese caso, sin relajar las reglas de cierre, y se repitieron los seis casos afectados en `direct-postgres.trx`. Las capturas y el transporte en memoria no certifican fluidez por LAN ni funcionamiento físico del lector.

## Activación

Actualizar **API y cliente** desde este código. Estos cambios no necesitan una migración SQL adicional. El servidor anterior no conoce el filtro por UUID ni el desplazamiento explícito; el cliente detecta una respuesta que no corresponde a la ejecución solicitada e informa que hay que actualizar.

Cerrar la ventana anterior del cliente y ejecutar desde la raíz:

```powershell
$env:DOTNET_ENVIRONMENT = "Production"
C:\Tools\WinAppCLI\winapp.exe run `
  .\Frontend\Lims.Desktop\Lims.Desktop.csproj `
  --configuration Debug `
  --arch x64 `
  --property Platform=x64 `
  --property UseSharedCompilation=false `
  --with-alias
```

El comando recompila el cliente; no despliega ni reinicia la API del servidor.
