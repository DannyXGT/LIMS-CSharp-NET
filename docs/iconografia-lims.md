# Iconografía LIMS

Implementación del 5 de octubre de 2026. Componente `LimsIcon` en `Lims.DesignSystem`, catálogo semántico compartido y vectores WinUI. No se incorporan imágenes de producción.

## Componente y recursos

`LimsIcon` admite `Icon`, `Size`, `Foreground`, `IsEnabled`, `IsSelected` e `IsDanger`. Observa el puntero y el estado habilitado del botón propietario; en navegación observa también `NavigationViewItem.IsSelected`. Retira las suscripciones al salir del árbol visual y las restaura al volver a cargar. El estado deshabilitado tiene prioridad sobre peligro y selección. Un `Foreground` explícito conserva el contraste sobre accent; los estados semánticos siguen teniendo prioridad.

`LimsIcon.NavigationIcon` adapta el mismo catálogo a los espacios nativos `NavigationViewItem.Icon`, `MenuFlyoutItem.Icon` y `AutoSuggestBox.QueryIcon`. Se conserva el presenter nativo de navegación y su comportamiento expandido/colapsado.

| Recurso | Tamaño |
|---|---:|
| IconSizeSmall | 16 |
| IconSizeMedium | 20 |
| IconSizeLarge | 24 |
| IconSizeExtraLarge | 28 |
| IconSizeHero | 32 |

`LimsIconNormalBrush`, `LimsIconHoverBrush`, `LimsIconSelectedBrush`, `LimsIconDisabledBrush` y `LimsIconDangerBrush` utilizan los colores existentes del DesignSystem, tanto en Light como en Dark. Las geometrías no contienen colores. Se usa una caja cuadrada centrada, contornos de 1.5 unidades sobre base 24 y escala uniforme explícita: `PathIcon.Data` no se ajusta automáticamente a `Width`/`Height`. El tamaño base 24 conserva la geometría sin transformación adicional.

```xml
<design:LimsIcon Icon="NewStandard" Size="{StaticResource IconSizeSmall}"
                 Foreground="{ThemeResource LimsOnAccentBrush}" />
<NavigationViewItem Content="Estándares" AutomationProperties.Name="Estándares"
                    design:LimsIcon.NavigationIcon="Standards" />
```

Los iconos son decorativos, sin hit testing ni tabulación y con `AccessibilityView="Raw"`. Los nombres humanos permanecen en botones y elementos de navegación. No se anuncian nombres técnicos del catálogo en el árbol normal de automatización.

## Catálogo definitivo

Los nombres y códigos Fluent se verificaron contra el [catálogo oficial de Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font). La familia de todos los `FontIcon` es exclusivamente **Segoe Fluent Icons**.

| Navegación / área | Icon | Vector |
|---|---|---|
| Inicio | Home | Fluent Home · E80F |
| Programación | Scheduling | Fluent Calendar · E787 |
| Resultados | Results | Fluent CheckList · E9D5 |
| Calidad | Quality | Fluent Shield · EA18 |
| Materiales de referencia | ReferenceMaterials | Path propio `Vial`: vial con tapa y nivel |
| Reportes | Reports | Fluent ReportDocument · E9F9 |
| Administración | Administration | Fluent Equalizer · E9E9: controles |
| Cerrar sesión | SignOut | Path propio `DoorArrow`: puerta con flecha de salida |
| Resumen | Summary | Fluent ViewAll · E8A9 |
| Estándares | Standards | Path propio `Vial`, compartido con materiales |
| Preparaciones | Preparations | Path propio `Flask`: matraz simple |
| Alertas | Alerts | Fluent Warning · E7BA |
| Reposición | Replenishment | Path propio `Replenish`: unidad con flecha de entrada |
| Inventario | Inventory | Fluent Package · E7B8 |
| Trazabilidad | Traceability | Fluent History · E81C |
| Documentación | Documentation | Fluent Document · E8A5 |
| Disponibilidad | Availability | Path propio `Available`: unidad con confirmación |

| Acción | Icon | Vector / aplicación |
|---|---|---|
| Nuevo estándar | NewStandard | Path propio `NewVial`: vial + signo más; botón de cabecera a 16 |
| Editar | Edit | Fluent Edit · E70F; botón a 16 |
| Reemplazar | Replace | Path propio `ReplaceArrows`: dos flechas opuestas; botón a 16 |
| Archivar | Archive | Path propio `ArchiveBox`: caja con tapa y ranura; botón a 16, danger |
| Guardar | Save | Fluent Save · E74E disponible; **Guardar estándar permanece sin icono** |
| Cancelar | Cancel | Fluent Cancel · E711 disponible; el editor conserva texto |
| Eliminar | Delete | Fluent Delete · E74D disponible |
| Buscar | Search | Fluent Search · E721; espacio nativo del buscador |
| Filtrar | Filter | Fluent Filter · E71C disponible |
| Aplicar filtros | ApplyFilters | Fluent Filter · E71C disponible; el botón Aplicar conserva texto y composición compacta |
| Limpiar filtros | ClearFilters | Path propio `ClearFunnel`: embudo + cruz; botón a 16 |
| Exportar | Export | Fluent Export · EDE1 disponible |
| Imprimir | Print | Fluent Print · E749 disponible |
| Ver detalle | ViewDetail | Fluent Document · E8A5; estados vacíos a 24 |
| Calendario | Calendar | Fluent Calendar · E787; campos de fecha a 16 |

