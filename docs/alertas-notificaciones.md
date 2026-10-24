# Alertas y Notificaciones

El módulo usa los saldos de `ReferenceMaterial` y `StockAvailability`, vencimiento efectivo del frasco comercial y un único registro por material/condición activa. Curva y AQS tienen volumen disponible cero por diseño: se evalúa su vigencia, sin inventar un saldo consumible. Leer un aviso, registrar atención y resolver una condición son operaciones diferentes. La resolución se registra al reevaluar la condición; archivar un origen o retirarlo del ámbito queda identificado en el historial.

## Puesta en servicio

### Finalización del motor (22/10/2026)

La consulta de la API no ejecuta el motor. `LastEvaluation = null` indica que no se ha confirmado una evaluación transaccional en esa base. Antes de esta corrección solo `Lims.AlertWorker` podía invocarlo: no existía ruta ni botón manual. La API disponible no demuestra que la tarea esté instalada. Sin acceso al servidor no se puede atribuir el estado remoto a una tarea ausente, credenciales, migración o error concreto.

`POST /api/alerts/evaluate` exige administrador activo y `alerts.manage`, verificados otra vez en el servicio. Usa el bloqueo PostgreSQL del ejecutor; una ejecución simultánea devuelve `Acquired=false`. La acción manual no despacha SMTP. `GET /api/alerts/evaluations` y «Historial del motor» muestran los últimos 30 ciclos. No se asignan departamentos por defecto: la ejecución manual rechaza una configuración sin ámbitos y el ejecutor registra ese fallo. Configurar los cinco ámbitos con titularidad real para evaluar Estándares, Stock, Intermedia, Curva y AQS.

La migración adicional `20261022224139_AddAlertEvaluationHistory` agrega únicamente `alert_evaluations` y su índice. Revisar el [preflight de solo lectura](../artifacts/sql/preflight_alerts.sql) y el [SQL incremental](../artifacts/sql/alert_evaluation_history.sql). El SQL tiene su propia transacción y exige el historial de `20261022031315_AddAlertsAndNotifications`; no aplicarlo sobre una base parcialmente migrada. No se ejecutó en producción. El despliegue requiere API, ejecutor y cliente compatibles, después de aprobar las migraciones pendientes.

Cada ciclo registra inicio, finalización, actor manual o tarea, materiales, cambios, alertas activas y correos confirmados. Los resultados y `LastEvaluation` se guardan en la misma transacción. Cero alertas con evaluación completada es saludable. `Failed` señala fallo anterior a confirmar la evaluación; `DeliveryFailed` conserva una evaluación confirmada aunque fallen canales. Tras una interrupción, el siguiente propietario del bloqueo marca los ciclos inconclusos como `Interrupted` y reevalúa idempotentemente.

El ejecutor escribe además `logs/alerts-YYYYMMDD.jsonl` junto al binario o en `Alerts__LogDirectory`. Conceder escritura a la cuenta y configurar retención mediante la política del servidor. Registra tipo de excepción y SQLSTATE sin texto del proveedor ni credenciales, incluso si falla antes de acceder a las tablas. `Lims.AlertWorker.exe --evaluate-only` evalúa sin despachar SMTP. No activar envíos automáticos hasta aprobar los destinatarios reales. La prueba SMTP existente usa exclusivamente el correo del administrador solicitante.

Preparar sin instalar: `scripts/install-alert-task.ps1 -WorkerExe <ruta> -EveryMinutes 5`. Incluye frecuencia configurable, arranque tras reinicio, recuperación de ejecuciones omitidas e ignorar solapamientos. `-Install` modifica Task Scheduler y necesita autorización de despliegue; no se ejecutó durante esta corrección. En el servidor, revisar `Get-ScheduledTask`, `Get-ScheduledTaskInfo`, los JSONL y el historial del motor. Las variables de IIS no se heredan por la cuenta de la tarea.

Para diagnóstico desde RDP, ejecutar [diagnose-alert-server.ps1](../scripts/diagnose-alert-server.ps1). Solo imprime JSON; no inicia tareas, ejecuta evaluaciones, consulta SQL ni cambia configuración. Detecta tareas por nombre LIMS/Alertas o ejecutable `Lims.AlertWorker`, identifica el directorio del ejecutor cuando es posible y recopila estado, última ejecución, versiones y campos seleccionados de logs. No muestra argumentos de tareas ni valores de conexión o credenciales. Permite indicar `-WorkerDirectory`, `-ApiDirectory` y `-LogDirectory` cuando se conocen las rutas. La configuración de archivo se identifica expresamente como incompleta: no representa las variables efectivas de la cuenta de la tarea. Ejecutar aparte el preflight SQL de solo lectura en la base del LIMS.

