# Arquitectura

La solución es un monolito modular con dependencias hacia adentro:

`Desktop/API → Application → Domain`, mientras `Infrastructure` implementa puertos definidos por `Application`.

`User`, `Role`, `Department` y `Permission` son conceptos separados. Las políticas de ASP.NET Core centralizan rol, permiso y departamento; la UI puede adaptar visibilidad, pero el backend conserva la autoridad.

La identidad legada se mapea a `usuarios`, `roles`, `departamentos` y `usuarios_permisos`. Esas tablas están excluidas de migraciones. Este checkpoint solo posee `auth_sessions` y `auth_refresh_tokens`.

El cliente usa Host/DI, `IHttpClientFactory`, ViewModels de CommunityToolkit.Mvvm y navegación única Login → shell temporal. No usa WebView, HTML ni acceso directo a PostgreSQL.

Serilog produce eventos estructurados y OpenTelemetry instrumenta ASP.NET Core, HTTP, runtime y `ActivitySource` de Npgsql. Correlation ID viaja en `X-Correlation-ID`.
