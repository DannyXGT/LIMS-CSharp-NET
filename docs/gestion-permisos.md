# Gestión de permisos

Implementación local de Administración → Gestión de permisos. No se aplicó SQL ni migraciones a InterDB, no se importó identidad de Trazalab y no se modificaron fórmulas ni transacciones operativas de Estándares, Stock o Intermedia.

## Identidad y alcance

Se reutiliza el modelo existente: `usuarios.id_departamento → departamentos.id`, `usuarios.id_rol → roles.id`, `usuarios.activo`, `usuarios.nombre` y `usuarios.email`. El servidor busca el nombre exacto `departamentos.nombre = 'Laboratorio Analítico'`; no hay IDs predeterminados. `reference_locations` es ubicación física de estándares y no participa.

La existencia del nombre y de sus usuarios en **InterDB no se verificó**: la solicitud prohíbe ejecutar SQL allí. El módulo consulta la relación al ejecutarse; si el departamento no existe devuelve `DepartmentExists = false`, lista vacía y un mensaje explícito. No interpreta Laboratorio Químico como equivalente. Usuarios inactivos, sin departamento, inexistentes o de otro departamento quedan fuera tanto del listado como de lectura, escritura e historial. El servidor devuelve 403 sin revelar sus datos.

## Persistencia

Migración pendiente: `20261013060720_AddIndividualPermissions`.
SQL para revisión: `artifacts/sql/individual_permissions.sql`, generado sin conexión a la base de datos; contiene únicamente esta migración de avance.

| Tabla | Finalidad |
|---|---|
| `permission_catalog` | Códigos estables y FK para impedir códigos inexistentes. Solo se siembra el catálogo, nunca usuarios o concesiones. |
| `user_permission_sets` | Una fila por usuario con versión optimista. FK restrictiva a `usuarios(id)`. Su existencia identifica una matriz configurada. |
| `user_permission_assignments` | PK usuario/código, estado concedido o revocado, administrador responsable y fecha UTC. Revocar conserva la fila. |
| `user_permission_audit` | Historial con destinatario, actor, código, estado anterior/posterior y fecha UTC. No existe endpoint de borrado. |

Guardar verifica administrador activo y destinatario elegible dentro de una transacción serializable. Calcula diferencias, actualiza estado, registra auditoría y cambia la versión en una sola operación. La primera configuración registra las nueve decisiones explícitas, incluso denegaciones. Un guardado obsoleto devuelve 409. Duplicados y códigos desconocidos devuelven 400. El actor procede de `sub` autenticado; el DTO no admite actor, roles ni cambios de cuenta. Las tablas de identidad y `usuarios_permisos` siguen excluidas de las migraciones; no se alteran.

## Permisos y API

| Módulo | Códigos |
|---|---|
| Estándares | `reference_materials.view`, `.create`, `.edit`, `.archive`, `.replace` |
| Stock | `stock.view`, `stock.create` |
| Intermedia | `intermedia.view`, `intermedia.create` |

Solo se incluyen acciones existentes. Catálogo extensible por códigos y módulo; Curvas, AQS, Alertas y Compras no se implementan.

Todos los endpoints de lectura y escritura de estos módulos requieren su política correspondiente; opciones/fuentes/cálculos previos de preparación requieren el permiso de creación. Reemplazo requiere `reference_materials.replace`. Administrador activo mantiene acceso completo por el rol existente, sin filas manuales.

Grupo administrativo, siempre protegido por Administrador:

- `GET /api/administration/permissions/catalog`
- `GET /api/administration/permissions/users?search=...`
- `GET /api/administration/permissions/users/{id}`
- `PUT /api/administration/permissions/users/{id}`: lista deseada y versión; persiste solo diferencias.
- `GET /api/administration/permissions/users/{id}/history?page=1`: 30 filas por página.

`GET /api/auth/permissions` requiere sesión y devuelve solo los permisos efectivos del propio usuario.

La validación JWT existente conserva la comprobación de sesión activa. En cada petición autenticada consulta el usuario y sus permisos vigentes, elimina roles/permisos del JWT anterior y reconstruye esas claims desde PostgreSQL. No hay caché de autorización. Una revocación confirmada impide nuevas solicitudes; una operación que ya pasó autorización puede completar su transacción. Usuarios desactivados reciben 401. Una matriz configurada pierde sus permisos efectivos si el usuario deja el departamento autorizado. Fallos de base de datos no conceden acceso.

## Transición controlada

`PermissionManagement:EnforceIndividualPermissions` se entrega en `false` para preparar asignaciones sin interrumpir a usuarios todavía no configurados. En esa fase:

1. Administradores mantienen su acceso por rol.
2. Usuarios sin matriz individual conservan los permisos **existentes en PostgreSQL**, no los del JWT. Para reproducir el comportamiento previo, `.view` de Estándares habilita consultas Stock/Intermedia, `.create` habilita creación de preparaciones, y create+archive habilitan reemplazo.
3. Guardar la matriz activa autorización individual para ese usuario inmediatamente. Una matriz vacía niega los nueve permisos; las filas legadas no se copian, borran ni restauran por una revocación.
4. Tras configurar y revisar usuarios, establecer `PermissionManagement__EnforceIndividualPermissions=true` y reiniciar la API activa mínimo privilegio global. Los usuarios sin matriz quedan sin permisos de estos módulos. Mantener `true` después de la transición.

Antes de desplegar esta API se requiere aplicar la migración en el entorno autorizado. Sin las tablas nuevas, la validación de peticiones necesita actualización de esquema y falla cerrada. **No ejecutar comandos de actualización contra InterDB sin autorización separada.** El flag controla la transición de acceso; no sustituye la migración.

