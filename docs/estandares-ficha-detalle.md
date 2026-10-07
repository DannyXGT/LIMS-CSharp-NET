# Estándares: tabla completa y ficha técnica

Entrega de la fase visual del 06/10/2026. Se conservaron el editor, API, contratos, permisos, formatters y reglas de negocio existentes. No se ejecutaron scripts SQL ni se crearon migraciones.

| Solicitud | Resultado |
| --- | --- |
| A. Panel lateral | Eliminado de la página, junto con el layout alternativo de detalle inferior. La superficie completa contiene la tabla. |
| B. Interacción | Clic simple selecciona; doble clic y Enter abren la ficha. Tooltip, ayuda en el footer, foco de teclado y selección lateral fina. |
| C. Detalle | `ReferenceMaterialDetailDialog`, centrado usando el tamaño cliente del `XamlRoot`, con overlay oscuro. |
| D. Resumen | Identidad y acciones arriba; superficie dividida en disponibilidad, vencimiento y estado; información general en tres columnas; vigencia/almacenamiento en dos; auditoría secundaria. Solo Resumen real. |
| E. Disponibilidad | Cantidad y presentación con los formatters actuales. ProgressBar cuando la cantidad y el total producen un porcentaje entre 0 y 100. En la tabla usa el total existente; en la ficha, presentación × unidades. No hay umbrales ni nuevas reglas de inventario. La tabla muestra solo cantidad en anchos menores. |
| F. Acciones | Editar primario, Reemplazar secundario y Archivar danger, con iconos existentes. Se suspende visualmente la ficha mientras opera el editor/confirmación y vuelve al mismo estándar actualizado. Reemplazar conserva la ficha del original. Si el estándar sale del filtro, se cierra. |
| G. Animación | Overlay fade y entrada de ficha con opacity, scale 0.97 y traslación de 8 DIP durante 190 ms. Salida de 120 ms con scale 0.985. Respeta la preferencia de movimiento de Windows mediante Motion. |
| H. Responsive | Máximo 1100 × 760 DIP, limitado por el área cliente con margen. Header/footer fijos y scroll interno. Acciones bajan a una segunda fila cuando el ancho del detalle es menor de 1000 DIP. Tabla con scroll horizontal para conservar columnas. |
| I. Capturas | Capturas de la interfaz WinUI real, ejecutando las fuentes de producción en un harness con fixtures en memoria. El título de la ventana identifica los datos ficticios. |
| J. Validación | Build Debug x64 de solución y harness con 0 errores y 0 advertencias. Suite completa: 141 tests aprobados, 0 fallos, 0 omitidos. 12 comprobaciones de interacción aprobadas. Diff check sin errores. |

## Capturas reales

1. [Tabla completa sin panel](../artifacts/validation/standards-detail/01-tabla-completa.png).
2. [Fila seleccionada](../artifacts/validation/standards-detail/02-fila-seleccionada.png).
3. [Detalle Naphthol AS](../artifacts/validation/standards-detail/03-detalle-naphthol-as.png).
4. [Detalle 4-Aminobiphenyl](../artifacts/validation/standards-detail/04-detalle-4-aminobiphenyl.png).
5. [Detalle en 1366 × 768](../artifacts/validation/standards-detail/05-detalle-1366x768.png).
6. [Disponibilidad](../artifacts/validation/standards-detail/06-disponibilidad.png).
7. [Auditoría](../artifacts/validation/standards-detail/07-auditoria.png).
8. [Estado archivado seguro](../artifacts/validation/standards-detail/08-estado-archivado.png).

[Regreso después de editar](../artifacts/validation/standards-detail/editar-regreso-a-ficha.png), [regreso después de reemplazar](../artifacts/validation/standards-detail/reemplazar-regreso-a-ficha.png), [auditoría de archivo](../artifacts/validation/standards-detail/archivo-auditoria.png).

## Evidencia y reproducción

- [Comprobaciones de interacción](../artifacts/validation/standards-detail/detail-interaction-checks.json): 12 comprobaciones, incluyendo clic simple/doble, Enter/Escape, Tab, apertura repetida, selección rápida, resize, editar, reemplazar, archivar y cierre al salir del filtro Activo.
- Layout medido en [1920 × 1080](../artifacts/validation/standards-detail/layout-1920x1080.json), [1600 × 900](../artifacts/validation/standards-detail/layout-1600x900.json) y [1366 × 768](../artifacts/validation/standards-detail/layout-1366x768.json). El escritorio de validación usa 125 % DPI; las dimensiones de ventana son píxeles y el layout de WinUI usa DIP.
- [Build de la solución](../artifacts/validation/detail-build.log), [build del harness](../artifacts/validation/detail-harness-build.log) y [tests completos](../artifacts/validation/detail-tests.log).
- `git diff --check` sin errores.

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet test Lims.sln --no-build --no-restore -c Debug -p:Platform=x64 -m:1
Set-ExecutionPolicy -Scope Process Bypass -Force
& .\scripts\validate-standards-visual.ps1 -Action Start -EvidenceFolder standards-detail -IsolatedIdentity
& .\scripts\validate-standards-visual.ps1 -Action Command -Command '{"action":"standards"}' -EvidenceFolder standards-detail
& .\scripts\validate-standards-detail.ps1
```

El harness usa sesión, API y operaciones de escritura ficticias en memoria, sin HTTP, PasswordVault ni base de datos. La validación prueba el comportamiento de la UI y el ciclo de actualización, no persistencia en un servidor desplegado. No se implementaron Stock, Preparaciones, Trazabilidad, Consumo, Documentos, Historial ni Dashboard.
