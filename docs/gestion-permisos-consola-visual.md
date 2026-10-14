# Consola visual de Gestión de permisos

13 de octubre de 2026. Cambios limitados a la presentación de Gestión de permisos, su ViewModel y la validación visual. Endpoints, contratos, autorización y persistencia permanecen iguales. No se ejecutó SQL ni se modificaron usuarios, roles o permisos reales.

## Pantalla y componentes

La aplicación utiliza la pantalla real `PermissionsPage`, con dos paneles: usuarios de 280–300 DIP y espacio de permisos adaptable. La búsqueda permanece visible; la lista desplaza sus filas de manera independiente. Las filas muestran iniciales, nombre, correo, rol y un resumen de la última configuración consultada. Los usuarios todavía no consultados se identifican como «Permisos por consultar»; no se inventan sus asignaciones ni se hacen peticiones adicionales por cada fila.

El encabezado del usuario, los tabs y las acciones permanecen fuera del scroll. Permisos e Historial son vistas internas exclusivas con `LimsTabStrip` y su underline. Cada módulo muestra sus acciones en columnas iguales, con un máximo de cinco columnas; Stock e Intermedia usan las mismas medidas que Estándares. El panel adapta las columnas al ancho disponible. El historial tiene lista, actor, fecha, permiso, acción y paginación propios.

Componentes locales nuevos:

- `Frontend/Lims.Desktop/Controls/PermissionAction.cs`: acción de permiso basada en `CheckBox`, conservando el peer nativo, TogglePattern y foco de teclado.
- `Frontend/Lims.Desktop/Controls/PermissionActionPanel.cs`: distribución adaptable de acciones.
- `Frontend/Lims.Desktop/Controls/PermissionUserItem.cs`: fila plana, selección persistente con acento y hover independiente.
- `Frontend/Lims.Desktop/Presentation/PermissionsMotion.cs`: ejecución de transiciones del compositor, con lectura de la política de animaciones de Windows.

Se reutilizan `LimsIcon`, `LimsTabStrip`, `StatusBadge`, los estilos de botones, Motion, paleta, superficies, bordes, tipografía e inputs del DesignSystem. No se modifica el sidebar ni las pantallas de otros módulos.

## Selección y guardado

Las selecciones permanecen vinculadas a `PermissionChoice.IsGranted`. El resumen guardado se calcula desde la respuesta del servidor y el contador local desde la selección actual; un cambio local no se muestra como un permiso guardado.

Guardar está deshabilitado sin modificaciones, también para un usuario sin configuración individual. La acción separada «Configurar sin permisos» permite activar explícitamente una matriz vacía únicamente después de confirmar. Revocar todos los permisos individuales de un usuario común también requiere confirmación antes de guardar. El acceso de Administrador permanece determinado por el rol existente.

Durante la petición se muestra el estado Guardando, se bloquean edición, recarga, descarte y otro envío. El éxito solo procede de la respuesta exitosa de la API; un fallo conserva la selección pendiente. Descartar restaura el estado confirmado sin enviar una petición. Cambiar usuario, buscar o salir conserva el aviso existente antes de descartar.

Se distinguen los cuatro estados: sin configuración individual, configuración existente vacía, configuración existente con permisos y Administrador con acceso por rol. Al cambiar de usuario se limpia inmediatamente la matriz anterior, aparece el indicador de carga y la nueva matriz comienza al inicio del scroll.

## Movimiento implementado

| Interacción | Ejecución |
|---|---|
| Usuario cargado | Fade de 180 ms y desplazamiento de 7 DIP en el espacio del usuario. |
| Hover y selección de filas | Transiciones de opacidad de 120 ms en superficies distintas; el acento de selección permanece visible. |
| Permiso | Fondo y marca con transición de 140 ms; contador local actualizado desde el ViewModel. |
| Tabs | Underline de 180 ms del DesignSystem y fade de contenido de 180 ms. |
| Mensaje | Entrada suave de 180 ms después de actualizar el mensaje real de la operación. |
| Botones | Estados del DesignSystem y escala pressed de 0.98, con tiempos de 70/110 ms. |

