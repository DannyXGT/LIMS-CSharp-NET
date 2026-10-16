# Inicio / Dashboard: composición analítica

> Documento de la reconstrucción anterior. El diseño vigente de Inicio se integra con Estándares y sus recursos compartidos; ver [inicio-design-system.md](inicio-design-system.md), con las capturas y verificaciones de esa unificación.

Inicio se reconstruyó como dashboard oficial WinUI 3 tomando como referencia las imágenes recibidas el 15 de octubre. Incluye título «Resumen dashboard»; cuatro KPI con iconos iluminados; estado general con donut grande; tabla de vencimientos; columnas de disponibilidad por método; distribución por método; resumen de disponibilidad con donut; tabla de saldos bajos y alarmas. El encabezado del contenido muestra únicamente el título, sin avatar, identidad, fecha, hora ni botón Actualizar. El contenido termina con estos dos últimos paneles, sin actividad reciente, accesos rápidos ni línea de última consulta.

La restauración posterior recupera el shell corporativo original y corrige los gráficos para usar agregados del catálogo completo. El estado actual, las pruebas y los límites de despliegue se detallan en [restauracion-shell-y-agregados-dashboard.md](restauracion-shell-y-agregados-dashboard.md). La evidencia de reconstrucción citada más abajo corresponde a la versión anterior a esa restauración.

## Datos reales

- Los KPI, estados y disponibilidad conservan `/api/dashboard` y sus permisos. Se amplió exclusivamente su respuesta de lectura con agregados por método; no se modifican fórmulas de negocio, umbrales, esquema ni operaciones de escritura.
- Las columnas usan el promedio simple de porcentajes verificables de todos los materiales utilizables del alcance autorizado. El subtítulo informa el número de participantes. No se pondera por volumen ni pureza.
- La distribución por método usa el catálogo autorizado completo, incluidos todos sus estados. Para acceso a Estándares, su total coincide con el KPI de registrados. Los seis próximos vencimientos siguen siendo una lista prioritaria separada.
- Si el servidor aún no entrega los nuevos agregados, los gráficos indican que faltan estadísticas del catálogo; no usan las listas limitadas como sustituto. El paquete de actualización del servidor se entrega por separado.
- El resumen de estándares utiliza categorías excluyentes: activos sin alerta baja, disponibilidad baja sin críticos, críticos, vencidos y otros estados. Cada material se cuenta una vez.
- No se presentan incrementos mensuales, series históricas ni eventos ficticios. Las alarmas proceden de disponibilidad baja/crítica, conteos reales de vencidos y vencimientos de hoy o dentro de la ventana configurada por el servidor. No se inventa una ventana cuando es nula. La topbar no expone una acción de notificaciones sin destino.
- Los KPI críticos y vencidos con valor cero usan color favorable. Los días restantes de un vencimiento lejano usan color informativo. La barra del próximo vencimiento representa la disponibilidad del material y está rotulada como tal; no representa una cuenta regresiva ficticia.
- La búsqueda permite navegar a módulos autorizados y registros prioritarios. El texto libre consulta Estándares; para cuentas sin ese acceso utiliza Stock o Intermedia, según sus permisos, mediante la búsqueda existente de ese módulo. No se implementa una búsqueda global del contenido de módulos pendientes.
- El sidebar recupera las entradas originales de Inicio, Estándares, Preparaciones y Permisos. La búsqueda mantiene los destinos funcionales autorizados, incluidas las pestañas de Stock e Intermedia.

## Componentes

`DashboardComponents` proporciona tarjetas KPI, panel analítico, tabla compacta, chips semánticos, enlaces de registros, donut con leyenda y barras verticales/horizontales. Todos son controles y geometría WinUI nativos. `DashboardAnalytics` consume los agregados completos del servidor; la deduplicación de listas prioritarias se conserva únicamente para la búsqueda.

