# Inicio: tamaños compactos restaurados

Se recuperó la densidad anterior del dashboard conservando las superficies, bordes, colores y estados nativos aprobados. No se modificaron datos, cálculos, servicios, shell ni otros módulos.

- KPI: altura mínima de 128 DIP en lugar de 166.
- Tablas: filas compactas de 24 DIP, con hover, selección y navegación existentes.
- Encabezados de panel de 12 DIP, etiquetas de 10 y secundarios de 9, como en la composición compacta anterior.
- Cuatro KPI y tres paneles analíticos en paralelo desde los anchos anteriores; se retiró la reorganización que añadía filas en ventanas medianas.
- Se conservaron los donuts, barras, indicadores y todos los paneles. No se ocultó contenido para lograr el ajuste.

Archivos de producto ajustados: DashboardComponents.cs, DashboardPage.xaml, DashboardPage.xaml.cs y LimsDashboard.xaml. La expectativa tipográfica del script de validación se actualizó al tamaño compacto solicitado.

El build Debug win-x64 de la aplicación y del harness terminó con cero errores y cero advertencias. No se añadieron pruebas unitarias para este ajuste exclusivamente visual.

Se comprobó la aplicación real autenticada con 53 registrados y 46 activos. A DPI 120 (125 %), en 1920×1140 y en la ventana maximizada, DashboardScroll informa que no hay desplazamiento vertical ni horizontal. Todos los bloques quedan visibles en una sola pantalla. En 1600×900 y 1366×768 se conserva el desplazamiento vertical necesario y no hay desbordamiento horizontal.

- [Captura real maximizada](../artifacts/validation/dashboard-compact-real/home-maximized.png).
- [Manifiesto real](../artifacts/validation/dashboard-compact-real/validation.json).
- [Compilación](../artifacts/validation/dashboard-compact-real/build.log).

La captura real está separada de las capturas de prueba en dashboard-compact. El DLL del proceso ejecutado se contrastó con el build: SHA256 4C312CFCA20E0FAAB658B464B3F038FB854A4759D7B004092FFAE3520011072A. La aplicación queda abierta y maximizada en Inicio.
