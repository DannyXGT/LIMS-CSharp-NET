# Auditoría del login legacy

Se revisaron los ocho documentos `docs/dotnet-migration/login` y el código efectivo del LIMS React/FastAPI sin modificarlo.

Hallazgos confirmados:

- Passlib Argon2 para passwords.
- JWT HS256: access 60 minutos y refresh 720 minutos.
- Refresh en texto plano, sin rotación ni reuse detection.
- DDL ejecutado dentro del flujo de login.
- `@intertek.com` aplicado en cliente, no como autoridad backend.
- roles y departamentos son conceptos distintos.
- frontend con refresh single-flight.
- revocación de access incompleta y respuestas de ping ambiguas.
- endpoints administrativos de auth sin protección suficiente.
- declaraciones duplicadas de relaciones SQLAlchemy en `User`.

No hubo diferencias materiales entre la documentación de auditoría y el código inspeccionado. No se confirmó el contenido real de PostgreSQL porque la configuración disponible no proporcionó una conexión de Development/Testing funcional; no se intentó modificar ni probar contra producción.
