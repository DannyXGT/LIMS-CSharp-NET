# Inicio integrado en el Design System

> Ajuste posterior del 16 de octubre: se recuperaron los tamaños compactos anteriores por solicitud del usuario, conservando esta paleta. La evidencia vigente de tamaños está en [inicio-compacto.md](inicio-compacto.md); las mediciones tipográficas de la unificación descritas aquí corresponden a la versión anterior a ese ajuste.

## Resultado y alcance

Inicio utiliza las mismas superficies, bordes y colores semánticos que Estándares. Se retiraron los fondos petróleo, gradientes de iluminación, cian propio, filas coloreadas y la imposición local de tema oscuro. Se mantienen KPI, proporciones, donuts vectoriales, columnas, barras horizontales, estados, tablas, vencimientos, disponibilidad baja y alarmas.

No se editaron en esta tarea Estándares, Preparaciones, Permisos, login, sidebar, topbar, autenticación, servicios de negocio, base de datos ni migraciones. Se conservaron los cambios previos del árbol de trabajo.

| Elemento | Recurso o medida compartida |
| --- | --- |
| Fondo de página | Transparente, heredado del mismo contenedor que Estándares |
| Paneles y KPI | LimsSurfaceBrush, LimsBorderBrush, radio 8 DIP, relleno 12 DIP |
| Título | 22 DIP y SemiBold, como el encabezado de Estándares |
| Texto | LimsBodyStyle y LimsCaptionStyle; encabezados de panel 16, contenido 14/13 y secundarios 12 DIP |
| Tabla | LimsTableHeadingStyle, cabecera de 36 DIP, LimsDataRowStyle y estados nativos de ListView |
| Nombres de material | Texto primario SemiBold dentro de la fila, con tooltip y navegación por ID |
| Etiquetas de estado | StatusBadge compartido |
| Acciones y transiciones | LimsStandardsSecondaryButtonStyle, Motion.Enter y política compartida de movimiento reducido |
| Iconos | LimsIcon existente, sin iluminación propia |

Las cuadrículas reorganizan las tarjetas y los paneles al reducir el ancho; se conserva el desplazamiento vertical para no comprimir el texto ni ocultar secciones. No se modificaron cantidades, porcentajes, umbrales, redondeos o cálculos.

## Dependencia de los gráficos por método

El cliente existente consulta GET /api/dashboard mediante DashboardApiClient. Los gráficos requieren DashboardResponse.MethodStatistics: Scope, Total, AvailabilityTotal y Methods, con Count, AvailabilityCount y AverageAvailabilityPercent por método.

La consulta de lectura completa ya existe localmente en DashboardRepository.GetAsync y AggregateMethodsAsync. El cliente está configurado para http://10.226.248.191:8080/. La entrega anterior dejó preparado artifacts/publish/Lims.Api-dashboard-read-update.zip; el usuario decidió instalarla después.

La disponibilidad de ese campo en el servidor real se registra en methodAggregatesAvailable de la evidencia de esta tarea. Si falta, Inicio conserva ambos gráficos con su estado «Estadísticas del catálogo no disponibles». No se recurre a las seis prioridades, a descargar todas las entidades ni a cifras de ejemplo. No se ejecutó SQL ni se cambió el servidor durante esta tarea.

## Archivos de esta tarea

- Frontend/Lims.Desktop/Views/DashboardPage.xaml y DashboardPage.xaml.cs.
- Frontend/Lims.Desktop/Controls/DashboardComponents.cs.
- Frontend/Lims.Desktop/Presentation/DashboardAnalytics.cs: únicamente nombres de pinceles compartidos.
- Frontend/Lims.DesignSystem/Themes/LimsDashboard.xaml: conserva solo estilos de composición basados en recursos compartidos; no redefine la paleta global.
- Frontend/Lims.Desktop/Presentation/DashboardMotion.cs: retirado; se llama directamente al Motion compartido.
- Tests/Frontend/Lims.Desktop.VisualHarness/App.xaml.cs y su proyecto: mediciones visuales y retirada del enlace al archivo de animación eliminado.
- scripts/validate-dashboard-design-system.ps1 y ajuste de la expectativa de herencia de tema en scripts/validate-dashboard-premium.ps1.

## Verificación

- Build Debug win-x64 de la aplicación: cero errores y cero advertencias. Registro: artifacts/validation/dashboard-design-system-real/build.log.
- Build del harness: cero errores y cero advertencias.
- 103 pruebas de frontend aprobadas, sin omisiones. Resultado: artifacts/validation/dashboard-design-system/tests/frontend.trx.
- Cuatro casos WinUI aislados: claro/oscuro × 1600×900/1366×768, a DPI 120 (125 %). Se midieron superficies, bordes y radios iguales entre Inicio y Estándares, títulos de 22 DIP y texto de contenido mínimo de 12 DIP, excluyendo glifos de iconos.
- Capturas aisladas de gráficos, contenido inferior, hover, pressed y selección. Las acciones de navegación ejercitadas solo leen datos. Los fixtures están rotulados «DATOS FICTICIOS» y separados de la sesión real.
- Las comparativas se componen con dos capturas nativas a su tamaño original, sin escalar ni reconstruir la interfaz como imagen.

Evidencia aislada: artifacts/validation/dashboard-design-system/design-validation.json y comparison-*.png.

Las capturas reales y su manifiesto quedan en artifacts/validation/dashboard-design-system-real/. Para comprobar el tema claro sin modificar código de producto se utiliza un controlador temporal que cambia RequestedTheme del contenido de la ventana en su DispatcherQueue. La configuración de arranque de la copia ejecutada se restaura tras iniciar; al terminar se restituye oscuro y se desconecta el controlador. El valor RequestedTheme de Application permanece Dark. Este mecanismo se limita a la validación y no se integra en los binarios del producto ni en su interfaz.

### Sesión real completada

Se guardaron las cuatro comparativas Inicio/Estándares, con cada página al mismo tamaño, además de gráficos, paneles inferiores y estados de hover/pressed:

- [Oscuro 1600×900](../artifacts/validation/dashboard-design-system-real/comparison-dark-1600x900.png).
- [Claro 1600×900](../artifacts/validation/dashboard-design-system-real/comparison-light-1600x900.png).
- [Oscuro 1366×768](../artifacts/validation/dashboard-design-system-real/comparison-dark-1366x768.png).
- [Claro 1366×768](../artifacts/validation/dashboard-design-system-real/comparison-light-1366x768.png).

validation.json confirma passed=true, authenticated=true, fixtureOnly=false, runtimeConfigRestored=true y temporaryThemeDriverDetached=true. Las cuatro consultas conservaron 53 registrados y 46 activos; las capturas también muestran tres vencidos. No se escribieron registros ni permisos. La aplicación queda maximizada en Inicio, en oscuro y con el sidebar compacto.

El DLL ejecutado coincide con el build final: SHA256 0D185A022DDFE457C9D46848EB44DC3BB876CDADCD9AFA7FC702F2502B09DAB5.

En las cuatro consultas reales methodAggregatesAvailable=false. Falta instalar en 10.226.248.191:8080 la actualización de lectura que entrega MethodStatistics; no falta una consulta local ni una migración. Los gráficos poblados de las capturas aisladas usan fixtures identificados y no se presentan como datos reales.

La escala de escritorio comprobada es 125 %. A menor altura, se accede a las secciones mediante desplazamiento vertical; no se ha reducido la tipografía para forzar todos los paneles en una sola pantalla. Los botones nativos de ventana conservan su configuración global previa, también durante la comprobación en claro.
