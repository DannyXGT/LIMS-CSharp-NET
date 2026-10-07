# Estándares: placeholders, editor y ficha técnica

Implementación visual del 05/10/2026. Se conservan los cambios locales previos, los controles de selección/fecha y la iconografía. Esta fase modifica únicamente DesignSystem, XAML y code-behind de presentación, además de instrumentación del banco visual. No se modifican contratos, ViewModels, permisos, CRUD, API, backend, catálogos ni autenticación. No se ejecuta SQL.

## Corrección del bloqueo del editor — 06/10/2026

La modificación anterior del modal desfasaba el dibujo y las zonas de entrada: el campo Nombre se veía en X=363/Y=311, pero sus límites de automatización aparecían en X=683/Y=514. Las invocaciones de UI Automation utilizadas anteriormente omitían el hit testing del ratón y no detectaron la regresión. Se retiró la plantilla modal personalizada y toda modificación de tamaño, margen o composición de sus partes internas (`LayoutRoot`, `BackgroundElement`, `SmokeLayerBackground`). WinUI vuelve a gestionar el posicionamiento, foco y transiciones del modal. Se mantienen el contenido responsive, los placeholders y la ficha técnica.

Con la corrección, los límites de Nombre son X=363/Y=311 y coinciden con su posición visible. `validate-standards-visual.ps1 -Action Click` usa el ratón físico sin InvokePattern ni foco programático. En el banco WinUI con API y sesión ficticias se verificaron escritura en Nombre/CAS, selección de método/ubicación, apertura de calendario/unidades, Cancelar, Escape, reapertura, entrada y cierre en ventana de 800×700, validación del formulario vacío sin petición y un guardado válido simulado que cierra el editor. Evidencias: `artifacts/validation/standards-modal-native/` (`native-input-bounds.json`, `method-popup.json`, `date-popup.json`, `unit-popup.json`, `location-popup.json`, `after-mouse-cancel.json`, `validation-errors.json`, `save-success.json`, `narrow-native-input.png`, `after-escape.json`). El guardado simulado incluye una espera de 700 ms en la API ficticia.

Build final Debug x64 sin errores/advertencias y 141/141 tests. Se respaldaron los binarios del cliente local, se actualizaron DLL, recursos y XBF y se comprobó su coincidencia con la compilación corregida. El cliente real se reabrió en el login; su arranque se verificó mediante HWND/UI Automation y captura. El formulario corregido todavía requiere comprobación dentro de esa sesión autenticada; no se guardó ningún registro real. Las capturas del editor del 05/10 son históricas y no prueban funcionamiento de entrada.

## A. Causa y corrección de placeholders

La plantilla instalada de WinUI usa `TextControlPlaceholderForeground`, `TextControlPlaceholderForegroundPointerOver`, `TextControlPlaceholderForegroundFocused` y `TextControlPlaceholderForegroundDisabled`. El DesignSystem personalizaba fondos/bordes y el foreground del control, pero no estos recursos: los ejemplos conservaban el brush claro del tema nativo. AutoSuggestBox y la búsqueda interna de LimsSearchSelect usan un TextBox y comparten la misma resolución.

Los controles LimsSelect/LimsSearchSelect/LimsDatePicker tenían otra causa: su TextBlock heredaba el foreground principal del botón y sólo reducía su opacidad a 0.7.

En `LimsControls.xaml`, `LimsPlaceholderBrush` y los recursos nativos apuntan al color semántico existente `LimsTextTertiaryColor`. El estado disabled reduce la opacidad a 0.5. Los ComboBox reciben también sus recursos de placeholder por estado. LimsPopupField aplica `LimsPlaceholderTextStyle` sólo cuando no hay valor y retira ese estilo cuando hay selección o fecha. Los ThemeResource mantienen el comportamiento por tema. No se pone gris el TextBox entero ni se establece un PlaceholderForeground local que impida la resolución de disabled.

Medidas del TextBlock real de la plantilla, en tema oscuro:

| Estado de CAS vacío | Brush efectivo | Opacidad del brush | Foreground del valor |
| --- | --- | --- | --- |
| Normal | `#FF8497AA` | 1 | `#FFF5F7FA` |
| Focused | `#FF8497AA` | 1 | `#FFF5F7FA` |
| Disabled | `#FF8497AA` | 0.5 | `#FFF5F7FA` |

Los selectores sin valor reciben ese gris azulado; las selecciones y fechas existentes permanecen blancas. Su estado disabled conserva la atenuación del template del campo.

## B. Centrado y límites del editor

Nuevo, editar y reemplazar comparten `LimsEditorDialogStyle`. La superficie completa incluye título, formulario y footer y se centra en el ancho/alto de `XamlRoot`, incluyendo el espacio del sidebar. No se consulta el monitor ni se utiliza el ancho de ReferenceMaterialsPage para centrar.

El estilo deriva de `DefaultContentDialogStyle` y conserva la plantilla nativa completa. WinUI gestiona sus partes internas, overlay, foco y transiciones. El código del editor sólo ajusta el tamaño y las filas de su propio contenido.

El formulario tiene ancho máximo de 960 DIP y reduce su ancho según la ventana. En anchos inferiores a 600 DIP del formulario, los grupos externos pasan a una columna. El cambio de XamlRoot actualiza los límites incluso con el diálogo abierto. El footer se mantiene visible; sólo el formulario tiene scroll cuando su extensión supera el viewport. Los márgenes externos pertenecen al template nativo.

Medidas históricas del editor del 05/10, a DPI del sistema, 125%; corresponden a la plantilla retirada y no acreditan la corrección del bloqueo:

| Tamaño externo de ventana | XamlRoot (DIP) | Diferencia respecto al centro X/Y (DIP) | Scroll interno |
| --- | --- | --- | --- |
| 1920×1080 | 1521.6×856 | 0 / -0.4 | No |
| 1600×900 | 1265.6×712 | Dentro de 0.5 DIP | No |
| 1366×768 | 1078.4×606.4 | 0 / 0.4 | No |
| 920×560 | 721.6×440 | -0.4 / -0.4 | Sí: extensión 395.2, viewport 249.6 DIP |

Se redimensionó una ventana sobre el monitor existente; no se cambió la resolución del monitor. Las diferencias inferiores a 0.5 DIP corresponden al redondeo del layout.

## C. Jerarquía del detalle

- Identidad: nombre a 25 DIP, CAS y catálogo en una línea que puede envolver, estado existente visible y método en el encabezado. Si CAS o catálogo no están informados, no se agrega separador vacío.
- Disponibilidad y vencimiento se muestran primero en una superficie compacta compartida. Se usa AvailableQuantity y las representaciones ya existentes de presentación, porcentaje y fecha. No se calculan nuevos estados de vencimiento ni movimientos/consumos.
- Datos principales siguen en dos columnas. Vigencia y almacenamiento comparten separador y tienen grupos densos.
- Auditoría usa tipografía de 12 DIP y foreground secundario; las secciones de baja y reemplazo mantienen sus condiciones de visibilidad.
- El detalle ocupa aproximadamente 35% del ancho disponible, limitado a 340–480 DIP. Se conserva el detalle debajo de la tabla cuando la página mide menos de 960 DIP, y el scroll horizontal existente de la tabla.
- Acciones y resumen se distribuyen según el ancho del panel. El panel sin selección conserva el icono existente y usa un mensaje centrado más compacto.

## D. Movimiento e inline

Detalle: opacity 0→1 y translation X 12→0 DIP en 180 ms. Motion.Enter detiene y sustituye las animaciones anteriores de opacity, translation y scale al cambiar rápidamente de registro.

El contenido del formulario (`EditorSurface`) tiene entrada de 140 ms con escala 0.985→1 y cierre de 100 ms. El cierre libera el modal también si la composición queda suspendida, con un límite de espera de 350 ms. La superficie y el overlay conservan las transiciones nativas. Se conserva la política de movimiento reducido de UISettings.