`LimsDashboard.xaml` reúne fondos petróleo con gradientes, bordes discretos, cian y morado, estilos de panel/enlace/fila y equivalentes de alto contraste. Las filas analíticas usan ListView nativo con hover, pressed, selección, foco y apertura del registro por ID. Los gráficos usan geometría vectorial, anillos gruesos con profundidad interior, leyendas laterales, barras redondeadas y escala porcentual. El contenido conserva opacidad breve y respeta movimiento reducido.

La ventana conserva la barra corporativa original, su logo y los controles nativos de ventana, tanto en login como en la sesión autenticada. El sidebar inicia compacto. La navegación y la búsqueda respetan la confirmación de cambios pendientes de Permisos.

## Archivos de esta implementación

- `Frontend/Lims.DesignSystem/Themes/LimsDashboard.xaml`
- `Frontend/Lims.DesignSystem/Controls/LimsIconKind.cs`
- `Frontend/Lims.DesignSystem/Controls/LimsIconCatalog.cs`
- `Frontend/Lims.DesignSystem/Controls/LimsIcon.cs` (la restauración posterior retiró el color optativo y recuperó sus estados originales)
- `Frontend/Lims.Desktop/App.xaml`
- `Frontend/Lims.Desktop/MainWindow.xaml`
- `Frontend/Lims.Desktop/MainWindow.xaml.cs`
- `Frontend/Lims.Desktop/Controls/DashboardComponents.cs`
- `Frontend/Lims.Desktop/Presentation/DashboardAnalytics.cs`
- `Frontend/Lims.Desktop/Presentation/DashboardMotion.cs`
- `Frontend/Lims.Desktop/ViewModels/DashboardViewModel.cs`
- `Frontend/Lims.Desktop/Views/DashboardPage.xaml`
- `Frontend/Lims.Desktop/Views/DashboardPage.xaml.cs`
- `Frontend/Lims.Desktop/Views/ShellPage.xaml`
- `Frontend/Lims.Desktop/Views/ShellPage.xaml.cs`
- `Tests/Frontend/Lims.Desktop.Tests/DashboardAnalyticsTests.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/App.xaml.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/FixtureDashboardApi.cs`
- `Tests/Frontend/Lims.Desktop.VisualHarness/Fixtures.cs` (métodos de prueba y escenario saludable, exclusivamente en validación aislada)
- `Tests/Frontend/Lims.Desktop.VisualHarness/ValidationApp.xaml`
- `Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj`
- `scripts/validate-dashboard-premium.ps1`
- `docs/dashboard-premium.md`

Se preservaron los cambios locales que ya existían en algunos de estos archivos y en otros módulos.

## Validación

Las pruebas de presentación verifican categorías autorizadas, ceros reales, deduplicación por material, conteos de disponibilidad sin solapamientos, errores y cancelación de respuestas anteriores. La validación WinUI aislada usa `scripts/validate-dashboard-premium.ps1` y queda identificada como datos ficticios.

La reconstrucción posterior a recibir las imágenes tiene evidencia independiente en `artifacts/validation/dashboard-reconstruction/` (aislada) y `artifacts/validation/dashboard-reconstruction-real/` (sesión real). Las carpetas anteriores `dashboard-premium*` pertenecen a la versión anterior.

Resultados del 15 de octubre de 2026:

