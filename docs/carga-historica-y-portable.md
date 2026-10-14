# Carga manual historica y publicacion portable

La carga de estandares ya permite una expiracion pasada. Se desactiva el bloqueo por vencimiento actual al seleccionar, previsualizar y consumir un estandar para registrar Stock historico, incluido un Stock ya vencido. Las fechas y el estado Vencido se conservan.

Interruptor: `HistoricalEntryPolicy.EnforceCurrentExpiration` en `Shared/Lims.Contracts/ReferencePreparations/HistoricalEntryPolicy.cs`. Actualmente devuelve `false`. Para reactivar la validacion, devolver `true` y recompilar/desplegar API y escritorio.

Registrar las fechas originales del Excel: ingreso <= preparacion <= expiracion del estandar y preparacion <= expiracion del Stock. Siguen siendo obligatorios el peso tomado, los saldos suficientes, los catalogos y permisos. No se habilitan estandares archivados/bloqueados/agotados. El consumo y su movimiento siguen registrandose. No se modifican validaciones de Intermedia ni se ejecutan migraciones o SQL en InterDB.

## Portable Windows x64: un solo EXE

Desde la raiz de LIMS-CSharp-NET:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

Comando directo equivalente para publicar:

```powershell
dotnet publish .\Frontend\Lims.Desktop\Lims.Desktop.csproj -c Release -r win-x64 --self-contained true -m:1 -p:Platform=x64 -p:PortableSingleFile=true -o .\artifacts\publish\LIMS-singlefile-win-x64
```

El script deja el archivo distribuible en `artifacts/publish/Lims.Desktop.exe`. Copiar solamente ese EXE y abrirlo. Conservar el nombre `Lims.Desktop.exe`: WinUI busca el indice de recursos incluido a partir del nombre del ejecutable. No se necesitan DLL, JSON ni un ZIP al lado del EXE. Los simbolos PDB del directorio de compilacion no forman parte de la distribucion.

La configuracion, .NET y Windows App SDK estan incluidos. Al arrancar, el ejecutable extrae sus dependencias automaticamente a la carpeta temporal del usuario, sin un instalador. Requiere Windows compatible y acceso a la API configurada; PostgreSQL y la API siguen siendo servicios externos. La API actualizada todavia debe desplegarse para habilitar el cambio de vencimiento en el servidor.

Referencia oficial: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe

## Verificacion local

123 pruebas de Application, 92 de Desktop y 20 de Stock/PostgreSQL aislado aprobadas previamente para la carga historica (235 en total). Para la publicacion de archivo unico se comprobo el arranque copiando solo `Lims.Desktop.exe` a una carpeta vacia: creo la ventana LIMS y cargo WinUI desde la extraccion temporal. No se inicio sesion ni se desplego la API ni se modifico InterDB. Evidencia: `artifacts/validation/singlefile-portable/verification.json`.