### Evidencia de esta corrección

- Backend: 19 pruebas de reglas, 14 de PostgreSQL aislado y una de rutas/permisos aprobadas. Incluyen las cinco familias, vencimiento, disponibilidad baja/crítica/agotada, idempotencia, trazabilidad, lectura por usuario, bloqueo, recuperación, cola y reintentos con transporte simulado. El SQL incremental se aplica dos veces sobre un esquema aislado y rechaza la ausencia de su prerrequisito.
- Cliente: 24 pruebas aprobadas. WinUI y ejecutor compilan sin advertencias ni errores. El harness pasó tema claro/oscuro a 1366×768 y 1920×1080, filtros, campana, lectura, ficha, vacío saludable, motor pendiente y errores de sincronización. Son datos de prueba, no una sesión del backend real.
- Ejecutor real: `scripts/validate-alert-worker-local.ps1` completó dos ciclos sobre un material de prueba ya existente en `127.0.0.1:55439/lims_stock_test`, sin crear materiales, sin alterar inventario y sin despachar SMTP. Cada ciclo evaluó un material, registró su identificador y produjo cero cambios adicionales. Evidencia: `artifacts/validation/alerts-functional/worker-controlled.json` y `worker-logs`.
- Windows aceptó un aviso nativo aislado con el cliente abierto. No se verificó clic de activación ni generación con el cliente cerrado. Evidencia actual: `artifacts/validation/alerts-remake/windows-notification.json`.
- Servidor configurado: `/health/live` y `/health/ready` respondieron 200; `/api/alerts` respondió 401 sin credenciales y `/api/alerts/evaluations` respondió 404. La nueva ruta no está desplegada. No se verificaron los ámbitos, migraciones, tarea, errores del motor ni SMTP del servidor. No se ejecutaron migraciones productivas, instalaciones de tareas, publicaciones ni correos reales. No se declara cerrado el flujo productivo.

1. Revisar la migración `20261022031315_AddAlertsAndNotifications` y [SQL incremental](../artifacts/sql/alertas_notificaciones.sql). Requiere el historial anterior completo. Solo agrega ocho tablas, índices y cuatro permisos; no altera inventarios. Su estado en InterDB debe comprobarse con el diagnóstico de solo lectura.
2. Publicar API y Desktop juntos. El backend verifica permisos actuales, usuario activo y departamento en cada operación. Los permisos nuevos se administran en Gestión de permisos; no se otorgan automáticamente a usuarios normales.
3. En Alertas → Configuración, asignar los ámbitos a departamentos del catálogo real. El inventario actual no tiene departamento por registro: esta asignación declara la titularidad del ámbito completo. **No hay destinatarios ni departamentos predeterminados.** Para repartir un mismo ámbito entre departamentos se necesita registrar primero esa titularidad por material. Los frascos compartidos entre Stock e Intermedia no se duplican.
4. Publicar el ejecutor: `dotnet publish Backend/Lims.AlertWorker -c Release -r win-x64 --self-contained false -o artifacts/publish/alert-worker`. Requiere .NET 10 en el servidor.
5. Configurar la misma conexión PostgreSQL y política de permisos para la cuenta que ejecuta la tarea. Configuración de IIS en `web.config` no se transmite automáticamente a una tarea de Windows. Usar variables de entorno de esa cuenta/servidor o configuración protegida por ACL fuera del repositorio. No copiar secretos a argumentos de línea de comandos.
6. Preparar el XML con `scripts/install-alert-task.ps1 -WorkerExe <ruta publicada>`. No modifica Windows. Para instalarlo en el servidor, usar `-Install -ServiceAccount <cuenta de servicio compatible>`, con los permisos necesarios. No se instaló ninguna tarea ni se modificó IIS durante la implementación.

El ejecutor termina después de cada ciclo, con límite de cuatro minutos. La tarea propuesta se repite cada cinco minutos, ignora solapamientos y recupera ejecuciones omitidas. Un bloqueo de sesión PostgreSQL impide carreras entre equipos; la evaluación y la creación de avisos/cola son transaccionales e idempotentes. No depende de clientes abiertos ni del ciclo de vida de IIS. Una caída conserva la cola para el siguiente ciclo.

## Correo