Las transiciones locales consultan `Motion.Enabled` (`UISettings.AnimationsEnabled`). Cuando las animaciones están desactivadas, esas transiciones se resuelven inmediatamente. Los indicadores de carga solo están activos durante sus peticiones. No se añadieron efectos decorativos continuos.

## Validación y evidencia

- Solución Debug x64: 0 advertencias, 0 errores. Escritorio real y harness compilados sin errores.
- 88/88 pruebas Desktop aprobadas; reporte final `artifacts/validation/permissions-console/permissions-console-desktop-final.trx`.
- Regresiones de ViewModel: guardar solo cambios, descarte sin petición, estado vacío explícito, pérdida de matriz anterior durante carga, doble envío bloqueado, fallo que conserva selecciones y cuatro estados de configuración.
- UI Automation: selección, foco, TogglePattern, concesión, revocación, envío, guardado deshabilitado sin cambios, error, aviso de descarte, historial, paginación y confirmación vacía cancelada sin enviar datos.
- Geometría a 1366×768, 1600×900 y 1920×1080: buscador, encabezado, tabs, viewport y acciones dentro de la ventana, con scroll independiente. Reportes `1366x768-layout.json`, `1600x900-layout.json` y `1920x1080-layout.json`.
- `console-validation.json` registra la ejecución aprobada. Las capturas y la grabación provienen de WinUI compilando la pantalla y sus componentes reales con un cliente aislado de pruebas. Cada ventana lleva el aviso «VALIDACIÓN VISUAL · DATOS FICTICIOS». Los resultados de guardado del cliente aislado son evidencia de interfaz; no prueban un guardado en InterDB.
- La automatización accesible utiliza los patrones de checkbox y tab. También se comprobó un clic físico de permiso durante la validación; la entrega no afirma una prueba completa de teclado físico ni de hover con puntero en la sesión productiva.
- El hover de la casilla se comprobó además con movimiento real del puntero en el cliente aislado. La opacidad efectiva se limita al 6 % en la animación de Composition, evitando que el fondo se vuelva blanco. El píxel de fondo pasó de RGB (24, 38, 53) a (37, 50, 65), sin cambiar la selección. Evidencia: `hover-validation.json`, `15-hover-normal.png` y `16-hover-corregido.png`.

| Evidencia visual aislada | Archivo |
|---|---|
| Permisos 1366×768 | [Captura](../artifacts/validation/permissions-console/01-permisos-1366x768.png) |
| Permisos 1600×900 | [Captura](../artifacts/validation/permissions-console/09-permisos-1600x900.png) |
| Permisos 1920×1080 | [Captura](../artifacts/validation/permissions-console/09-permisos-1920x1080.png) |
| Historial 1366×768 | [Captura](../artifacts/validation/permissions-console/07-historial-1366x768.png) |
| Guardando | [Captura](../artifacts/validation/permissions-console/03-guardando.png) |
| Error con cambios conservados | [Captura](../artifacts/validation/permissions-console/04-error-cambios-preservados.png) |
| Aviso antes de cambiar usuario | [Captura](../artifacts/validation/permissions-console/05-aviso-cambios-pendientes.png) |
| Confirmación de matriz vacía | [Captura](../artifacts/validation/permissions-console/12-confirmacion-matriz-vacia.png) |
| Movimiento | [Grabación GIF](../artifacts/validation/permissions-console/animaciones-winui-fixture.gif) |

La grabación usa PrintWindow del HWND de la ventana WinUI. Captura fotogramas reales, sin interpolación ni imágenes generadas. `motion-recording.json` incluye tamaño físico, tiempos de captura, eventos y número de fotogramas distintos. Muestra carga y selección de usuario, transición de permiso, tabs y estado de guardado; no es una grabación de operaciones productivas.

## Aplicación real

Se recompiló y reabrió el ejecutable que inicia WinAppCLI: `Frontend/Lims.Desktop/bin/Debug/net10.0-windows10.0.26100.0/win-x64/AppX/Lims.Desktop.exe`. Se verificó por SHA256 que `Lims.Desktop.dll` y `resources.pri` del AppX coinciden con la salida compilada. Evidencia final en `artifacts/validation/permissions-console-real/real-runtime.json`.

