# Seguridad

## Contraseñas

Los hashes existentes se verifican como PHC Argon2. La política nueva usa Argon2id v19, 64 MiB, 3 iteraciones, paralelismo 4 y hash de 32 bytes. Un hash válido con parámetros distintos se reemplaza progresivamente durante login.

Antes de desplegar debe verificarse al menos un hash real anonimizado de producción. La venv legada inspeccionada tenía Passlib, pero no un backend Argon2 disponible, y no se obtuvo conexión segura a una DB de prueba; por ello esa aceptación permanece explícitamente pendiente.

## Secretos

La cadena PostgreSQL y la signing key no están versionadas. API falla rápido si faltan. Development usa User Secrets o variables de entorno. HS256 se eligió por simplicidad operacional para un único backend; la clave nueva no debe reutilizar el secreto legacy y debe tener al menos 32 caracteres aleatorios.

## Windows Credential Locker

`PasswordVault` almacena solo el refresh token bajo la identidad empaquetada y el usuario Windows actual. No se guarda password. Logout elimina la credencial; access token y perfil se borran de memoria.

Limitaciones: el almacenamiento está vinculado al usuario Windows y a la identidad del paquete. Reinstalar con otra identidad no recupera la credencial. Windows puede conservar credenciales al desinstalar según administración del sistema; el despliegue debe probar su política corporativa y ofrecer limpieza explícita.

## Logs

No se registran passwords, JWT, refresh tokens, `Authorization` ni cadenas de conexión. Los eventos usan UserId, códigos de error y correlation ID cuando están disponibles.
