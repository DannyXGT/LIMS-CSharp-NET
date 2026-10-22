# Preparaciones: creación modal consistente

Intermedia comercial vuelve a abrirse en un `ContentDialog` WinUI, tanto desde «Mix comprado» como desde «Nueva Intermedia → Comercial». El listado permanece detrás del modal y conserva su búsqueda, página y selección al cancelar. El editor existente se reutiliza como contenido del diálogo; no se inserta como página en Preparaciones.

Los tres formularios comparten encabezado, icono, acción Ampliar/Restaurar, controles del Design System, botones y footer fijo. El cuerpo usa columnas y desplazamiento interno. `PreparationEditorHeader` y `CommercialBottleVisual` concentran los elementos compartidos sin duplicar sus XAML.

Stock preparado mantiene el estándar seleccionado destacado y el cálculo fijo a la derecha: peso requerido, peso tomado, concentración real, diferencia y saldo restante. Los datos de preparación, composición de solventes y vigencia se recorren en la columna izquierda. El compuesto conserva su derivación automática desde el estándar. El catálogo existente de Stock por pesada expone sólidos y su contrato no proporciona una lista de analitos múltiples; no se inventaron componentes ni se amplió el contrato.

Stock comercial destaca producto, fabricante, lote, concentración y unidad. Su panel fijo muestra el frasco, volumen inicial, disponible, porcentaje y expiración. No presenta cálculos de pesada ni requiere seleccionar un estándar físico. Catálogo, responsable y concentración nominal alternativa quedan como datos complementarios.

Intermedia conserva la tabla editable, búsqueda por analito/CAS, pegado desde Excel y resumen de composición/disponibilidad. La tabla vacía mide 72 DIP y muestra instrucciones con las acciones Agregar analito y Desde Excel. Con componentes, el alto se limita al espacio del cuerpo; el desplazamiento de la tabla conserva encabezado, resumen y footer.

## Capturas reales a 1366 × 768

Capturas de ventanas WinUI ejecutadas con los mismos controles de producción y datos de prueba explícitos. No son mockups. La validación con datos de prueba fue confirmada por el usuario; no acredita persistencia en la base de datos ni una sesión autenticada de producción.

| Formulario | Vacío | Con datos | Más evidencia |
|---|---|---|---|
| Stock preparado | [Vacío](../artifacts/validation/preparation-dialogs/stock-preparado-vacio-1366.png) | [Estándar y cálculo](../artifacts/validation/preparation-dialogs/stock-preparado-datos-1366.png) | [Solvente y vigencia](../artifacts/validation/preparation-dialogs/stock-preparado-solvente-vigencia-1366.png) · [Validación](../artifacts/validation/preparation-dialogs/stock-preparado-validacion-1366.png) |
| Stock comercial | [Vacío](../artifacts/validation/preparation-dialogs/stock-comercial-vacio-1366.png) | [Producto y frasco](../artifacts/validation/preparation-dialogs/stock-comercial-datos-1366.png) | [Inventario](../artifacts/validation/preparation-dialogs/stock-comercial-inventario-1366.png) · [Validación](../artifacts/validation/preparation-dialogs/stock-comercial-validacion-1366.png) |
| Intermedia comercial | [Vacía](../artifacts/validation/preparation-dialogs/intermedia-vacia-1366.png) | [Tres analitos](../artifacts/validation/preparation-dialogs/intermedia-tres-analitos-1366.png) | [Veinte analitos](../artifacts/validation/preparation-dialogs/intermedia-veinte-analitos-1366.png) · [Analito 20 visible](../artifacts/validation/preparation-dialogs/intermedia-veinte-desplazamiento-1366.png) |

![Stock preparado con estándar y cálculo](../artifacts/validation/preparation-dialogs/stock-preparado-datos-1366.png)

![Stock comercial con concentración e inventario](../artifacts/validation/preparation-dialogs/stock-comercial-datos-1366.png)

![Intermedia comercial con veinte analitos](../artifacts/validation/preparation-dialogs/intermedia-veinte-desplazamiento-1366.png)

## Comprobación

- Compilación Debug `win-x64` de Desktop y del harness sin errores ni advertencias.
- 30 pruebas existentes de `StockViewModelTests` y `CommercialIntermediateViewModelTests` superadas.
- 1366 × 768 y 1920 × 1080 físicos, a escala 125 %: formularios vacíos y con datos, composición, validaciones, búsquedas, popups de catálogos, Excel, cancelación y ambas rutas de apertura de Intermedia.
- Tabla con veinte componentes: analito 20 completamente contenido tanto en el viewport de la tabla como en el área de captura. Encabezado y footer permanecen en su posición al desplazar la tabla.
- Guardados de prueba aislados: consumo de Stock por 10.1 mg y registro de Intermedia con tres componentes. No se escribieron datos de producción.
- Ampliar/Restaurar conserva los veinte componentes y mantiene el footer dentro de la ventana.

[Resultado visual automatizado](../artifacts/validation/preparation-dialogs/validation-result.json) · [Comprobación de ampliación](../artifacts/validation/preparation-dialog-expansion/validation-result.json) · [Compilación Desktop](../artifacts/validation/preparation-dialog-build.log) · [Pruebas](../artifacts/validation/preparation-dialog-tests.log)

| Formulario | 1920 × 1080 |
|---|---|
| Stock preparado | [Vacío](../artifacts/validation/preparation-dialogs/stock-preparado-vacio-1920.png) · [Con datos](../artifacts/validation/preparation-dialogs/stock-preparado-datos-1920.png) |
| Stock comercial | [Vacío](../artifacts/validation/preparation-dialogs/stock-comercial-vacio-1920.png) · [Con datos](../artifacts/validation/preparation-dialogs/stock-comercial-datos-1920.png) |
| Intermedia comercial | [Vacía](../artifacts/validation/preparation-dialogs/intermedia-vacia-1920.png) · [Tres analitos](../artifacts/validation/preparation-dialogs/intermedia-tres-analitos-1920.png) · [Veinte analitos](../artifacts/validation/preparation-dialogs/intermedia-veinte-analitos-1920.png) |

Esta corrección no modifica backend, base de datos, cálculos, contratos, inventarios, consumos, trazabilidad, permisos, Dashboard, Sidebar, Estándares, Curva ni AQS. Se conservaron los cambios previos del workspace.

Repetir la comprobación visual: `powershell -ExecutionPolicy Bypass -File scripts/validate-preparation-dialogs.ps1`. Requiere acceso a la sesión interactiva de Windows.
