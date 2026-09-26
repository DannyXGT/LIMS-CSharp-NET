# Desarrollo

## API

Configure secretos desde `Backend/Lims.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:Lims" "Host=SERVER;Database=DB;Username=USER;Password=SECRET"
dotnet user-secrets set "Authentication:Jwt:SigningKey" "UNA-CLAVE-ALEATORIA-DE-AL-MENOS-32-CARACTERES"
dotnet run
```

No coloque esos valores en `appsettings*.json`. Production debe terminar TLS en Kestrel o un proxy confiable; el cliente solo acepta una URL HTTPS configurada.

## Migraciones

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations script --idempotent --project Backend/Lims.Infrastructure --startup-project Backend/Lims.Infrastructure --output artifacts/sql/authentication.sql
```

El script actual crea únicamente `__EFMigrationsHistory`, `auth_sessions` y `auth_refresh_tokens`; referencia `usuarios(id)` por FK. Revíselo y haga backup antes de aplicarlo. No se aplicó a ninguna DB durante este checkpoint.

## WinUI 3 en esta máquina

`dotnet run` puede compilar y luego fallar solo durante activación empaquetada por el AUMID predeterminado (`Arg_COMException`). Eso no es un fallo de WinUI, .NET ni Windows App SDK.

Use:

```powershell
.\scripts\run-winui.ps1
```

El script compila x64, usa primero `C:\Tools\WinAppCLI\winapp.exe`, prepara un staging temporal y ejecuta `winapp run ... --with-alias`. No use `--detach`: WinApp omite execution alias en ese modo y vuelve a AUMID.

La ejecución fue validada: WinApp registró `Lims.NextGeneration.Desktop` y el host informó `Application started` desde el layout AppX.
