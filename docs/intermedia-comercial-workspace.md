# Nueva Intermedia comercial: página de trabajo

La entrada comercial se abre dentro de Preparaciones → Intermedia mediante **Mix comprado**, o desde **Nueva Intermedia → Comercial**. Cancelar restaura la lista existente. El registro interno conserva su formulario.

La captura ocupa aproximadamente 70 % del área y el resumen 30 %. La tabla permite editar celdas, agregar y quitar filas, buscar por nombre o CAS y pegar cuatro columnas de Excel: `Compuesto`, `CAS`, `Concentración`, `Unidad`. El atajo es **Ctrl+Mayús+V**. Tab recorre celdas; arriba/abajo cambian de fila en la misma columna y Enter avanza o agrega una fila al final. La tabla desplaza sus filas conservando el encabezado; el resumen tiene su propio desplazamiento.

Identificación e inventario se pueden contraer para priorizar los componentes. En ventanas estrechas, **Ver resumen / Volver a captura** alterna las dos áreas. La barra inferior permanece fuera de las áreas de desplazamiento.

Se cuentan únicamente analitos válidos. Una fila vacía no se registra; una fila parcialmente completada bloquea el guardado. El pegado se valida completo antes de modificar la composición. Los mensajes por fila aparecen al abandonarla. El resumen muestra las concentraciones ingresadas, el volumen por frasco multiplicado por la cantidad, el saldo y la vigencia. Un ingreso nuevo válido inicia en 100 %; un producto existente conserva su saldo, incluso cuando no está informado.

El ViewModel utiliza `ICommercialPreparationApiClient`, los contratos comerciales y los catálogos existentes. La matriz conserva el identificador del solvente. Se mantiene un inventario por material y se admite asociar un producto ya registrado mediante su ID y versión. El bloqueo durante el envío y la reutilización de la misma solicitud tras una respuesta perdida evitan duplicados. Este rediseño no agrega migraciones, tablas ni cambios en los servicios de cálculo, movimientos, Curva o AQS.

## Evidencia

Las imágenes corresponden a controles WinUI reales y los mismos archivos de página y ViewModel del escritorio, ejecutados en el arnés visual con **datos ficticios y cliente comercial de prueba en memoria**. La cabecera los identifica como DATOS FICTICIOS. No prueban persistencia en InterDB, autenticación real ni despliegue del backend. No se ejecutó SQL en InterDB para este rediseño.

| Estado | 1366 × 768 | 1920 × 1080 |
|---|---|---|
| Vacía | [Captura](../artifacts/validation/intermedia-workspace/empty-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/empty-1920.png) |
| 3 analitos | [Captura](../artifacts/validation/intermedia-workspace/three-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/three-1920.png) |
| 20 analitos | [Captura](../artifacts/validation/intermedia-workspace/twenty-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/twenty-1920.png) |
| Últimas filas | [Captura](../artifacts/validation/intermedia-workspace/scroll-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/scroll-1920.png) |
| Inventario desplegado | [Captura](../artifacts/validation/intermedia-workspace/inventory-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/inventory-1920.png) |
| Composición y vigencia | [Captura](../artifacts/validation/intermedia-workspace/composition-expiry-1366.png) | [Captura](../artifacts/validation/intermedia-workspace/composition-expiry-1920.png) |

También se capturan la [vista estrecha](../artifacts/validation/intermedia-workspace/narrow-capture-960.png), su [resumen](../artifacts/validation/intermedia-workspace/narrow-summary-960.png), el [catálogo de solventes](../artifacts/validation/intermedia-workspace/solvent-catalogue.png) y el [detalle después de guardar](../artifacts/validation/intermedia-workspace/saved-commercial-detail.png).

## Comprobaciones

- Solución: compilación Debug x64 serial, 0 errores y 0 advertencias.
- Escritorio: **139 pruebas correctas**, incluidas ocho del nuevo ViewModel y las pruebas existentes de Intermedia. Se verifican pegados atómicos de 3/20 componentes, filas incompletas, identidad de componentes, inventario único, saldo desconocido, fechas, permisos, doble envío y reintento con la misma solicitud.
- [Resultado de la ejecución visual](../artifacts/validation/intermedia-workspace/validation-result.json): portapapeles real de Windows, conteos, búsquedas, desplazamiento independiente, encabezado fijo, flechas/Tab/Enter, acceso a los campos de producto e inventario, solventes, cancelación, entrada desde el formulario anterior y registro mediante el cliente comercial de prueba.
- Los informes JSON guardan geometría, filas dentro del viewport, foco y solicitudes. Una fila fuera del viewport no se considera visible aunque exista en el árbol de controles. Las capturas se comprueban por sus dimensiones físicas y el informe registra la escala de rasterización.

Para repetir:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet vstest Tests/Frontend/Lims.Desktop.Tests/bin/Debug/net10.0-windows10.0.26100.0/Lims.Desktop.Tests.dll
powershell -ExecutionPolicy Bypass -File scripts/validate-intermedia-workspace.ps1
```

La validación visual requiere una sesión de escritorio de Windows disponible y WinAppCLI. Conserva los formatos recuperables del portapapeles original y los restaura tras cada pegado de prueba.