## WinUI

Sidebar: Administración → Gestión de permisos solo para administradores. Búsqueda por nombre/correo, rol y departamento reales, matriz agrupada con checkboxes accesibles, indicador de cambios locales, Guardar cambios, recarga y auditoría paginada. La matriz muestra asignaciones explícitas; aclara el acceso implícito de un destinatario Administrador.

Cambiar usuario, buscar, recargar o salir hacia otra sección advierte antes de descartar modificaciones. Durante el guardado se deshabilita la edición. Fallos de red conservan cambios; un conflicto requiere recarga y decisión de descarte. El estado exitoso procede de la respuesta real del servidor y el historial se vuelve a consultar.

La navegación consulta permisos antes de entrar y cada 15 segundos durante la sesión. Oculta módulos y pestañas sin acceso; al perder acceso limpia datos cargados y muestra un mensaje humano. El servidor valida cada operación independientemente de ese refresco visual.

## Verificación reproducible

```powershell
$env:VSLANG = '1033'
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1 -p:BuildInParallel=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-permissions.ps1
git diff --check
```

El wrapper de pruebas usa exclusivamente PostgreSQL local `127.0.0.1:55439`, base `lims_stock_test`, y lo detiene al terminar. La prueba de permisos vuelve a comprobar host/puerto/base antes de crear un esquema aislado, reproduce la identidad existente, ejecuta la migración nueva y verifica concesiones, revocaciones, auditoría y guardados concurrentes. No usa la conexión de InterDB.

Capturas en `artifacts/validation/permissions`: listado, selección/matriz, pendientes, error de red, guardado, historial, usuario común sin acceso y aviso antes de descartar cambios, a 1366×768. El harness compila la pantalla real y usa exclusivamente fixtures en memoria; las capturas no prueban persistencia productiva.

## Resultado de validación · 13 de octubre de 2026

- Build Debug x64: 0 advertencias, 0 errores. Harness: 0 advertencias, 0 errores.
- 273/273 pruebas aprobadas: Application 115, API 38, Infrastructure 45 y Desktop 75. Ninguna omitida en la ejecución con PostgreSQL aislado.
- PostgreSQL aislado: migración ejecutada en esquema exclusivo de pruebas; concesión, revocación, identidad del actor, historial conservado y dos guardados concurrentes verificados.
- UI Automation real: cambiar de usuario con modificaciones abre el aviso; elegir «Seguir editando» conserva la selección original. La matriz expone checkboxes con nombres humanos. El usuario común no muestra módulos ni datos protegidos.
- Ocho capturas reales a 1366×768 y reportes UIA de matriz/acceso denegado. No son renders ni mockups generados.
- `git diff --check` aprobado.
- Pendiente: autorización/aplicación de la migración en InterDB, despliegue de API/desktop y comprobación de sus datos reales de departamento/usuarios. No se ejecutó SQL en InterDB.

| Captura | Archivo |
|---|---|
| Listado | [Listado a 1366×768](../artifacts/validation/permissions/01-listado-1366x768.png) |
| Usuario y matriz | [Usuario seleccionado](../artifacts/validation/permissions/02-usuario-matriz-1366x768.png) |
| Cambios locales | [Pendientes de guardar](../artifacts/validation/permissions/03-cambios-pendientes-1366x768.png) |
| Error de red | [Cambios conservados](../artifacts/validation/permissions/04-error-red-cambios-preservados.png) |
| Confirmación | [Guardado exitoso](../artifacts/validation/permissions/05-guardado-exitoso.png) |
| Auditoría | [Historial](../artifacts/validation/permissions/06-historial-1366x768.png) |
| Usuario común | [Acceso denegado](../artifacts/validation/permissions/07-usuario-comun-acceso-denegado.png) |
| Descarte | [Aviso de cambios sin guardar](../artifacts/validation/permissions/08-aviso-cambios-sin-guardar.png) |

## Corrección de compatibilidad del cliente · 13 de octubre de 2026

Comprobación de solo lectura en la API configurada: `/health/live` devuelve 200, `/api/auth/me` y `/api/reference-materials` devuelven 401 sin credenciales; `/api/auth/permissions` y `/api/administration/permissions/catalog` devuelven 404. La API disponible no expone las rutas nuevas. No se consultaron datos de usuarios ni se ejecutó SQL.

El cliente conserva el nombre, rol y departamento autenticados cuando falla la consulta de permisos. Las secciones operativas quedan cerradas hasta verificar el acceso con el servidor; un aviso visible permite reintentar. La entrada de Administración permanece accesible para el perfil Administrador y la pantalla informa si el servicio requiere actualización.

Únicamente ante 404 de `/api/auth/permissions`, el cliente consulta `/api/auth/me` y utiliza ese perfil actual del servidor con las equivalencias de permisos legados existentes. No usa como fallback permisos almacenados localmente y no activa esta compatibilidad ante 401, 403 o 5xx. Las respuestas de error vacías o HTML conservan su código HTTP.

Para guardar asignaciones reales sigue siendo necesario aplicar `AddIndividualPermissions` en el entorno autorizado y desplegar la API que incluye las rutas administrativas. La corrección del cliente no realiza ese despliegue ni aplica migraciones.

Verificación de esta corrección: 85/85 pruebas Desktop y 38/38 API aprobadas. Desktop compilado sin advertencias ni errores y API Release publicada localmente en `artifacts/publish/permissions-api`. Se reabrió la aplicación real y se comprobó por SHA256 que AppX ejecuta el binario nuevo; la ventana quedó en Iniciar sesión. La sesión real del administrador y el guardado productivo siguen pendientes. Evidencia: `artifacts/validation/administrator-access/verification.json`.