El 14 de octubre se comprobó la consola autenticada con 29 usuarios activos reales. Se consultaron Ana Barrios, Alejandro Sican y Adrian de Paz mediante SelectionItemPattern, manteniendo Guardar y Descartar deshabilitados sin cambios. El administrador muestra acceso a los tres módulos por rol; los usuarios comunes consultados muestran su ausencia de configuración individual. El historial de Ana devuelve cero cambios y muestra ese estado vacío, sin agregar registros de prueba.

Las capturas de permisos e historial en `artifacts/validation/permissions-console-real/` tienen tamaños físicos de 1366×768, 1600×900 y 1920×1080. También se verificaron los scrolls independientes de usuarios y matriz a 1366×768, manteniendo buscador, identidad, tabs y acciones dentro de la ventana. `real-validation.json` y los archivos `*-layout.json` registran estas consultas y sus límites. Un intento de clic físico fue bloqueado por la verificación de ventana activa; la selección real se comprobó mediante UI Automation.

Al revisar las capturas reales se detectó el hover demasiado claro de una casilla. Se corrigió y se repitieron la validación aislada y las 88 pruebas Desktop, todas aprobadas. Se recompiló y reabrió el AppX real con esa corrección. Después del nuevo inicio de sesión se repitieron las capturas de permisos e historial en las tres resoluciones y se comprobó el hover real con el puntero sobre Editar estándares: el fondo pasó de RGB (24, 38, 53) a (37, 50, 65), manteniendo la selección y sin habilitar Guardar ni Descartar. El control conservó el foco nativo, sin enviar teclas ni activar la casilla. Evidencia en `hover-validation-final.json`, `hover-normal-final.png` y `hover-corregido-final.png`.

Los hashes finales y la comprobación autenticada están en `artifacts/validation/permissions-console-real/real-runtime.json`. La ventana quedó abierta con la sesión real. No se leyó ni capturó la contraseña y no se editaron ni guardaron permisos reales.

| Consulta en aplicación real | Archivo |
|---|---|
| Permisos 1366×768 | [Captura](../artifacts/validation/permissions-console-real/permisos-1366x768.png) |
| Permisos 1600×900 | [Captura](../artifacts/validation/permissions-console-real/permisos-1600x900.png) |
| Permisos 1920×1080 | [Captura](../artifacts/validation/permissions-console-real/permisos-1920x1080.png) |
| Matriz con scroll inferior | [Captura](../artifacts/validation/permissions-console-real/permisos-1366x768-scroll-inferior.png) |
| Historial real vacío | [Captura](../artifacts/validation/permissions-console-real/historial-1366x768.png) |
| Administrador por rol | [Captura](../artifacts/validation/permissions-console-real/administrador-acceso-por-rol-1920x1080.png) |
| Hover corregido en sesión real | [Captura](../artifacts/validation/permissions-console-real/hover-corregido-final.png) |

Archivos de presentación modificados: `PermissionsPage.xaml`, `PermissionsPage.xaml.cs` y `PermissionsViewModel.cs`. Archivos de verificación modificados: `PermissionsViewModelTests.cs`, `FixturePermissionsApi.cs`, `App.xaml.cs` y el proyecto `Lims.Desktop.VisualHarness.csproj`. Scripts añadidos: `validate-permissions-console.ps1` y `record-permissions-motion.py`.

Reproducción:

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1 -p:BuildInParallel=false
dotnet test Tests/Frontend/Lims.Desktop.Tests/Lims.Desktop.Tests.csproj --no-build -c Debug
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
& ./scripts/validate-permissions-console.ps1
python scripts/record-permissions-motion.py artifacts/validation/permissions-console
```

El harness necesita acceso a la sesión de escritorio para iniciar una ventana WinUI y usar UI Automation. El grabador usa Pillow, ya disponible en el entorno de validación. No se ejecutan scripts SQL, migraciones ni tests PostgreSQL en esta fase visual.
