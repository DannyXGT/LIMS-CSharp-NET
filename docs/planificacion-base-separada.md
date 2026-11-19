La API permite mantener autenticacion, usuarios, sesiones, permisos y demas modulos en InterDB y almacenar Planning en planificacion_migracion_test.

Configuracion del servidor

Conservar ConnectionStrings:Lims tal como esta (InterDB). Agregar ConnectionStrings:Planning con la cadena de conexion de planificacion_migracion_test. Puede usarse la variable de entorno ConnectionStrings__Planning. Copiar la cadena actual de Lims y cambiar solo Database cuando ambas bases compartan servidor y cuenta. Una configuracion Planning ausente o vacia mantiene la compatibilidad anterior usando Lims.

Actualizar la API Release del paquete y reiniciar su proceso despues de instalar los dos bloques SQL. Conservar los appsettings existentes, web.config, JWT, certificados y configuracion del servicio. El cliente actual conserva sus contratos y no requiere cambios.

Bases y SQL

- Ejecutar scripts/planning/install-planning-access-host.sql en InterDB. Agrega public.planning_access_history; las modificaciones de permisos y su auditoria se confirman juntas en la base del login. Los permisos ajenos a Planning se conservan. No modifica cuentas ni claves.
- En la ventana de planificacion_migracion_test que quedo abortada, ejecutar primero ROLLBACK;.
- Ejecutar completo scripts/planning/install-planning-separate.sql en planificacion_migracion_test. Instalacion inicial consolidada equivalente al estado final de seis migraciones: once tablas y trece FK, sin public.usuarios. Registra esas seis migraciones despues de ejecutar y verificar los cambios. No registra migraciones anteriores de otros modulos como aplicadas. Se detiene ante objetos/historial Planning existentes; no reconcilia instalaciones parciales.
- PostgreSQL 15 o posterior. No ejecutar dotnet ef database update del contexto global contra la base separada: ese contexto incluye los otros modulos y sus dependencias del host. Futuras migraciones de Planning deben desplegarse en su base efectiva.

Auditoria e identidad

planning.actors contiene solamente el ID y nombre de personas verificadas contra public.usuarios del host, sin passwords ni mecanismo de login. La API vuelve a consultar el usuario activo antes de registrar una identidad para una escritura de Planning; el actor proviene de la sesion autenticada, no del cuerpo de la solicitud. Historial, lotes y bandejas leen esos nombres locales y conservan sus FK. Los accesos se leen y actualizan exclusivamente en la conexion del host. La auditoria de nuevos cambios de permisos vive en public.planning_access_history del host, en la misma transaccion que los permisos; eventos anteriores de planning.history se conservan.

La migracion aditiva 20261119120000_SeparatePlanningIdentity permite actualizar instalaciones anteriores con base compartida: registra las identidades realmente referenciadas, preserva IDs/datos/historia, cambia las cuatro FK de identidad a planning.actors y crea la auditoria de permisos del host. No modifica las cinco migraciones anteriores.

Validacion

- Build API: cero errores y cero advertencias; publicacion Release portable para el runtime .NET 10 existente.
- 12 pruebas de reglas de importacion aprobadas.
- 26 pruebas PostgreSQL aprobadas: incluye dos bases independientes, ausencia de usuarios en el destino, importacion/consulta/nombres de auditoria, aislamiento de permisos, rechazo de usuarios inactivos/desconocidos, rollback de permisos si falla su auditoria y actualizacion preservando identidades e historia anteriores.
- 23 pruebas API aprobadas: 20 de autorizacion y 3 de seleccion de conexiones, preservando la conexion del contexto de autenticacion.
- Cinco comprobaciones SQL aprobadas: destino incorrecto, rollback completo por fallo intermedio, rechazo de esquema parcial, instalacion sin usuarios y rechazo de repeticion sin cambios.
- Smoke de la API Release real: login con la cuenta existente de la copia local, permisos vigentes, consulta y escritura auditada en una base desechable sin usuarios ni sesiones. Se cerro la sesion, se detuvo la API temporal y se elimino su base.

Toda la evidencia es local: servidor aislado 127.0.0.1:55441, copia lims_planning_dev y bases CI desechables. No se conecto ni modifico InterDB del servidor real ni planificacion_migracion_test remota. El despliegue, la configuracion efectiva del servicio y la ejecucion de SQL en ese servidor quedan pendientes de aplicacion por el operador.