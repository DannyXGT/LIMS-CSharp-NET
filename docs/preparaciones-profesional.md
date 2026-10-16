# Preparaciones: mejora de presentación y consultas

Implementación del 16 de octubre de 2026, limitada a Stock e Intermedia.

## Resultado

- Encabezado e identidad existentes de LIMS conservados. La creación usa la acción autorizada del encabezado, sin duplicación en el vacío.
- Pestañas nativas existentes, sin nueva identidad visual, conservando búsqueda, página y selección por pestaña.
- Barra compacta adaptable, búsqueda automática con espera de 300 ms, filtros inmediatos de vigencia y volumen, método por texto y restablecimiento común. Stock busca código, nombre, estándar y lote; Intermedia busca código, nombre y método, conforme a sus consultas existentes.
- Filtros aplicados en el servidor antes de contar y paginar. El cliente exige `FiltersApplied` cuando se usan los nuevos filtros; una API anterior muestra una explicación en vez de simular resultados filtrados.
- Tabla con código/nombre/vigencia, origen/lote o método/componentes, concentración/aforo, saldo/porcentaje/barra y fechas. El responsable y el peso consumido del estándar permanecen en la ficha. Vigencia visible junto al código incluso en ventanas estrechas.
- Vacío compacto, ausencia de coincidencias, carga y error diferenciados; reintento y referencia de error seleccionable. La acción de detalle es contextual. Se conservan selección, teclado, Enter y doble clic.
- Ficha Stock sin el resumen duplicado; mantiene origen histórico, cálculos, consumo del estándar, fechas, almacenamiento y auditoría. Ambas fichas muestran consumos existentes hacia preparaciones derivadas.
- Se reutilizan estilos, controles y movimiento del DesignSystem. No se cambia el sidebar, login, topbar, dashboard, Estándares, permisos ni la paleta.

## Disponibilidad y trazabilidad

La fuente es `ReferencePreparation.AvailableVolume`, en la unidad del aforo. El peso del estándar sigue siendo un movimiento distinto. El porcentaje y el consumo mostrado se derivan del saldo persistido usando la presentación existente, sin cambiar su cálculo ni redondeos químicos.

Se reutilizan los umbrales visuales existentes: información por encima del 60 %, aviso entre 25 y 60 %, peligro por debajo del 25 %. El filtro bajo considera saldo positivo hasta el 60 %. Vigencia e inventario se presentan por separado: una solución vencida conserva su volumen visible y su distintivo rojo; un saldo ausente nunca se supone completo. No se añadieron estados persistidos ni un umbral nuevo para «próximo a vencer».

Las consultas de ficha leen los débitos inmutables `IntermediateConsumption` existentes, con destino, volumen/unidad, saldo posterior, usuario y fecha. No recalculan ni sustituyen el saldo. Un servidor anterior que no entrega historial se distingue de uno que entrega historial vacío. No se implementó otro motor de consumos, correcciones o reversiones.

## Archivos de implementación

- `Frontend/Lims.Desktop/Views/PreparationsPage.xaml[.cs]`, `StockPage.xaml[.cs]`, `IntermediatePage.xaml[.cs]`.
- `StockDetailDialog.xaml[.cs]`, `IntermediateDetailDialog.xaml[.cs]`; ajuste localizado de la visibilidad del botón de Preparaciones en `ShellPage.xaml.cs`.
- `Frontend/Lims.Desktop/ViewModels/StockViewModel.cs`, `IntermediateViewModel.cs` y sus clientes/interfaces HTTP.
- `Shared/Lims.Contracts/ReferencePreparations/StockContracts.cs`, `IntermediateContracts.cs`, `PreparationListFilter.cs`, `PreparationUsage.cs`: extensiones de lectura, sin cambios a solicitudes de escritura.
- Servicios/interfaces y endpoints Stock/Intermedia: traslado y validación de filtros de lectura. Repositorios Stock/Intermedia y `PreparationListQuery.cs`: filtrado y consulta del historial existente.
- Pruebas afectadas de presentación, HTTP, ViewModel, API y persistencia; fixtures visuales y scripts de validación.

Los cambios previos del árbol de trabajo se conservaron. No se modificaron `StockCalculator`, `StockUnits`, `IntermediateCalculator`, entidades, escritura transaccional, descuento del estándar ni migraciones del proyecto.

## Validación y límites

- Debug x64: ejecutable y harness WinUI compilados con 0 errores y 0 advertencias. Se ejecutó también el build del destino real `bin/Debug/.../win-x64`.
- Pruebas: 130 de aplicación, 39 de API, 108 de escritorio y 56 de infraestructura; 333 correctas, ninguna omitida. TRX en `artifacts/validation/preparations-tests/`.
- Persistencia probada solo en `127.0.0.1:55439/lims_stock_test`, con el fixture aislado existente. Las pruebas verifican conteo antes de paginar, filtros reales, saldo separado de masa y relación con preparaciones derivadas. El servidor de pruebas se detuvo al terminar.
- WinUI real con las vistas del producto y APIs de fixture en `artifacts/validation/preparations-final/`: tamaños físicos 1920 × 1080, 1600 × 900 y 1366 × 768, escala de Windows 125 %. Reportes registran la escala y geometría. Se ejercitan botón, doble clic, Enter, selección, refresco y conservación al cambiar pestaña.
- Ejecutable real actualizado abierto por separado hasta el login; hash contrastado con la compilación. Evidencia en `artifacts/validation/preparations-runtime/runtime-validation.json`. La sesión autenticada anterior se dejó abierta; no demuestra esta nueva versión autenticada.
- Intermedia: creación de prueba en memoria con tres consumos y apertura de la ficha mediante botón, doble clic y Enter confirmadas en `artifacts/validation/preparations-final/intermedia-verification.json`. Listado y ficha capturados también a 1366 × 768, 125 %.
- Pendientes externos: validación nativa al 100 % y sesión autenticada de la nueva compilación contra la API actualizada. Las capturas pobladas son fixtures, no registros de InterDB. No se accedió a InterDB ni se crearon registros reales ni se aplicaron migraciones operativas.
- El backend de lectura actualizado debe desplegarse para habilitar los nuevos filtros e historial. No necesita cambios de esquema. Fuera de los movimientos existentes, un historial nuevo de otros consumos sigue fuera de este alcance.

Capturas revisadas: `stock-low-1366.png`, `stock-expired-1366.png`, `stock-empty-1366.png`, `detail-button.png`, `intermedia-list-1366.png` e `intermedia-detail-button-1366.png`, bajo `artifacts/validation/preparations-final/`. Todas las capturas pobladas de esa carpeta usan fixtures explícitos.