Auxiliares reutilizables: `ChevronLeft` E76B, `ChevronRight` E76C, `ChevronDown` E70D, `User` E77B, `Lock` E72E, `ShowPassword` E890, `HidePassword` ED1A, `SignIn` E72A y `Error` E783. Se corrige la decoración de Ocultar contraseña: el antiguo E8F5 corresponde a CalendarReply; ED1A corresponde a Hide. Se conserva la acción de mostrar/ocultar existente.

Hay nueve geometrías propias, compartidas por significado y por estado. Los iconos preparados para módulos ausentes sólo existen en el catálogo; el sidebar conserva Inicio y Estándares.

## Archivos de esta fase

- Creados: `Controls/LimsIcon.cs`, `Controls/LimsIconKind.cs`, `Controls/LimsIconCatalog.cs` en `Frontend/Lims.DesignSystem`.
- DesignSystem: `Themes/LimsTheme.xaml` (tamaños/colores), `Themes/LimsControls.xaml` (retiro del estilo antiguo de FontIcon), `Controls/LimsPopupField.cs` y `Controls/LimsDatePicker.cs` (decoración semántica).
- Desktop: `Views/ShellPage.xaml`, `Views/ReferenceMaterialsPage.xaml`, `Views/LoginPage.xaml` y `Views/LoginPage.xaml.cs` (sustituciones de iconos).
- Validación: `Tests/Frontend/Lims.Desktop.VisualHarness/IconValidation.cs`, `Tests/Frontend/Lims.Desktop.VisualHarness/App.xaml.cs` y `scripts/validate-standards-visual.ps1` (catálogo, verificación, hover y capturas de popups).
- Documentación: este archivo.

El checkout ya contenía cambios de UX antes de esta fase. Se conservaron. No se modificaron en esta fase ViewModels, CRUD, API, PostgreSQL, catálogos de datos, permisos ni disposición de las pantallas. El editor mantiene Guardar estándar sin icono.

## Evidencia visual

Capturas reales de ventanas WinUI con las fuentes XAML de producción, ejecutadas en el banco visual existente con usuario Administrador y **datos ficticios**. No son mockups y no acreditan una sesión autenticada contra el servicio desplegado. La geometría de salida se verifica también dentro del MenuFlyout nativo; esa captura incluye la ventana emergente.

| Revisión | Captura |
|---|---|
| A. Sidebar expandido | [01-sidebar-expanded.png](../artifacts/validation/iconography/01-sidebar-expanded.png) |
| B. Sidebar colapsado | [03-sidebar-collapsed.png](../artifacts/validation/iconography/03-sidebar-collapsed.png) |
| C / E. Estándares y Editar/Reemplazar/Archivar | [02-standards-actions.png](../artifacts/validation/iconography/02-standards-actions.png) |
| D. Nuevo estándar y Guardar sin icono | [05-new-standard.png](../artifacts/validation/iconography/05-new-standard.png) |
| F. Botón Nuevo estándar con puntero real | [Nuevo estándar.png](../artifacts/validation/iconography/Nuevo%20est%C3%A1ndar.png) |
| G. Usuario/Cerrar sesión | [04-user-menu.png](../artifacts/validation/iconography/04-user-menu.png) |
| Catálogo completo en cinco tamaños | [07-icon-catalog.png](../artifacts/validation/iconography/07-icon-catalog.png) |
| Estados con hover real | [Hover (puntero).png](../artifacts/validation/iconography/Hover%20(puntero).png) |

La carpeta de evidencia está ignorada por Git. El banco comprueba carga de todos los vectores, cajas cuadradas, escala uniforme de PathIcon, estados y automatización decorativa. Los JSON de accesibilidad y de layout acompañan las capturas. La inspección de automatización no sustituye una sesión con Narrador.

Reproducción: ejecutar `scripts/validate-standards-visual.ps1 -Action Start -EvidenceFolder iconography`, seleccionar Estándares con el comando JSON `{"action":"standards"}`, abrir el catálogo con `{"action":"icons","open":true}` y comprobar con `{"action":"iconChecks"}`. `-Action HoverCapture -Name 'Hover (puntero)'` usa el puntero real; `-Action Capture -IncludePopups` incluye el menú nativo. Requiere la sesión interactiva de Windows.

## Build y tests

Build completo Debug x64: **0 errores, 0 advertencias**. Tests completos: **141/141** (63 Application, 13 API, 19 Infrastructure y 46 Desktop), sin omitidos. `git diff --check`: correcto.

Comprobación real de 210 instancias WinUI: todos los vectores cargan, todas las cajas son cuadradas, las geometrías se escalan uniformemente y la automatización permanece decorativa. En Dark, el mismo vial con más cambia de `#FFB1BDCA` normal a `#FFF5F7FA` con puntero real; selected usa `#FF2A8BF2`, disabled `#FF7F91A1` y danger `#FFFF7A84`, todos procedentes de recursos. En el sidebar colapsado ambas cajas miden 20×20 y comparten centro horizontal 24.4 dentro del Shell. El reporte registra **0 llamadas de guardado**.

El resultado final, los logs `build.log`/`tests.log`, `icon-checks.json` y `sidebar-collapsed-layout.json` quedan en `artifacts/validation/iconography/`.

Comandos de comprobación:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet test Lims.sln --no-build --no-restore -c Debug -p:Platform=x64 -m:1
git diff --check
```
