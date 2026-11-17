# Actualización de API: solventes de Intermedia, Curva y AQS

La ventana real de Curva mostraba el aviso «Actualice la API del servidor para seleccionar solvente y agregar una Intermedia como estándar interno», fuera del área visible por el desplazamiento del formulario. El cliente estaba ofreciendo un selector sin opciones porque el servidor no anunciaba compatibilidad de guardado. Consultar solamente el catálogo de Stock no habilita esa capacidad.

La API compilada desde este repositorio devuelve `supportsDilutionAdditives: true` y los solventes activos de `reference_solvents` en las opciones de Intermedia, Curva y AQS. Intermedia conserva la selección en `stock_solvents`; Curva y AQS la conservan en el cálculo histórico JSON. Esta funcionalidad no agrega una migración.

El cliente también recupera el catálogo del endpoint compartido de Stock cuando una API compatible lo omite. Muestra los avisos junto al selector, distingue falta de soporte de catálogo sin solventes activos y conserva las opciones al agregar soluciones.

## Paquete preparado

`artifacts/publish/solvent-selection-api-20261028.zip` contiene la publicación Release de la API para .NET 10, este documento, diagnósticos SQL de solo lectura y un manifiesto SHA256. No contiene `appsettings*.json`, `web.config`, credenciales ni scripts de aplicación de migraciones. No se ha copiado al servidor ni se ha ejecutado SQL en InterDB.

Es la API completa del estado actual del repositorio. Incluye las funciones de preparaciones, alertas y la política de fechas pausada ya existentes. Revisar su compatibilidad con el servidor antes de reemplazar binarios; esta entrega no autoriza aplicar otras migraciones.

## Aplicación en el servidor

1. Ejecutar los diagnósticos SQL incluidos con una conexión de solo lectura y revisar el historial real de migraciones. Se necesitan el catálogo `reference_solvents`, `stock_solvents` y las migraciones previas de preparaciones/Curva/AQS. `20261017031716_AddStockSolventComposition` introduce esas tablas; `20261020045452_AddCommercialSolventComposition` define su estructura actual. Revisar además los diagnósticos de materiales comerciales, alertas y fechas frente al resto de esta API. Si falta una migración o el esquema no coincide, detener la actualización y revisar el cambio por separado.
2. Con autorización para actualizar el servicio, respaldar los binarios actuales y detener el servicio o application pool de IIS que aloja la API.
3. Reemplazar únicamente los archivos por el contenido de `api/`. Conservar la configuración, conexión, secretos y `web.config` existentes del servidor.
4. Reiniciar el servicio. Ante un fallo, restaurar los binarios respaldados y reiniciar; este paquete no cambia el esquema ni los datos por sí mismo.
5. Con una sesión autorizada, comprobar las respuestas GET de `/api/reference-materials/preparations/intermediate/options`, `/curve/options` y `/aqs/options`. Deben anunciar `supportsDilutionAdditives: true` y devolver los solventes activos reales.
6. En LIMS, pulsar «Actualizar orígenes» o reabrir el formulario. Verificar la lista y, con autorización para un registro controlado, comprobar el solvente al guardar y volver a abrir la preparación.

## Evidencia local

195 pruebas del cliente y 51 pruebas de API pasaron. La verificación WinUI con datos ficticios abrió el catálogo con el puntero, eligió Metanol y conservó el solvente al guardar en Intermedia, Curva y AQS. En Curva/AQS también conservó la selección al actualizar orígenes y se capturaron ventanas de 1920×1080 y 1366×768. Evidencia: `artifacts/validation/solvent-selection/verification.json` y `artifacts/validation/solvent-fix/*.trx`.

Estas pruebas no validan el servicio desplegado, la sesión real de laboratorio ni persistencia en InterDB.
