# Ficha de Estándares: pasada visual final

06/10/2026. Cambios de producto limitados a `ReferenceMaterialDetailDialog.xaml` y su presentación/layout en `.xaml.cs`. Se conservaron el CRUD, las acciones, permisos, datos, cálculos, formatters, iconos y tabla principal existentes. No se ejecutaron scripts SQL ni se modificaron PostgreSQL, migraciones, API, contratos, autenticación, catálogos, Nuevo estándar, Sidebar u otros módulos.

| Entrega | Cambio |
| --- | --- |
| A. Espacio | Eliminados los campos repetidos, el footer y el segundo cierre. Las filas reservadas para responsive no agregan gaps en desktop. La altura sigue el contenido real. |
| B. Header | Icono existente de material de referencia, nombre prominente, CAS/catálogo y chips de método/marca. Acciones alineadas: Editar primario, Reemplazar secundario, Archivar danger y X discreta. |
| C. Resumen | Una superficie dividida, con cantidad prominente y “disponibles” debajo; barra de 3 DIP y porcentaje en una misma línea; presentación compacta. Vencimiento e ingreso juntos. Estado mostrado una sola vez. Aproximadamente 21 % menos alto que en la captura anterior. |
| D. Cuerpo | Dos columnas: lote/pureza y almacenamiento. Nombre, CAS, catálogo, método y marca aparecen solo en el header; fechas solo en el resumen. Temperatura mantiene el formatter aprobado, sin agregar unidades. |
| E. Auditoría | Banda inferior plana, con tipografía secundaria, fechas más pequeñas y un divisor sutil. Archivo/reemplazo aparecen solo cuando corresponde. |
| F. Tamaño | La ficha normal mide 1120 × 620 px en el escritorio validado al 125 % DPI. El máximo es 1120 × 720 px, limitado por el área cliente; la altura se ajusta al contenido. El cuerpo cambia a vertical cuando el ancho ya no permite dos columnas. |
| G. Capturas | Ocho capturas WinUI reales con fixtures seguros; cinco muestran regiones tomadas directamente de la ventana renderizada. |
| H. Validación | Builds Debug x64 de solución y harness con 0 errores/advertencias; 141 tests aprobados, 0 fallos/omitidos; 12 comprobaciones de interacción y 8 comprobaciones visuales aprobadas. `git diff --check` sin errores. |

## Comparación real

| Antes | Después |
| --- | --- |
| [Ficha anterior de 4-Aminobiphenyl](../artifacts/validation/standards-detail/04-detalle-4-aminobiphenyl.png) | [Ficha nueva de 4-Aminobiphenyl](../artifacts/validation/standards-detail-polish/01-4-aminobiphenyl-1920x1080.png) |
| 1375 × 950 px al 125 % DPI | 1120 × 620 px al 125 % DPI |
| Propiedades repetidas y auditoría bajo scroll | Identidad arriba y datos operativos/auditoría visibles sin scroll |
| Resumen de aproximadamente 183 px de alto en la captura | Resumen medido de 145 px de alto |

Las dimensiones anteriores provienen del [layout anterior](../artifacts/validation/standards-detail/layout-1920x1080.json); las nuevas del [layout medido actual](../artifacts/validation/standards-detail-polish/layout-4-aminobiphenyl-1920x1080.json). El centrado se comprobó con las coordenadas del área cliente, sin usar el monitor como referencia.

## Ocho capturas solicitadas

1. [4-Aminobiphenyl en 1920 × 1080](../artifacts/validation/standards-detail-polish/01-4-aminobiphenyl-1920x1080.png).
2. [Naphthol AS en 1920 × 1080](../artifacts/validation/standards-detail-polish/02-naphthol-as-1920x1080.png).
3. [4-Aminobiphenyl en 1366 × 768](../artifacts/validation/standards-detail-polish/03-4-aminobiphenyl-1366x768.png).
4. [Resumen de disponibilidad](../artifacts/validation/standards-detail-polish/04-resumen-disponibilidad.png).
5. [Información general](../artifacts/validation/standards-detail-polish/05-informacion-general.png).
6. [Vigencia y almacenamiento](../artifacts/validation/standards-detail-polish/06-vigencia-almacenamiento.png); ingreso/vencimiento aparecen en el resumen superior para evitar repetición.
7. [Auditoría](../artifacts/validation/standards-detail-polish/07-auditoria.png).
8. [Header y acciones](../artifacts/validation/standards-detail-polish/08-header-acciones.png).

También se validaron [1600 × 900](../artifacts/validation/standards-detail-polish/detalle-1600x900.png) y un [layout vertical más estrecho](../artifacts/validation/standards-detail-polish/layout-vertical-1000x800.png), con scroll interno y header fijo.

## Evidencia

- [Build de solución](../artifacts/validation/standards-detail-polish/build.log), [build del harness](../artifacts/validation/standards-detail-polish/harness-build.log) y [tests completos](../artifacts/validation/standards-detail-polish/tests.log).
- [Checks visuales](../artifacts/validation/standards-detail-polish/polish-visual-checks.json): centrado, límites de tamaño, contenido normal sin scroll y reflow vertical con scroll.
- [Checks de interacción](../artifacts/validation/standards-detail-polish/detail-interaction-checks.json): clic/doble clic, Enter/Escape/Tab, aperturas repetidas, selección rápida, resize y acciones.
- [Regreso después de editar](../artifacts/validation/standards-detail-polish/editar-regreso-a-ficha.png), [regreso después de reemplazar](../artifacts/validation/standards-detail-polish/reemplazar-regreso-a-ficha.png) y [auditoría de archivo](../artifacts/validation/standards-detail-polish/archivo-auditoria.png).
- Se mantuvo la apertura de 190 ms con opacity, scale 0.97 y translation 8 DIP, y el cierre de 120 ms. Los [fotogramas inicial](../artifacts/validation/standards-detail-polish/motion-70ms.png) y [final](../artifacts/validation/standards-detail-polish/motion-300ms.png) muestran la transición real; los tiempos de captura están en [motion-frames.json](../artifacts/validation/standards-detail-polish/motion-frames.json). Se respeta la preferencia de animaciones de Windows.
- [Diff contra la ficha anterior](../artifacts/validation/standards-detail-polish/ReferenceMaterialDetailDialog.xaml.cs.diff): solo layout/presentación y medición antes de animar. Los handlers y el flujo de las acciones se conservaron.

Las capturas usan las fuentes reales de la UI en el harness aislado con fixtures en memoria. No prueban persistencia en un backend desplegado. La estructura conserva un único Resumen real y admite futura navegación debajo del header, sin tabs falsas ni nuevas funcionalidades.

## Reproducción

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet test Lims.sln --no-build --no-restore -c Debug -p:Platform=x64 -m:1
Set-ExecutionPolicy -Scope Process Bypass -Force
& .\scripts\validate-standards-visual.ps1 -Action Start -EvidenceFolder standards-detail-polish -IsolatedIdentity
& .\scripts\validate-standards-detail-polish.ps1
& .\scripts\validate-standards-detail.ps1 -EvidenceFolder standards-detail-polish
```
