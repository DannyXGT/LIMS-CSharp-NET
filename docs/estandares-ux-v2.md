# Estándares — pulido visual y UX V2

Implementación y validación local del 5 de octubre de 2026. El cambio se limita a WinUI, el DesignSystem y estado de presentación. Se conservan contratos, reglas, permisos, autenticación, catálogos y operaciones CRUD existentes. La evidencia usa exclusivamente fixtures identificados como datos ficticios; no acredita operaciones persistentes contra el servicio del laboratorio.

**A. Problemas visuales encontrados.** Había estilos duplicados, controles que caían en plantillas antiguas de WinUI, un indicador de navegación demasiado prominente, iconos con significado incorrecto, popups pesados y una tabla que recibía ancho ilimitado del scroll horizontal. Al cambiar de fila se vaciaba el detalle y se movía el layout. La confirmación de archivo conservaba el tratamiento nativo de botón por defecto; durante un guardado iniciado por automatización podía quedar abierto un calendario.

**B. Problemas corregidos.** Los estilos reutilizables están en `LimsControls.xaml`, con bases Fluent explícitas para los controles nativos. La tabla recibe un ancho medido y conserva un mínimo desplazable; la paginación queda fija. El detalle anterior permanece durante la consulta y sus acciones se deshabilitan hasta coincidir con la selección vigente. Dos pruebas cubren esa transición y su fallo. Los diálogos toman el tema efectivo de la página y los popups de campos se cierran antes de deshabilitar el editor al guardar.

**C. Sidebar.** Cada item tiene un indicador local de 2 DIP × 16 DIP, sin el indicador global que viaja entre items. La columna del icono conserva 40 DIP. Se ocultan agrupación, etiquetas y acción lateral al colapsar; permanece el avatar con tooltip y menú. La selección se aplica inmediatamente. La apertura/cierre del panel conserva el mecanismo nativo; etiquetas entrantes usan fade y desplazamiento de 3 DIP durante 180 ms.

**D. Iconografía.** Los iconos propios usan Segoe Fluent Icons y un estilo compartido. Editar usa E70F, reemplazar E8AB, archivar E7B8, limpiar E711, navegación E80F/E8F1 y salir F3B1. Se corrigieron los glyphs de reemplazo y salida y se quitó el check de guardar. Los controles nativos conservan sus iconos Fluent. Los significados se contrastaron con el [catálogo oficial de Microsoft](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font).

**E. Tabla.** Se conservan las ocho columnas. Header compacto, filas de 52 DIP, selección y hover sobrios, indicador nativo fino, truncamiento y tooltips con valores completos. Cada fila tiene un nombre accesible humano. Se conservan `98.09 %`, `100 mg`, `06/05/2029`, StatusBadge y la paginación humana. No se añadieron cálculos de disponibilidad.

**F. Detalle.** Identificación y estado preceden a acciones, datos principales, vigencia, almacenamiento, disponibilidad y auditoría. Separadores y jerarquía tipográfica sustituyen tarjetas anidadas. El scroll tiene espacio respecto al borde. Archivar usa danger; editar y reemplazar usan controles normales/secundarios. La entrada del contenido usa fade y TranslationX de 8 DIP durante 140 ms.

**G. Editor.** Se conserva una ficha continua: nombre a todo el ancho, tres columnas donde corresponde, fechas y almacenamiento distribuidos sin bloques pesados. Inputs de 36 DIP, labels compactos, footer a la derecha y decimales/entero en TextBox. El label es exactamente “Temperatura de almacenamiento” y el placeholder “Ej. 4 °C, -20 °C, T ambiente o 2–8 °C”. Guardar muestra un ProgressRing pequeño y “Guardando...”, deshabilita campos y acciones y evita doble envío. Se mantienen validaciones y errores sanitizados con referencia interna copiable.

**H. ComboBox.** Se usa el catálogo ya suministrado por la API. Popup limitado a 300 DIP, items compactos, borde y selección del tema. `IsTextSearchEnabled` mantiene la búsqueda incremental nativa por teclado. No se añadió un campo de búsqueda ni se hardcodearon los métodos de producción. Los 23 métodos del banco visual son fixtures aislados.

