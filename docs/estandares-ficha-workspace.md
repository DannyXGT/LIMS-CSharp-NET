# Ficha técnica de estándares: reconstrucción visual

Fecha: 06/10/2026.

Se reconstruyó exclusivamente la composición de `ReferenceMaterialDetailDialog.xaml(.cs)`. La ventana WinUI de validación compila las vistas reales del producto y usa fixtures en memoria, con el título «VALIDACIÓN VISUAL · DATOS FICTICIOS». Las capturas corresponden a esa ventana renderizada; no son mockups ni evidencia de una sesión de producción o de persistencia en PostgreSQL.

**Comparación con referencia pendiente:** el adjunto `aa5ac886-7d07-455c-b654-9284347b3441` contiene únicamente `Texto pegado.txt`. No se recibió la imagen mencionada; se solicitó su ruta. Se implementó la arquitectura explícita del texto, pero no se realizó ni se atribuye una comparación lado a lado con una imagen ausente.

## A–H. Composición

- **A. Layout reemplazado:** se retiraron la barra única de resumen, las dos pequeñas secciones de propiedades y la auditoría como etiquetas aisladas. El contenido se recompuso como una vista de trabajo con panel técnico, tarjetas separadas y tabla de actividad.
- **B. Estructura:** overlay oscuro, marco centrado al 90 % del ancho y 85 % del alto cliente, encabezado fijo, breadcrumb, acciones existentes y una única sección activa «Resumen». Cuerpo 38 % Información General y 62 % módulos/actividad, con desplazamiento interno. No hay footer ni tabs ficticias.
- **C. Identidad:** bloque de 136 DIP con el icono existente de Material de Referencia a 56 DIP y StatusBadge debajo. Nombre a 28 DIP, CAS y catálogo en líneas independientes, chips de marca y método. La X cierra la ficha. Entrada conjunta de 200 ms con escala 0.975 y desplazamiento de 10 DIP; salida de 120 ms y escala 0.985.
- **D. Información General:** diez filas label/valor: nombre, marca, catálogo, CAS, lote, pureza, método, presentación, almacenamiento y ubicación. Valores completos con ajuste de línea; almacenamiento conserva literalmente el texto guardado, sin agregar unidades.
- **E. Disponibilidad:** cantidad existente a 26 DIP, «disponibles de [total]», barra compacta con porcentaje y presentación por envase. Se conservó el helper existente; datos no informados mantienen su tratamiento y no se inventan cantidades.
- **F. Vencimiento:** módulo propio con fecha dd/MM/yyyy e ingreso, sin días restantes.
- **G. Estado:** tarjeta pequeña con el StatusBadge existente. Solo se presenta también debajo del icono, conforme al bloque de identidad solicitado.
- **H. Actividad:** tabla Fecha / Acción / Usuario con creación y última modificación. Archivo y reemplazo muestran únicamente la información que existe en el registro. No se inventan movimientos ni historial.

## I. Responsive

Medidas en DIP, tomadas del XamlRoot cliente con escala real de 125 %. Los tamaños de ventana son físicos.

| Ventana | Ficha DIP | Ancho / alto cliente | Cuerpo |
| --- | --- | --- | --- |
| 1920 × 1080 | 1369.6 × 727.2 | 90.0% / 85.0% | Dos columnas |
| 1600 × 900 | 1139.2 × 605.6 | 90.0% / 85.1% | Dos columnas |
| 1366 × 768 | 970.4 × 515.2 | 90.0% / 85.0% | Dos columnas |
| 1000 × 800 | 707.2 × 537.6 | 90.0% / 85.1% | Apilado |

En 1920×1080 la ficha completa de los dos materiales entra sin scroll. En 1600×900 y 1366×768 se mantienen las columnas; Estado se coloca debajo de Vencimiento y el cuerpo puede desplazarse. En 1000×800 el panel técnico pasa arriba y los módulos/actividad debajo. El encabezado permanece fijo. La captura inferior de 1366×768 confirma acceso a almacenamiento y ambas filas de auditoría.

