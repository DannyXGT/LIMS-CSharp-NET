# Pruebas

```powershell
dotnet test Tests/Backend/Lims.Application.Tests/Lims.Application.Tests.csproj
dotnet test Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj
dotnet test Tests/Backend/Lims.Api.Tests/Lims.Api.Tests.csproj
dotnet test Tests/Frontend/Lims.Desktop.Tests/Lims.Desktop.Tests.csproj -p:Platform=x64 -m:1
```

Desktop Tests desactiva auto-bootstrap de Windows App SDK porque prueba lógica MVVM/HTTP dentro de testhost sin paquete. `-m:1` evita que dos variantes condicionadas del compilador XAML escriban simultáneamente el mismo `obj`.

Cobertura útil actual:

- login, credenciales genéricas, usuario inactivo y dominio;
- refresh válido/reuse y revocación en Application;
- Argon2, hashing de refresh y alcance del modelo EF;
- 401, `/me`, policies, rate limit y correlation ID por HTTP;
- doble submit, estados MVVM, navegación, sesión local y refresh single-flight.

Pendiente de aceptación: PostgreSQL real exclusivo de Testing, aplicar/revertir migración allí, login con usuario real anonimizado, refresh concurrente contra Npgsql y revisión visual en 1280×720/1366×768/1600×900/1920×1080 a 100/125/150 %.
