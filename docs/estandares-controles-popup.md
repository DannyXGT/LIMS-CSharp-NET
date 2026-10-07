# Estándares: controles y feedback de interacción

Corrección limitada sobre la V2. Los cambios de esta fase están en DesignSystem y en la integración visual de Estándares. La Shell recibe únicamente un cierre de popup al ocultar una sección, porque cambiar su visibilidad no descarga la página. Se conserva el trabajo local previo.

## Componentes

| Componente | Comportamiento |
| --- | --- |
| `LimsPopupSurface` | Superficie compartida: recursos del tema, borde, radio, padding, elevación y scroll. Los estilos de lista y calendario están centralizados en `LimsControls.xaml`. |
| `LimsSelect` | `ItemsSource`, `SelectedItem`, `SelectedValue`, `SelectedValuePath` (Id por defecto), `SelectedIndex`, placeholder, habilitación y propiedades de automatización. La selección confirmada conserva la instancia del catálogo. |
| `LimsSearchSelect` | Reutiliza el selector y la superficie. Filtrado local, insensible a mayúsculas y acentos, sobre los objetos cargados. Conserva la colección visible y actualiza sus entradas; ninguna dependencia HTTP. |
| `LimsDatePicker` | `DateTimeOffset?` compatible con el editor existente, representación `dd/MM/yyyy`, es-GT, calendario gregoriano y lunes primero. CalendarView mantiene generación de fechas, selección, navegación y automatización. Su plantilla tiene header compacto con ‹ y ›. La conversión existente a DateOnly conserva la fecha local. |

Método se sustituye en el filtro y en el editor común de nuevo, editar y reemplazar. Unidad y Ubicación física usan `LimsSelect`. Fecha de ingreso y expiración usan `LimsDatePicker`. El filtro Estado conserva su control anterior.

El popup se coloca usando `TransformToVisual`, `XamlRoot.Size` y APIs públicas. Se abre debajo si cabe, encima cuando hay más espacio y reduce su altura cuando hace falta. Los selectores usan el ancho del campo; el calendario usa 296 unidades independientes del DPI. El editor limita el borde inferior al formulario para proteger Cancelar/Guardar. El calendario puede ocupar parte del encabezado superior al abrir encima en una ventana pequeña. No usa coordenadas ni campos privados de WinUI.

Un solo popup está activo por XamlRoot. Una capa transparente del mismo Popup procesa el clic fuera y permite animar su cierre. Se cierra por selección, Escape, salida del foco, apertura de otro selector, guardado, cierre del editor, navegación, scroll del formulario y cambio de tamaño. Los cierres de propietario/navegación y sustitución por otro popup son inmediatos para retirar el overlay a tiempo.

## Movimiento

Apertura: opacity 0→1, translation Y 6→0 y scale 0.98→1, 170 ms. Cierre de interacción: opacity 1→0 y scale 1→0.99, 105 ms. Una nueva operación invalida la anterior y sustituye sus animaciones de Composition. Tab y cierre de propietario retiran el popup inmediatamente para transferir el foco.

Pressed de Nuevo, Guardar, Cancelar, Aplicar, Editar, Reemplazar y Archivar: 0.965 en 70 ms; release en 110 ms, easing cúbico suave. Se captan eventos ya manejados por Button. Los secundarios de Estándares tienen un estilo específico; la escala previa de los botones secundarios de la Shell se conserva. No se modifica el layout.

`UISettings.AnimationsEnabled` se consulta al ejecutar las animaciones. Sin movimiento, las superficies usan un fade de 60 ms y se elimina scale/translation. Los días no tienen animaciones individuales.

## Evidencia real

El banco compila las vistas, code-behind, ViewModels y controles reales. Usa una sesión Administrador y API ficticias, sin el arranque, HTTP o autenticación de producción. Las capturas se obtienen de su HWND con `PrintWindow`, incluso si otra aplicación ocupa el escritorio. Son ventanas WinUI renderizadas, sin mockups ni retoques. Las dimensiones externas son 1920×1080 y 1366×768, con DPI del sistema (125%).