**I. CalendarDatePicker.** Base Fluent explícita y CalendarView con tipografía de 14 DIP, header compacto, fondos transparentes por día, selección fina, borde y radio coherentes. Cultura `es-GT` y formato `dd/MM/yyyy`. Las conversiones existentes a DateOnly permanecen sin conversión de fecha de negocio a UTC. Se usaron propiedades públicas de [CalendarView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.calendarview).

**J. Botones.** Primary, Secondary, Danger e IconButton comparten altura, radio, padding, foco del sistema y estados. Hover de 90 ms y pressed mediante escala de 0.98 en el compositor. Los diálogos de motivo usan Cancelar secundario, sin convertirlo en un botón accent por defecto. Archivar conserva su acción destructiva y exige el motivo existente.

**K. Animaciones.** `Motion` centraliza entradas de 100–180 ms, salida del contenido del editor de 100 ms y pressed de 90 ms. La navegación entrante usa 150 ms/5 DIP; la saliente tiene ocultación implícita de 90 ms/−3 DIP. Los nuevos inicios reemplazan animaciones de las mismas propiedades, sin una cola de Storyboards. Badges y errores inline tienen entradas cortas. Con `UISettings.AnimationsEnabled` desactivado, las animaciones propias eliminan escala/translation y usan fade de 60 ms, siguiendo la [preferencia del sistema](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-tailoring).

**L. Accesibilidad.** Nombres humanos en campos, botones, filas y paginación; iconos decorativos fuera del árbol relevante; errores con live setting polite; foco visible y controles nativos de teclado conservados. La inspección UI Automation y el recorrido de 16 paradas de Tab verificaron nombres, fechas, combos y footer. Se ejercitaron Shift+Tab, Escape, Enter, Space y flechas en controles nativos. No se hizo una sesión de Narrator ni una auditoría de alto contraste.

**M. Responsive.** Se comprobaron ventanas de 1920×1080, 1600×900, 1366×768 y 1100×800 a 120 DPI (125 %). En las cuatro, el contenido del editor medido es 395.2 DIP y el viewport coincide con su extensión: no necesita scroll en el estado normal. El detalle pasa debajo de la tabla cuando la página mide menos de 960 DIP; los filtros se redistribuyen bajo 800 DIP. La tabla conserva scroll horizontal cuando faltan columnas visibles. Las pruebas redimensionan una ventana sobre un monitor de 1920×1080; no cambian la resolución del monitor.

**N. Rendimiento.** Las animaciones propias modifican propiedades del compositor, sin animar anchos repetidamente. No se añadieron `.Wait()`, `.Result` ni `Thread.Sleep` en producción. El indicador de carga espera 200 ms para evitar flicker; con contenido existente usa una barra fina y en una carga sin filas utiliza placeholder. La última selección prevaleció después de siete cambios rápidos con consulta demorada. No se midieron FPS, uso de GPU ni tiempos percentiles: no se afirma rendimiento de 60 FPS medido.

**O. Archivos modificados.**

| Área | Archivos |
| --- | --- |
| DesignSystem | `Themes/LimsControls.xaml` (nuevo), `Presentation/Motion.cs` (nuevo), `Controls/StatusBadge.xaml.cs` |
| Desktop | `App.xaml`, `Views/ShellPage.xaml` y `.xaml.cs`, `Views/ReferenceMaterialsPage.xaml` y `.xaml.cs`, `Views/ReferenceMaterialEditorDialog.xaml` y `.xaml.cs`, `ViewModels/ReferenceMaterialsViewModel.cs` |
| Pruebas | `Tests/Frontend/Lims.Desktop.Tests/ReferenceMaterialsViewModelTests.cs`; proyecto aislado `Tests/Frontend/Lims.Desktop.VisualHarness` con App, ValidationApp, Fixtures y manifest propios |
| Verificación | `scripts/validate-standards-visual.ps1`; este informe |