## J. Capturas reales

| Captura | Archivo |
| --- | --- |
| 4-Aminobiphenyl 1920×1080 | [Abrir](../artifacts/validation/standards-detail-workspace/01-4-aminobiphenyl-1920x1080.png) |
| Naphthol AS 1920×1080 | [Abrir](../artifacts/validation/standards-detail-workspace/02-naphthol-as-1920x1080.png) |
| 1600×900 | [Abrir](../artifacts/validation/standards-detail-workspace/detalle-1600x900.png) |
| 1366×768 | [Abrir](../artifacts/validation/standards-detail-workspace/03-4-aminobiphenyl-1366x768.png) |
| 1366×768, parte inferior | [Abrir](../artifacts/validation/standards-detail-workspace/1366x768-parte-inferior.png) |
| Información General | [Abrir](../artifacts/validation/standards-detail-workspace/05-informacion-general.png) |
| Disponibilidad / Vencimiento / Estado | [Abrir](../artifacts/validation/standards-detail-workspace/04-disponibilidad-vencimiento-estado.png) |
| Disponibilidad | [Abrir](../artifacts/validation/standards-detail-workspace/06-disponibilidad.png) |
| Auditoría | [Abrir](../artifacts/validation/standards-detail-workspace/07-auditoria.png) |
| Header completo | [Abrir](../artifacts/validation/standards-detail-workspace/08-header-acciones.png) |
| Cuerpo apilado, 1000×800 | [Abrir](../artifacts/validation/standards-detail-workspace/layout-vertical-1000x800.png) |

## K. Validación

- `dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1`: correcto, cero errores y advertencias. [Log](../artifacts/validation/standards-detail-workspace/build.log).
- `dotnet test Lims.sln --no-build --no-restore -c Debug -p:Platform=x64 -m:1`: 141 pruebas aprobadas, cero fallos y omisiones (63 Application, 13 API, 19 Infrastructure, 46 Desktop). [Log](../artifacts/validation/standards-detail-workspace/tests.log).
- Harness Debug x64: correcto, cero errores y advertencias. [Log](../artifacts/validation/standards-detail-workspace/harness-build.log).
- Doce comprobaciones de interacción con fixtures: clic simple, doble clic real, Enter, Escape, foco, tabulación, aperturas repetidas, selección rápida, tamaños y los tres flujos existentes. Editar guarda una vez y actualiza la ficha abierta; Reemplazar vuelve al original; Archivar actualiza la auditoría o cierra si el registro sale del filtro Activos. [Resultados](../artifacts/validation/standards-detail-workspace/detail-interaction-checks.json).
- Ocho comprobaciones visuales de centrado, proporciones, contenido completo y adaptación. [Resultados](../artifacts/validation/standards-detail-workspace/workspace-visual-checks.json).
- `git diff --check`: salida 0. [Log](../artifacts/validation/standards-detail-workspace/diff-check.log).
- Snapshot SHA256: cero cambios durante esta tarea en los otros archivos de producto de Frontend, Backend y Shared. Se preservó el trabajo previo del repositorio. `RunActionAsync` conserva exactamente su implementación anterior. No se ejecutó SQL ni se cambiaron contratos, API, permisos, CRUD o módulos adicionales.

Para repetir las capturas, compilar el harness, iniciar `scripts/validate-standards-visual.ps1 -Action Start -EvidenceFolder standards-detail-workspace -IsolatedIdentity -SkipBuild`, ejecutar `scripts/validate-standards-detail-workspace.ps1`, y después `scripts/validate-standards-detail.ps1 -EvidenceFolder standards-detail-workspace` para las interacciones. Estas últimas modifican únicamente fixtures y generan capturas completas de actividad; para conservar los recortes finales, reiniciar el harness y repetir el script visual al terminar.