- Build Debug x64 de la aplicación y del harness: sin errores ni advertencias.
- 99 pruebas del frontend aprobadas. Resultado: `dashboard-reconstruction/tests/dashboard-reconstruction-final.trx`.
- 24 casos WinUI aislados aprobados: ocho escenarios (Administrador, saludable, laboratorio, acceso limitado, vacío, error, Compras y restringido), en 1920×1080, 1600×900 y 1366×768. Sin desbordamiento horizontal; KPI de altura uniforme; secciones inferiores accesibles; tema oscuro conservado al cambiar el tema padre; filtro de vencidos y Actualizar comprobados. Resultado: `dashboard-reconstruction/validation.json`.
- Las filas analíticas expusieron selección e invocación nativas. Se capturaron selección, hover y pressed; se verificó que la ficha abierta correspondía al ID invocado. Resultado: `dashboard-reconstruction/row-interaction.json`.
- Aplicación real autenticada como Danny Jimenez: 53 estándares registrados, 46 activos y 3 vencidos. Las cifras de la sesión anterior (3 estándares) no se reutilizaron como datos de prueba.
- Capturas reales de los tres tamaños, con gráficos y contenido inferior en cada tamaño; captura adicional maximizada. Actualizar se deshabilitó durante la consulta, conservó las estadísticas actuales y volvió a habilitarse. Resultado: `dashboard-reconstruction-real/final-validation.json`.
- Hash del DLL ejecutado comprobado contra el build final: `EF53E0F225F064FA5C1791F1D27694504B12464F5A3D65361392DA0DA8DDA96F`. Se actualizaron también las salidas habituales `bin/Debug/.../win-x64` y su `AppX` para evitar una versión anterior.

Se compararon las capturas con la imagen aprobada: iconos protagonistas y fondos iluminados en los KPI, proporción 40/60 del bloque de estados/vencimientos, donuts de anillo grueso, leyendas laterales, cabeceras con iconos, escala de columnas y valores superiores, barras horizontales proporcionales, tablas compactas y alarmas con severidad. La revisión produjo correcciones de altura de filas, legibilidad de los valores al 100 %, tipografía de grupos laterales y acabado del resumen de vencidos.

## Diferencias y límites visuales

Se utiliza la iconografía vectorial del sistema existente, no una copia de los dibujos de la imagen. Los datos reales determinan las cantidades de filas, métodos y segmentos: no se reproducen las tendencias, nombres ni alarmas ficticias de la referencia. El gráfico de columnas se titula por método y representa porcentajes verificables, en vez de categorías inexistentes.

Las capturas tienen DPI 120 (125 %); a menor altura se accede a las secciones inferiores mediante desplazamiento vertical. No se validó escala 100 %. La ventana queda maximizada en Inicio.

## Corrección del arranque desde WinApp

El 15 de octubre se reprodujo un fallo `XamlParseException` en `ShellPage.InitializeComponent` al abrir la salida habitual `bin/Debug/.../win-x64/AppX`. Esa carpeta tenía DLL anteriores junto con el PRI nuevo. La receta `Lims.Desktop.build.appxrecipe` reconstruía el paquete usando el ensamblado de `obj/Debug` y el DesignSystem de `bin/Debug`, que no se habían actualizado con la copia manual de la salida x64. Por eso copiar DLL a la carpeta final no resolvía el siguiente empaquetado.

Se recompiló la ruta habitual con `dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --configuration Debug --runtime win-x64 -m:1 -p:BuildInParallel=false`, regenerando intermedios, ensamblados, recursos y receta. Se probó el arranque real con `winapp run Frontend/Lims.Desktop/Lims.Desktop.csproj --with-alias --no-build --no-restore --debug-output` desde el proyecto. El login apareció desde la ruta que había fallado; ShellPage se construye antes del login, por lo que no hace falta volver a autenticar para verificar este fallo concreto.

`scripts/run-winui.ps1` ahora compila en serie con RID explícito y excluye tanto recetas como un AppX previo al preparar una carpeta aislada. No se deben sincronizar manualmente solamente `bin/Debug` o su AppX: hay que reconstruir la configuración correspondiente. La evidencia de este arreglo está en `artifacts/validation/startup-xaml-fix/`.

## Métricas no disponibles

El endpoint no proporciona tendencias mensuales, series históricas ni un historial persistente de notificaciones. La distribución completa por método está implementada y probada localmente, y requiere instalar el paquete actualizado en el servidor remoto. No se sustituyen las métricas faltantes por ejemplos de la imagen. Las alarmas representan condiciones de la consulta actual, no eventos históricos. Para estándares vencidos el agregado proporciona el conteo, no sus nombres ni fechas individuales: la fila de alarma abre el catálogo real filtrado por vencidos; no inventa registros ni fechas.