**P. Resultado build/tests.** Build completo Debug x64 y build del banco visual: 0 advertencias y 0 errores. Suite completa: 132/132, sin omitidos (Application 63, API 13, Infrastructure 19, Desktop 37). `git diff --check` limpio. Comandos:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet test Lims.sln --no-build -c Debug -p:Platform=x64 -m:1
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
git diff --check
```

Se ejercitaron navegación y sidebar repetidos, selección rápida, popups repetidos, apertura/cancelación de nuevo/editar/reemplazar, archivo/cancelar, paginación, validación y doble guardado. El doble intento produjo una sola llamada a la API ficticia. Archivo y reemplazo se cancelaron; no se ejecutaron operaciones destructivas contra un servicio real. El banco compila los XAML, code-behind y ViewModels reales mediante enlaces, sin incluir startup, DI, HTTP ni autenticación de producción.

**Q. Limitaciones reales.** Los tiempos y la colocación interna de los popups, el cambio de mes, el overlay del ContentDialog y las transiciones de filas siguen a cargo de WinUI; no se reemplazaron sus plantillas completas ni se accedió a partes privadas para imponer duraciones. El estilo nativo del calendario configura `ShouldConstrainToRootBounds=False`: su flyout puede salir del rectángulo de una ventana pequeña y sigue visible en el escritorio. Por eso las capturas limitadas a HWND se complementan con capturas completas del calendario propiedad del proceso de fixtures. El detalle conserva la vista anterior y anima la nueva; no duplica dos árboles visuales para un crossfade simultáneo. El tema claro se verifica como preview del DesignSystem: la ventana de producción continúa configurada en Dark y no se agregó un selector de tema. La ruta de movimiento reducido se revisó en código; no se cambió la configuración global del sistema para probarla.

**Evidencia local.** Está en `artifacts/validation/standards-v2/`, ignorada por Git. La captura se limita a la ventana del proceso de fixtures o a su CalendarView visible. DWM excluye los bordes invisibles: una ventana externa de 1920×1080 produce una imagen visible de 1904×1072. Las imágenes no son mockups generados.

| Evidencia | Archivo |
| --- | --- |
| 1920×1080, sidebar expandido y detalle seleccionado | `01-standards-1920.png` |
| 1366×768 | `standards-1366.png` |
| 1600×900 / 1100×800 | `standards-1600.png`, `standards-1100.png` |
| Sidebar colapsado | `02-sidebar-collapsed.png` |
| Nuevo estándar | `03-editor-1920.png`, `editor-1600.png`, `editor-1366.png`, `editor-1100.png` |
| ComboBox | `04-combobox-open.png`, `combo-1600.png`, `combo-1366.png`, `combo-1100.png` |
| Calendario | `05-calendar-open.png`, `calendar-1600.png`, `calendar-1366.png`, `calendar-1100.png`; completo: `calendar-popup-1366.png`, `calendar-popup-1100.png` |
| Auditoría y disponibilidad | `06-detail-audit.png` |
| Archivado | `07-archived.png` |
| Tema claro del DesignSystem | `08-light.png` |
| Editar / reemplazar / motivo de archivo | `09-edit.png`, `10-replace.png`, `11-archive-dialog.png` |
| Validación / guardado / paginación / loading | `12-inline-validation.png`, `13-saving.png`, `14-pagination.png`, `16-loading.png` |
| Medidas y teclado | `layout-*.json`, `keyboard.json`, `editor-uia.json`, `save-result.json`, `rapid-interactions.json` |

Para reproducir, compilar/restaurar el banco y ejecutar `scripts/validate-standards-visual.ps1 -Action Start`. Requiere WinAppCLI disponible. `-Action Command -Command '{"action":"standards"}'` muestra la página; `editor`, `combo`, `calendar`, `size`, `report` y `exit` controlan únicamente el banco. `-Action Capture -Name ejemplo` guarda la ventana; `-PopupOnly` captura el calendario completo. Cerrar un diálogo antes de mezclar apertura desde la página y apertura directa del banco. El manifest usa la identidad separada `Lims.VisualValidation` y nunca cambia el arranque de la aplicación de producción.
