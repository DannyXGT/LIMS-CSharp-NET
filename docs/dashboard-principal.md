# Dashboard principal LIMS .NET

Un módulo WinUI 3 reemplaza Inicio. DashboardPage, DashboardViewModel y DashboardComponents comparten indicadores, superficies, distribución, filas/tabla de vencimientos, actividad y acciones. Reutiliza el tema, iconos, Motion, barras de disponibilidad y módulos existentes.

## Autorización y composición

GET /api/dashboard requiere una sesión activa. El usuario se obtiene exclusivamente de sub; el servidor vuelve a resolver permisos efectivos y consulta el departamento relacionado por usuarios.id_departamento. Cada sección requiere reference_materials.view, stock.view o intermedia.view; Administrador conserva acceso por rol.

El modelo existente tiene un rol y un departamento por usuario. Prioridad explícita: Administrador, Laboratorio Analítico (incluida la grafía del catálogo sin acento), Compras configurado, operaciones autorizadas, inicio restringido. No hay un selector local que permita saltar esta resolución. Revocar permisos limpia la información anterior y cancela respuestas tardías.

No se presume que exista un rol o departamento de Compras. Dashboard:PurchasingDepartmentName y Dashboard:PurchasingRoleName aceptan el nombre exacto de una relación existente, después de verificar el catálogo del entorno. Sin esta configuración no se identifica una cuenta como Compras por nombres inventados. Su composición permanece pendiente, sin consultas de inventario ni contadores de compras.

## Cálculos y datos

- Registrados incluye todos los estándares; activos respeta el estado efectivo existente. Para estándares se conserva la prioridad de agotado antes de vencido.
- Disponibilidad crítica reutiliza el umbral visual menor al 25%; baja incluye hasta el 60%. Estos indicadores cuentan saldos positivos de materiales utilizables, no materiales agotados o archivados.
- Se examinan todos los vencimientos futuros. La consulta ordena por fecha y devuelve los seis primeros; vencimiento hoy corresponde a cero días. El límite se aplica después de filtrar y ordenar en PostgreSQL, no después de descargar el inventario. No hay un plazo por defecto de 30 o 60 días.
- Cuando se autoriza consultar estándares, la lista prioritaria corresponde a estándares. Para cuentas que solo consultan preparaciones se muestran los vencimientos de sus preparaciones autorizadas.
- Stock e Intermedia disponibles respetan las condiciones de selección operativa existentes: saldo positivo, vigencia, fecha de preparación, método activo y estado del origen para Stock.
- Porcentaje de estándar = saldo / presentación total. Porcentaje de preparación = volumen disponible / volumen inicial. La fecha y el porcentaje son campos independientes. Los saldos inválidos muestran disponibilidad no calculada.
- La distribución se agrega en SQL; las listas de vencimientos, saldos bajos y actividad son acotadas. No se cargan todas las filas en Desktop.
- La actividad presenta registros de creación de estándares, archivo/reemplazo y creación de preparaciones, con material, actor real y fecha. UpdatedAt no se representa como historial de eventos ni se inventan ediciones históricas.

Cero es una consulta correcta. Una sección no autorizada se omite. Una consulta fallida limpia los valores anteriores y presenta error y reintento. Compras pendiente describe funcionalidades sin valores ficticios.

## Navegación y presentación

Registrados abre Estándares y Vencidos usa su filtro real Expired. Los enlaces de filas abren el registro por ID; Stock e Intermedia activan sus pestañas existentes. Los accesos administrativos solo se presentan a Administrador. Los indicadores sin filtro soportado son informativos.

Se conservan sidebar y navegación principal. Las entradas escalonadas duran 180 ms, con pausas de 30 ms; hover 140 ms y feedback pressed del DesignSystem. La política de Windows desactiva movimiento y escalonamiento. El contenido usa un scroll vertical y adapta columnas/tabla al ancho disponible. Light resuelve colores semánticos de su propio tema.

## Validación y límite de entorno

Se ejecutó PostgreSQL local aislado, con guardia de conexión 127.0.0.1:55439/lims_stock_test y esquema exclusivo. Las pruebas verifican conteos, precedencia de estados, vencimientos más allá de 60 días, días y porcentaje independientes, permisos limitados, revocación y Compras pendiente. Las pruebas HTTP verifican sesión y usuario autenticado; las pruebas del ViewModel cubren cero, error, recuperación y respuestas tardías.

scripts/validate-dashboard.ps1 compila/ejercita las páginas y shell reales mediante el harness existente, UI Automation y capturas. La barra superior identifica DATOS FICTICIOS. Matriz: Administrador, Laboratorio, Compras pendiente, limitado, sin permisos, vacío y error; Dark/Light; 1920×1080, 1600×900 y 1366×768. También se verifica scroll, filtro de vencidos, ficha por ID y accesos Stock/Intermedia/Permisos.

Evidencia: artifacts/validation/dashboard/. Ejecución reproducible: build Debug x64 serial, build del harness serial, scripts/test-dashboard.ps1 y scripts/validate-dashboard.ps1.

La API nueva requiere desplegar el backend actualizado. No se ha validado el endpoint en el servidor remoto ni con datos productivos. No se ejecutaron migraciones en InterDB, no se crearon tablas y no se implementaron Compras, Alertas, Curvas ni AQS. Los cambios locales ajenos al dashboard se conservaron.
