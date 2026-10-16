# Restauración del shell y agregados completos de Inicio

## Alcance

Se conserva «Resumen dashboard» y sus cuatro KPI, donuts, tabla de vencimientos, columnas de disponibilidad, distribución por método, disponibilidad baja y alarmas. No se restauran actividad reciente, accesos rápidos ni el encabezado de usuario retirados por solicitud previa.

El shell se contrastó con la captura original de Estándares y con la versión anterior al rediseño conservada en Git. Se restituyeron:

- Barra corporativa de 48 DIP con el logo original, LIMS y «Gestión de laboratorio».
- Sidebar de 252 DIP al expandir, compacto al iniciar, sin expansión automática por resolución.
- Márgenes, tipografía, colores nativos, separadores y perfil/cierre de sesión originales.
- Icono de biblioteca de Estándares y colores uniformes en navegación. Se retiraron las entradas añadidas por la maqueta, incluidas las pendientes.
- Encabezados y separación originales en los módulos. Los recursos petróleo del dashboard ya no se usan para pintar el shell.

La búsqueda existente conserva destinos reales y comprobación de permisos; usa la barra corporativa y sus recursos originales. No hay notificaciones globales ficticias. No se modificaron los estilos internos de Estándares, Preparaciones ni Permisos, ni los estilos globales de controles.

## Causa y consulta corregida

DashboardAnalytics.Methods y MethodAvailability usaban PrioritySample, unión deduplicada de Expirations y LowAvailability. Esas listas están limitadas; los seis próximos vencimientos no representan el catálogo de 53 estándares.

DashboardRepository.GetAsync ahora calcula MethodStatistics mediante GROUP BY, conteos y AVG en PostgreSQL, sobre el mismo conjunto autorizado de ReferenceMaterials que alimenta Standards.Registered. La resolución de métodos usa una unión izquierda con ReferenceMethods; un nombre ausente o vacío se presenta como «Sin método». No se descargan todas las entidades al cliente.

Con acceso a Estándares:

- Distribución: catálogo completo, incluidos sus distintos estados. MethodStatistics.Total coincide con Standards.Registered y con la suma de Methods.Count.
- Disponibilidad: promedio aritmético de porcentajes de estándares activos, vigentes, con saldo positivo, cantidad inicial positiva y saldo no superior al inicial. AvailabilityTotal informa cuántos participan. Se excluyen los saldos no verificables; no se usa pureza ni se pondera por cantidad física.

Sin acceso a Estándares, el agregado solo incluye Stock/Intermedia autorizados, identificados en Scope. Sus condiciones de uso coinciden con las reglas existentes. Si ambos están autorizados, se combinan medias mediante sus cantidades participantes; no se promedian medias con pesos iguales.

La geometría utiliza los conteos originales. Los porcentajes de partes de un total se presentan a una décima con reparto de restos para sumar exactamente 100,0 %. Las listas prioritarias mantienen su límite de seis.

El nuevo campo es opcional en el contrato de lectura. Si la API anterior no lo entrega, el cliente indica que faltan estadísticas del catálogo y no vuelve a dibujar una distribución parcial de seis registros.

## Cambios de código

- Backend/Lims.Infrastructure/Persistence/DashboardRepository.cs
- Shared/Lims.Contracts/Dashboard/DashboardContracts.cs (solo respuesta de lectura)
- Frontend/Lims.Desktop/MainWindow.xaml y .xaml.cs
- Frontend/Lims.Desktop/Views/ShellPage.xaml y .xaml.cs
- Frontend/Lims.DesignSystem/Controls/LimsIcon.cs (retirada del color multicolor de navegación introducido por el dashboard)
- Frontend/Lims.Desktop/Presentation/DashboardAnalytics.cs
- Frontend/Lims.Desktop/Controls/DashboardComponents.cs
- Frontend/Lims.Desktop/Views/DashboardPage.xaml.cs
- Pruebas PostgreSQL y frontend de dashboard, fixtures/harness y scripts/validate-dashboard-shell.ps1.

No se modificaron migraciones, esquema, operaciones CRUD, fórmulas ni reglas de autorización.

## Verificación local

- Build Debug de Desktop y harness: sin errores ni advertencias.
- 103 pruebas de frontend y 39 de API aprobadas.
- Prueba PostgreSQL real aislada en 127.0.0.1:55439/lims_stock_test: 53 estándares frente a seis prioridades, incluyendo 37 registros de otro método que no aparece en las prioridades. Comprueba conteos completos, medias, estados y revocación de permisos.
- 24 escenarios WinUI aislados aprobados.
- 12 capturas adicionales: Inicio compacto/expandido, Estándares y Permisos, en 1920×1080, 1600×900 y 1366×768. DPI del escritorio: 120 (125 %). Estas capturas son fixtures y están rotuladas como tales.

Evidencia: artifacts/validation/dashboard-shell-restoration/, con TRX en tests/, validation.json y shell-validation.json.

## Aplicación real y servidor

El cliente está configurado para http://10.226.248.191:8080/, otro equipo distinto de esta estación. La actualización está publicada en artifacts/publish/Lims.Api-dashboard-read-update.zip, sin archivos appsettings para conservar la configuración del servidor. No requiere migraciones ni SQL.

**Pendiente de instalar en el servidor remoto, por decisión del usuario: finalizar la entrega local y actualizarlo después.** Hasta entonces no se declara corregida la distribución mostrada por el servidor real. La distribución 34/10/9 de las capturas aisladas pertenece exclusivamente al fixture de 53 registros.

Se completaron las capturas con la sesión real del usuario: Inicio compacto/expandido, Estándares y Permisos en 1920×1080, 1600×900 y 1366×768, además de Inicio maximizado y los paneles inferiores en las tres resoluciones. Se revisaron visualmente el logo/barra originales, el icono de biblioteca de Estándares, la selección nativa, el perfil inferior y los fondos de los módulos. La aplicación queda maximizada en Inicio con el sidebar compacto.

Las capturas de sesión real están separadas en artifacts/validation/dashboard-shell-restoration-real/. validation.json registra authenticated=true, fixtureOnly=false, registered=53 y methodAggregatesAvailable=false; el hash corresponde al DLL del proceso ejecutado. La respuesta anterior del servidor conserva los KPI de 53 registrados, 46 activos y tres vencidos. Los dos gráficos por método muestran «Estadísticas del catálogo no disponibles», sin presentar las seis prioridades como un total general. No se escribieron registros ni permisos durante las capturas.

Como contraste independiente, se leyeron las tres páginas de Estándares mediante la interfaz autenticada, recorriendo también sus posiciones de desplazamiento, sin agregar una descarga completa al cliente. El conteo observado es:

| Método | Registros |
| --- | ---: |
| Phtalatos | 30 |
| Azodyes | 21 |
| Disperse dyes | 1 |
| PAHs | 1 |
| **Total** | **53** |

La evidencia de ese contraste es real-method-check.json, con serverAggregatesVerified=false. Es una referencia de la sesión actual para verificar el despliegue posterior; no sustituye la comprobación de MethodStatistics ni verifica todavía las medias reales de disponibilidad. La instalación del servidor y esa comprobación quedan pendientes para la actualización posterior.