Errores inline: mantienen LimsDangerBrush, tamaño de 11 DIP y entrada de 110 ms con desplazamiento de 2 DIP. El footer conserva Cancelar y Guardar alineados a la derecha, con 10 DIP entre botones y padding compacto. No se alteran las validaciones ni la operación de guardado.

## E. Capturas y comprobación visual

Estas capturas son ventanas WinUI renderizadas mediante PrintWindow, con las vistas y controles reales compilados en el banco. **La sesión y los datos del banco son ficticios. No son capturas de operaciones con registros reales.** No se generaron ni retocaron imágenes.

| Caso | Evidencia |
| --- | --- |
| Ficha de Naphthol de prueba, ancho 1920 | [01-detail-fixture-1920.png](../artifacts/validation/standards-final/01-detail-fixture-1920.png) |
| Editor vacío 1920, placeholders CAS/presentación/temperatura, centrado | [02-editor-fixture-1920.png](../artifacts/validation/standards-final/02-editor-fixture-1920.png) |
| Editor vacío 1366 | [03-editor-fixture-1366.png](../artifacts/validation/standards-final/03-editor-fixture-1366.png) |
| Errores inline | [04-inline-validation-fixture.png](../artifacts/validation/standards-final/04-inline-validation-fixture.png) |
| Editor 1600 / ventana pequeña | [05-editor-fixture-1600.png](../artifacts/validation/standards-final/05-editor-fixture-1600.png), [06-editor-fixture-small.png](../artifacts/validation/standards-final/06-editor-fixture-small.png) |
| Búsqueda de LimsSearchSelect | [07-search-select-placeholder.png](../artifacts/validation/standards-final/07-search-select-placeholder.png) |
| Panel sin selección | [08-empty-fixture-1920.png](../artifacts/validation/standards-final/08-empty-fixture-1920.png) |
| Editar / reemplazar | [09-edit-fixture.png](../artifacts/validation/standards-final/09-edit-fixture.png), [10-replace-fixture.png](../artifacts/validation/standards-final/10-replace-fixture.png) |
| Auditoría condicional de archivado | [11-archive-audit-fixture.png](../artifacts/validation/standards-final/11-archive-audit-fixture.png) |

Los JSON `presentation-*.json`, `placeholder-focused.json`, `placeholder-disabled.json` y `layout-small.json` guardan medidas y brushes efectivos. StandardsPresentationChecks recorre el árbol visual real usando APIs públicas. Los artefactos de esta fase están en `artifacts/validation/standards-final/`, ignorado por Git.

Se comparó visualmente con las capturas anteriores locales y con la referencia conceptual descrita en la solicitud. En el banco: A) la identidad y el resumen operativos se leen como ficha; B) disponibilidad, vencimiento y estado se reconocen arriba; C) el diálogo está centrado por medición; D) los ejemplos se diferencian de los valores. La imagen de referencia anterior no estaba adjunta a esta solicitud, por lo que no se afirma comparación con ese archivo original.

**Pendiente para cerrar la aceptación solicitada:** capturas de la compilación final en una sesión real, incluyendo Naphthol AS y el registro denominado `4-Aminobiophenyl` en la aplicación. La ventana final se abrió con startup, HTTP y autenticación de producción intactos y quedó en el login. La ventana anterior permitió comprobar la existencia de esos dos registros, pero esas observaciones no sustituyen las capturas finales pedidas. No se extraen credenciales ni tokens y no se introduce un bypass de autenticación.

## F. Build y tests

Build final de solución Debug x64: 0 errores y 0 advertencias. Tests completos: 141/141 (63 Application, 13 API, 19 Infrastructure, 46 Desktop). Build final del banco: 0 errores y 0 advertencias. `git diff --check` correcto.

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet test Lims.sln --no-build -c Debug -p:Platform=x64 -m:1
git diff --check
```

La validación termina en Estándares. No se avanza a Stock, Dashboard ni otro módulo.
