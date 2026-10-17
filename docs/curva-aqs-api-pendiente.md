# Curva y AQS no permiten consultar ni guardar: diagnostico real

Comprobado el 16 de octubre de 2026 contra la API configurada en el escritorio: `http://10.226.248.191:8080/`.

| GET sin credenciales | HTTP |
| --- | --- |
| `/health/live` | 200 |
| `/api/reference-materials/preparations/stock` | 401 |
| `/api/reference-materials/preparations/intermediate` | 401 |
| `/api/reference-materials/preparations/curve` | 404 |
| `/api/reference-materials/preparations/curve/options` | 404 |
| `/api/reference-materials/preparations/aqs` | 404 |
| `/api/reference-materials/preparations/aqs/options` | 404 |

El servidor responde, pero no reconoce las rutas de Curva/AQS que usa el cliente nuevo. No es una validacion de campos del formulario. Los 401 de Stock/Intermedia indican rutas protegidas existentes; no prueban su guardado. Las consultas fueron exclusivamente GET sin token y sin SQL.

## Preparado, sin desplegar

[Paquete de API revisado](../artifacts/publish/curve-aqs-server-reviewed.zip), con instrucciones, binarios Release win-x64, verificacion de migraciones de solo lectura y la migracion existente `20261016092426_AddCurveAndAqsPreparations`.

[Instrucciones revisables](../artifacts/publish/curve-aqs-server/DESPLIEGUE.md).

La publicacion local contiene las rutas nuevas. Su DLL coincide por SHA256 con la compilacion Release. El ZIP excluye appsettings y web.config para preservar la configuracion real del servidor. No se ha desplegado en IIS ni se ha consultado o modificado InterDB. No esta confirmado si la migracion ya esta aplicada; debe comprobarse antes de plantear su aplicacion autorizada.

## Correccion del mensaje

`SolutionGroupApiClient.cs` distingue un 404 vacio/HTML de una preparacion inexistente con error JSON estructurado. El primer caso explica que la API del servidor debe actualizarse para consultar y guardar Curva/AQS. Los errores 401/403/500 no se reclasifican como rutas ausentes. No se muestra una lista vacia ficticia ni se cambian autorizaciones.

Verificacion: solucion compilada con 0 errores y 0 advertencias; 127 pruebas Desktop correctas, incluidas ocho nuevas de compatibilidad de rutas. [Pruebas](../artifacts/validation/curve-aqs-deployment/tests.log), [compilacion](../artifacts/validation/curve-aqs-deployment/build.log), [publicacion](../artifacts/validation/curve-aqs-deployment/publish.log).

La instancia de escritorio abierta conserva el codigo que cargo al iniciar; el nuevo mensaje requiere reiniciar con el binario recompilado. Reiniciar el escritorio no publica las rutas en el servidor: el despliegue sigue siendo necesario.