| Caso | Captura |
| --- | --- |
| Página 1920×1080 | [01-standards-1920.png](../artifacts/validation/standards-controls/01-standards-1920.png) |
| Método del filtro | [02-method-filter-1920.png](../artifacts/validation/standards-controls/02-method-filter-1920.png) |
| Método del editor | [04-method-editor-1920.png](../artifacts/validation/standards-controls/04-method-editor-1920.png) |
| Unidad | [05-unit-1920.png](../artifacts/validation/standards-controls/05-unit-1920.png) |
| Ubicación | [06-location-1920.png](../artifacts/validation/standards-controls/06-location-1920.png) |
| Fecha de ingreso | [07-received-date-1920.png](../artifacts/validation/standards-controls/07-received-date-1920.png) |
| Fecha de expiración | [08-expiration-date-1920.png](../artifacts/validation/standards-controls/08-expiration-date-1920.png) |
| Guardar pressed | [Guardar estándar.png](../artifacts/validation/standards-controls/Guardar%20est%C3%A1ndar.png) |
| Cancelar pressed | [Cancelar.png](../artifacts/validation/standards-controls/Cancelar.png) |
| Nuevo pressed | [Nuevo estándar.png](../artifacts/validation/standards-controls/Nuevo%20est%C3%A1ndar.png) |
| Archivar pressed | [Archivar.png](../artifacts/validation/standards-controls/Archivar.png) |
| Página 1366×768 | [09-standards-1366.png](../artifacts/validation/standards-controls/09-standards-1366.png) |
| Método filtro / editor a 1366×768 | [10-method-filter-1366.png](../artifacts/validation/standards-controls/10-method-filter-1366.png), [11-method-editor-1366.png](../artifacts/validation/standards-controls/11-method-editor-1366.png) |
| Unidad / Ubicación a 1366×768 | [12-unit-1366.png](../artifacts/validation/standards-controls/12-unit-1366.png), [13-location-1366.png](../artifacts/validation/standards-controls/13-location-1366.png) |
| Calendario a 1366×768 | [14-date-1366.png](../artifacts/validation/standards-controls/14-date-1366.png) |
| Borde inferior y derecho, control real en el banco | [15-popup-bottom-right-1366.png](../artifacts/validation/standards-controls/15-popup-bottom-right-1366.png) |

Los archivos se guardan en `artifacts/validation/standards-controls/`, ignorado por Git. Las capturas pressed utilizan un descenso real del botón del ratón; se cancelan los diálogos que puedan abrirse al soltarlo. Archivar no se confirma. El guardado de validación sólo crea un registro ficticio en memoria.

## Validación

Build completo Debug x64: 0 errores y 0 advertencias. Tests completos: 141/141 (63 Application, 13 API, 19 Infrastructure y 46 Desktop), incluidos nueve casos nuevos de filtrado, identidad, colocación y movimiento reducido. `git diff --check` correcto.

El banco verifica selección por ID, filtro local y vacío, preservación de selección, doce reaperturas, nueva apertura durante cierre, popup único, salida del foco y cierre por propietario. Las pruebas con teclado real verifican búsqueda/Down/Enter, End/Enter, fecha con flecha/Enter, Escape, Tab y Shift+Tab. Los reportes JSON guardan el foco y los estados de popup. Se verifica además que guardar retira el popup, produce una sola llamada ficticia y cierra el editor, y que navegar lo retira.

Reproducción:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-standards-visual.ps1 -Action Start
Set-ExecutionPolicy -Scope Process Bypass
./scripts/validate-standards-visual.ps1 -Action Command -Command '{"action":"standards"}'
./scripts/validate-standards-visual.ps1 -Action Command -Command '{"action":"editor"}'
./scripts/validate-standards-visual.ps1 -Action Command -Command '{"action":"checks"}'
./scripts/validate-standards-visual.ps1 -Action Command -Command '{"action":"popup","field":"MethodBox","open":true}'
./scripts/validate-standards-visual.ps1 -Action Capture -Name ejemplo
./scripts/validate-standards-visual.ps1 -Action Command -Command '{"action":"exit"}'
```

Requiere WinAppCLI y una sesión interactiva de Windows. El script verifica el proceso que posee el foco antes de enviar teclas o pulsaciones.

## Limitaciones comprobables

En el filtro se ven aproximadamente ocho opciones; dentro del editor la altura adaptativa puede reducirlas a seis para proteger el footer. El calendario puede cubrir parte del título al abrir encima en 1366×768. No se modificó la configuración global de movimiento de Windows: la política reducida tiene tests, pero no una captura con el ajuste del sistema desactivado. Narrador no se probó; sí se inspeccionó la automatización de los controles. La evidencia usa catálogos ficticios y no demuestra persistencia ni acceso al servicio desplegado. Esta fase no cambia SQL, migraciones, API, permisos, autenticación, CRUD, tabla, detalle ni otros módulos.

Referencia pública: [CalendarView de Windows App SDK](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.calendarview).
