# LIMS .NET

Base del LIMS como monolito modular en .NET 10, ASP.NET Core, PostgreSQL y WinUI 3 nativo. La autenticación aprobada se conserva y la primera vertical operativa es Materiales de Referencia / Estándares.

## Estructura

- `Backend`: API, Application, Domain e Infrastructure.
- `Frontend`: cliente WinUI 3 y Design System reutilizable.
- `Shared`: contratos compartidos entre API y Desktop.
- `Tests/Backend`: pruebas Application, Infrastructure y API.
- `Tests/Frontend`: pruebas MVVM y HTTP del cliente Desktop.
- `artifacts/sql/authentication.sql`: SQL de autenticación; no aplicado.
- `artifacts/sql/materiales_referencia.sql`: DDL revisable del flujo Estándar → Stock → Intermedia → Curva/AQS; no aplicado.

## Inicio rápido

Requiere .NET SDK `10.0.302` y el toolchain WinUI ya validado en esta máquina.

```powershell
dotnet restore Lims.sln
dotnet build Lims.sln -p:Platform=x64 -m:1
dotnet test Tests/Backend/Lims.Application.Tests/Lims.Application.Tests.csproj
dotnet test Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj
dotnet test Tests/Backend/Lims.Api.Tests/Lims.Api.Tests.csproj
dotnet test Tests/Frontend/Lims.Desktop.Tests/Lims.Desktop.Tests.csproj -p:Platform=x64 -m:1
```

La API exige secretos al arrancar. Consulte [desarrollo](docs/development.md) antes de ejecutarla. Para WinUI use `scripts/run-winui.ps1`; no use el fallo conocido de AUMID de `dotnet run` como diagnóstico del framework.

## Estado del checkpoint

- Compilación backend y WinUI: completada sin advertencias.
- Arranque WinUI: validado mediante WinApp CLI con execution alias.
- Migración: generada y revisada, no aplicada.
- PostgreSQL real y credenciales reales: pendientes hasta disponer de una conexión de Development/Testing autorizada.
- Compatibilidad con un hash Argon2 extraído de producción: pendiente; la compatibilidad PHC/Argon2id está cubierta en tests, pero no se obtuvo una muestra real de DB.
- MainShell y vertical de Estándares: implementados en código; la migración nueva no se aplicó y la aceptación contra PostgreSQL/WinUI real sigue pendiente.

Más detalle: [arquitectura](docs/architecture.md), [modelo de Materiales de Referencia](docs/materiales-referencia-db.md), [autenticación](docs/authentication.md), [seguridad](docs/security.md), [pruebas](docs/testing.md) y [auditoría legacy](docs/legacy-audit.md).