API y ejecutor leen `Alerts:Smtp` (variables `Alerts__Smtp__…`). Configurar `Enabled`, `Host`, `Port`, `Sender`, `Authentication`, `TransportSecurity`, `Username`, `Password` y `MaxAttempts`. La conexión usa `ConnectionStrings__Lims`. Las credenciales no aparecen en respuestas, trazas de entrega ni ejemplos de configuración.

El transporte incluido admite **STARTTLS obligatorio**, autenticación `Password`, `Windows` o `None` para un relay empresarial autorizado. No admite TLS implícito/puerto 465 ni OAuth SMTP. Confirmar con el proveedor qué modalidad permite antes de habilitarlo; un proveedor que exija OAuth necesita implementar `IAlertEmailTransport` con ese método. No se deshabilita la validación de certificados. Referencia: [SmtpClient.EnableSsl](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl).

Activar también el canal global desde la pantalla y seleccionar destinatarios existentes con permisos. La prueba autorizada se encola al correo del administrador solicitante y permite como máximo una solicitud nueva cada cinco minutos. La interfaz nunca presenta una prueba encolada como correo entregado.

Estados de entrega: `Pending`, `Sending`, `Sent`, `Retry`, `Failed`, `Cancelled`, `Uncertain`. Cada intento queda registrado. Solo `SendMailAsync` completado permite `Sent`; una respuesta SMTP temporal explícita permite reintentar con espera exponencial y límite. Un rechazo permanente termina en `Failed`. Si una caída/error impide conocer si SMTP aceptó el mensaje, queda `Uncertain` y no se reenvía automáticamente: verificar su `Message-ID` con el proveedor. SMTP no ofrece una transacción atómica con PostgreSQL; no se promete entrega exactamente una vez ante aceptación ambigua.

Los permisos, destinatarios, preferencias y condiciones se vuelven a comprobar antes de enviar. Un resumen se registra una vez por usuario y fecha local, con contenido vigente al enviarlo. Los correos críticos administrativos son obligatorios; la preferencia personal controla el resumen diario. SMTP sin configurar no bloquea el motor ni el centro interno.

## Cliente Windows

La campana consulta avisos cada treinta segundos y permite lectura individual, lectura masiva y apertura de la alerta/ficha real. Al cerrar sesión o cambiar permisos descarta el contenido y las respuestas anteriores. Los filtros y la paginación se resuelven en el servidor.

Las notificaciones usan `AppNotificationManager`, con activación COM declarada para el paquete y registro compatible con instalación sin paquete. Se suscribe la activación antes de registrar, se valida el destinatario de la activación y se vuelve a autorizar la alerta en el backend. Las preferencias personales vienen desactivadas para Windows; por defecto, al habilitarlas solo muestran críticas. Como máximo se intenta un aviso por ciclo, reciente, pendiente y reclamado atómicamente en el servidor, sin volver a mostrar el mismo aviso en otros clientes. La reclamación previa favorece evitar repeticiones; una interrupción antes de mostrar puede perder ese aviso nativo, que permanece en la campana.

La generación nativa funciona **con el cliente abierto** y depende de la configuración/paquetes disponibles en Windows. No hay agente residente ni generación de avisos con el cliente cerrado. La activación de un aviso ya entregado depende de Windows y exige sesión válida antes de abrir datos. Referencia: [AppNotificationManager.Register](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register).

## Evidencia reproducible

- `scripts/test-alerts.ps1`: PostgreSQL local aislado `127.0.0.1:55439/lims_stock_test`; requiere el clúster de pruebas existente. Incluye familias de origen, condiciones, historial, lectura, filtros, autorización, cola, recuperación y bloqueo del ejecutor. Los transportes SMTP de esas pruebas son dobles de prueba: no representan correos reales enviados.
- `scripts/validate-alerts.ps1`: interfaz WinUI real con fixtures explícitos; capturas oscuras/claras a 1366×768 y 1920×1080 y estados de error/vacío, campana y configuración. Usa el DPI del escritorio sin cambiar ajustes del usuario.
- `artifacts/validation/alerts-postgres`: resultados TRX y definición revisable de la tarea. `artifacts/validation/alerts`: capturas y reporte visual.
- `artifacts/validation/alerts/windows-notification.json`: Windows aceptó un aviso nativo de prueba con el cliente abierto tras comprobar el ejecutable COM del manifiesto generado. No se verificó clic de activación ni generación con el cliente cerrado.

El funcionamiento en producción requiere aplicar la migración autorizada, publicar los componentes, asignar departamentos/permisos reales, instalar la tarea y confirmar SMTP con el proveedor. La validación aislada no prueba esos pasos.
