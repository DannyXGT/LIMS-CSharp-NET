# Carga manual historica y publicacion portable

La carga de estandares ya permite una expiracion pasada. Se desactiva el bloqueo por vencimiento actual al seleccionar, previsualizar y consumir un estandar para registrar Stock historico, incluido un Stock ya vencido. Las fechas y el estado Vencido se conservan.

Interruptor: `HistoricalEntryPolicy.EnforceCurrentExpiration` en `Shared/Lims.Contracts/ReferencePreparations/HistoricalEntryPolicy.cs`. Actualmente devuelve `false`. Para reactivar la validacion, devolver `true` y recompilar/desplegar API y escritorio.

Temporalmente, `HistoricalEntryPolicy.EnforceStockPreparationDates` devuelve `false`: permite cualquier fecha de preparacion y expiracion de Stock, incluida preparacion anterior al ingreso, posterior al vencimiento del estandar, futura o posterior a la expiracion del propio Stock. Las fechas siguen siendo obligatorias y se conservan tal como se ingresan. Para reactivar las comparaciones, devolver `true` y recompilar/desplegar API y escritorio.

La base de datos tambien debe recibir la migracion `AllowHistoricalStockDates` (SQL: `artifacts/sql/historical_stock_dates.sql`) antes de usar esta API; la restriccion anterior rechaza Stock preparado despues del vencimiento del estandar incluso si el boton esta habilitado. El cambio solo flexibiliza fechas de Stock y conserva la cronologia de los demas tipos y las marcas de auditoria. La migracion inversa valida los datos existentes y falla si hay registros incompatibles; no elimina ni corrige registros historicos automaticamente.

Registrar las fechas originales del Excel. Siguen siendo obligatorios el peso tomado, los saldos suficientes, los catalogos y permisos. No se habilitan estandares archivados/bloqueados/agotados. El consumo y su movimiento siguen registrandose. No se modifican validaciones de Intermedia ni se ejecutan migraciones o SQL en InterDB.

## Portable Windows x64: un solo EXE

Desde la raiz de LIMS-CSharp-NET:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

Comando directo equivalente para publicar:

```powershell
dotnet publish .\Frontend\Lims.Desktop\Lims.Desktop.csproj -c Release -r win-x64 --self-contained true -m:1 -p:Platform=x64 -p:PortableSingleFile=true -o .\artifacts\publish\LIMS-singlefile-win-x64
```

El script incrementa automaticamente la version de `scripts/portable-version.txt` y deja el archivo distribuible en `artifacts/publish/Lims.Desktop-<version>.exe` (por ejemplo, `Lims.Desktop-1.0.6.exe`). El contador se actualiza solamente si la publicacion y la copia terminan correctamente. Las versiones anteriores se conservan; un EXE de version existente no se sobrescribe. El comando directo no incrementa la version: usar el script para las publicaciones habituales.

Copiar solamente ese EXE y abrirlo. El nombre versionado se establece durante la compilacion mediante `PortableExecutableName`, para que los recursos de WinUI coincidan con el ejecutable. Conservar el nombre generado; renombrar un EXE compilado con otra identidad puede impedir su arranque. No se necesitan DLL, JSON ni un ZIP al lado del EXE. Los simbolos PDB del directorio de compilacion no forman parte de la distribucion.

La configuracion, .NET y Windows App SDK estan incluidos. Al arrancar, el ejecutable extrae sus dependencias automaticamente a la carpeta temporal del usuario, sin un instalador. Requiere Windows compatible y acceso a la API configurada; PostgreSQL y la API siguen siendo servicios externos. La API actualizada todavia debe desplegarse para habilitar el cambio de vencimiento en el servidor.

Referencia oficial: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe

## Verificacion local

### Cierre inmediato en otra computadora

El registro de una computadora afectada con 1.0.8 identifico `0x80070057` en `PermissionsMotion.Enter`: `StopAnimation("Translation")` se ejecutaba sin habilitar/inicializar la propiedad cuando Windows tenia las animaciones desactivadas. La correccion inicializa `Translation` antes de detenerla; tambien protege `Motion.ExitAsync` cuando el elemento no tuvo animacion de entrada. La regresion del arnes visual se ejecuta con el comando `motionRegression` y escribe `motion-regression.json`, sin cambiar las preferencias de Windows. Ese resultado es evidencia de interfaz con fixtures; no demuestra que todos los cierres remotos tengan la misma causa.

Desde la version 1.0.8, el punto de entrada registra las etapas de arranque y las excepciones administradas en `%LOCALAPPDATA%\LIMS\Logs\startup-*.log`. Los errores capturados muestran un aviso con la ruta. Esto no captura necesariamente fallos nativos ni bloqueos anteriores al inicio de .NET.

Para obtener tambien la version de Windows, la arquitectura, el SHA256 del EXE, la salida del host y los eventos de error de Windows, copiar `scripts/diagnose-desktop-startup.ps1` junto al EXE en la computadora afectada y ejecutar alli:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\diagnose-desktop-startup.ps1 -ExePath .\Lims.Desktop-1.0.8.exe
```

El script abre LIMS y guarda los resultados en `%LOCALAPPDATA%\LIMS\Diagnostics\<fecha-hora>`. No instala dependencias, no necesita conexion a la API para recopilar el diagnostico y no cambia configuraciones de seguridad. Compartir `diagnostico.txt`, `stderr.txt` y los `startup-*.log` que existan. Si falla tambien la version 1.0.7, el mismo script acepta su ruta. Una prueba de arranque en la maquina de desarrollo no valida la compatibilidad con las computadoras afectadas.

123 pruebas de Application, 92 de Desktop y 20 de Stock/PostgreSQL aislado aprobadas previamente para la carga historica (235 en total). Para la publicacion de archivo unico se comprobo el arranque copiando solo `Lims.Desktop.exe` a una carpeta vacia: creo la ventana LIMS y cargo WinUI desde la extraccion temporal. No se inicio sesion ni se desplego la API ni se modifico InterDB. Evidencia: `artifacts/validation/singlefile-portable/verification.json`.
