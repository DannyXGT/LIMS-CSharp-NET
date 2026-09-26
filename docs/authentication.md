# Autenticación y sesión

## Contrato

- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/me`

El backend acepta la parte local o el correo completo del dominio corporativo configurado, normaliza a minúsculas y rechaza espacios, dominios externos y cuentas inactivas.

## Tokens y revocación

- Access token JWT HS256: 10 minutos, configurable hasta 30 minutos.
- Refresh/session lifetime absoluta: 12 horas, configurable hasta 30 días.
- Se permiten múltiples sesiones independientes por usuario/dispositivo.
- Cada access token incluye `sid`; JWT bearer comprueba en PostgreSQL que esa sesión siga activa en cada request.
- El refresh tiene 256 bits aleatorios. PostgreSQL recibe únicamente SHA-256, nunca el token real.
- Cada refresh se consume una vez y rota dentro de una transacción.
- Reuse de un token consumido revoca toda la sesión y su familia.
- Logout revoca sesión y refresh tokens. El cliente limpia siempre su estado local aunque falle la revocación remota.

El cliente mantiene el access token solo en memoria. Ante 401, todas las requests concurrentes comparten un refresh; cada request se reintenta como máximo una vez.

## Contratos de error

La lógica consume códigos estables (`auth.invalid_credentials`, `auth.invalid_session`, `auth.forbidden`, etc.), no mensajes. 5xx, timeout, DNS o red caída no invalidan automáticamente la sesión.
