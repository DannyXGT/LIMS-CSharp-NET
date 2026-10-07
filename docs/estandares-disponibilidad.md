# Disponibilidad de la ficha: corrección visual

06/10/2026. Alcance: únicamente el contenido de Disponibilidad y su actualización visual. El XAML fuera de `AvailabilityCard`, el método `Resize` y los handlers/flujos existentes de acciones permanecen iguales al inicio de esta tarea. Los demás archivos de producto conservan su SHA256. [Comprobación](../artifacts/validation/standards-availability/scope-check.txt).

La imagen de referencia mencionada no fue recibida: el adjunto `2739a105-d37f-433b-acfa-39f026818dc6` contiene solo `Texto pegado.txt`. Se solicitó su ruta. Se siguieron las especificaciones escritas; la comparación lado a lado con esa imagen queda pendiente.

- **Estructura final:** título, cantidad dominante y «disponibles», barra con porcentaje cercano, «[cantidad] disponibles de [total]» y «Presentación: [cantidad por envase] × [unidades]». El componente conserva su superficie y reduce su altura mínima de contenido; no aumenta la altura anterior.
- **ProgressBar:** control nativo de WinUI, altura de 8 DIP tanto en el track como en el indicador y radio de 4 DIP. Se mantienen el cálculo y la validación existentes. El valor visual se anima durante 200 ms al actualizarse, también cuando el editor suspende y luego reabre la ficha. Respeta la preferencia de animaciones del sistema y actualiza los números de inmediato.
- **Icono:** `ReferenceMaterials` existente, a 40 DIP, secundario y a la derecha. Se oculta cuando la tarjeta mide menos de 280 DIP de ancho para conservar el texto y la barra.
- **Colores:** Success del DesignSystem para disponibilidad positiva normal; Warning para el estado real Depleted/Agotado; Danger para los estados existentes de tono Danger, incluido Blocked/Bloqueado. Sin umbrales nuevos. Cero muestra track vacío; datos desconocidos o inválidos conservan el guard existente y no muestran un porcentaje inventado o negativo.
- **Estados probados:** 100 %, 74 %, 25 % y 0 %. También 74.5 mg, 0.025 mg, no informada, porcentaje negativo inválido y Bloqueado. Se verificaron cantidad y porcentaje formateados, valor nativo, proporción del indicador, colores, altura y responsive en 1366×768. Guardado real del editor de fixtures: una llamada, misma ficha abierta, 74 mg y 74 % finales; se conservó una muestra intermedia de 74.208389 durante la animación. [Resultados](../artifacts/validation/standards-availability/availability-checks.json), [transición](../artifacts/validation/standards-availability/animation-in-flight.json), [estado final](../artifacts/validation/standards-availability/availability-after-edit.json).
- **Capturas reales:** tomadas directamente de la ventana WinUI que compila las vistas reales del producto, con fixtures en memoria y sin conexión a producción. No son mockups.
- **Build/tests:** Debug x64 de solución y harness correctos, cero errores y advertencias. Los 141 tests completos pasan (63 Application, 13 API, 19 Infrastructure, 46 Desktop), sin fallos ni omisiones. `git diff --check` devuelve 0. No se ejecutó SQL. [Build](../artifacts/validation/standards-availability/build.log), [tests](../artifacts/validation/standards-availability/tests.log), [harness](../artifacts/validation/standards-availability/harness-build.log).

| Estado | Captura |
| --- | --- |
| 100 % | [Abrir](../artifacts/validation/standards-availability/availability-100.png) |
| 74 % | [Abrir](../artifacts/validation/standards-availability/availability-74.png) |
| 25 % | [Abrir](../artifacts/validation/standards-availability/availability-25.png) |
| 0 % | [Abrir](../artifacts/validation/standards-availability/availability-0.png) |
| 74.5 mg | [Abrir](../artifacts/validation/standards-availability/availability-74-5.png) |
| 0.025 mg | [Abrir](../artifacts/validation/standards-availability/availability-0-025.png) |
| Bloqueado | [Abrir](../artifacts/validation/standards-availability/availability-blocked.png) |
| Ficha completa, 1920×1080 | [Abrir](../artifacts/validation/standards-availability/availability-full-1920x1080.png) |
| 1366×768, vial oculto | [Abrir](../artifacts/validation/standards-availability/availability-1366x768.png) |
| Después del editor | [Abrir](../artifacts/validation/standards-availability/availability-after-edit.png) |

Reproducir: compilar el harness Debug x64, iniciar `scripts/validate-standards-visual.ps1 -Action Start -EvidenceFolder standards-availability -IsolatedIdentity -SkipBuild` y ejecutar `scripts/validate-standards-availability.ps1`. Los comandos `availability` y `nextAvailability` están disponibles solamente en el harness; modifican sus fixtures en memoria, nunca el producto ni su base de datos.
