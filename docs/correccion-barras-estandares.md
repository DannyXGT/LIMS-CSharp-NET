# Barras de disponibilidad de Estándares — 16/10/2026

La tabla dejaba algunas barras ocultas al desplazarla, regresar al módulo o cambiar de página. La cantidad disponible seguía visible y era correcta.

La actualización anterior dependía de `Loaded` y `SizeChanged` de cada fila y consultaba su `DataContext`. La realización y reutilización de las plantillas de `ListView` puede actualizar el registro sin volver a ejecutar esa actualización con los datos actuales. La barra quedaba con su visibilidad, valor y etiqueta anteriores o iniciales.

Ahora visibilidad, valor y etiqueta se obtienen del mismo registro de la plantilla mediante funciones `x:Bind`. Las filas usan resúmenes inmutables: al recibir otro registro, el ciclo de binding de la plantilla vuelve a evaluar estos valores. Los eventos de tamaño gestionan únicamente la presentación compacta, conservando el umbral de 1150 unidades. Se conserva la fórmula y el tratamiento de cantidades no informadas o inválidas.

Archivos de producción:

- `Frontend/Lims.Desktop/Views/ReferenceMaterialsPage.xaml`
- `Frontend/Lims.Desktop/Views/ReferenceMaterialsPage.xaml.cs`
- `Frontend/Lims.Desktop/Presentation/ReferenceMaterialVisuals.cs`

Validación en WinUI real con API ficticia, sin conexión a PostgreSQL:

- Antes: 11 de 20 comprobaciones fallaron. Evidencia: `artifacts/validation/standards-availability-before/availability-summary.json` y sus informes por paso.
- Después: 20 de 20 con cantidades al 100 %, y otras 20 de 20 con porcentajes de 100, 20, 60, 0, 75 y 50 %.
- Cada secuencia incluyó tres ciclos de desplazamiento, salida/reentrada y recarga; también paginación, ventana compacta y regreso a ancho completo. Se compararon visibilidad, valor y etiqueta con el registro de cada fila visible.
- Informes: `artifacts/validation/bars-after/availability-uniform-summary.json` y `availability-mixed-summary.json`.
- Captura revisada: `artifacts/validation/bars-after/availability-fixed.png`.
- Compilación del harness y de `Lims.Desktop`: cero errores y cero advertencias, en serie con `-m:1 -p:BuildInParallel=false` y `Platform=x64`.

La comprobación se puede repetir con una sesión de escritorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-standards-visual.ps1 -Action Start -IsolatedIdentity -EvidenceFolder bars-check
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-standards-availability.ps1 -EvidenceFolder bars-check
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-standards-availability.ps1 -EvidenceFolder bars-check -MixedAvailability
```

La primera validación compiló en `bin/x64/Debug/...`, mientras que la aplicación del usuario seguía ejecutando `bin/Debug/.../AppX`. Al reportar que el fallo continuaba, se verificó el proceso real: su DLL no contenía `AvailabilityVisibility` y tenía SHA256 `4C312CFCA20E0FAAB658B464B3F038FB854A4759D7B004092FFAE3520011072A`.

Se corrigió `scripts/run-winui.ps1` para compilar por RID y abrir ese mismo proyecto con WinAppCLI, sin crear otra salida mediante `Platform=x64` ni una ubicación temporal. El lanzador imprime la ruta y el SHA256 de la compilación.

Se compiló la salida real con `dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --configuration Debug --runtime win-x64 --no-restore -m:1 -p:BuildInParallel=false`: cero errores y cero advertencias. Para completar esa compilación se corrigieron dos detalles de compilación en los cambios de Preparaciones presentes en el workspace: se restauró `SourcePage` en la consulta de orígenes de Stock y se documentó con `SuppressMessage` que `ShowEmptyCreate` permanece como propiedad de instancia para los bindings de Stock e Intermedia.

La ventana anterior se cerró de forma normal después de comprobar que estaba en la lista, sin editor abierto. Se abrió la aplicación real actualizada en `Frontend/Lims.Desktop/bin/Debug/net10.0-windows10.0.26100.0/win-x64/AppX/Lims.Desktop.exe`. El proceso nuevo 16516 y la salida compilada tienen el mismo SHA256: `B23FDA2C6EEC0FF1B304F3D43CA5AF04F62ABBFBCC932978185BA69DF39150A3`; ambos contienen la corrección. También coincide `resources.pri`. Evidencia: `artifacts/validation/standards-live/before.json`, `updated-login.json` y registros de lanzamiento.

Con la sesión real iniciada se comprobaron diez escenarios sobre el catálogo real de 53 registros: vista inicial, tres desplazamientos con PageDown, dos ciclos Inicio/Estándares, páginas 2 y 3, regreso a página 2 y regreso final al inicio de página 1. Los diez pasaron: cada fila completa en el área visible tenía su barra. Se inspeccionaron los controles y porcentajes mediante UI Automation. El desplazamiento se realizó con clic y teclado físicos; la navegación y paginación se ejercitaron mediante sus patrones accesibles. Las filas parcialmente recortadas se identifican por separado porque UI Automation puede marcar como visible una franja de 4 px cuya barra está fuera del área de la lista.

Informe real: `artifacts/validation/standards-live/runtime-summary.json`, con `fixtureOnly=false`, `authenticated=true` y `passed=true`. Captura real revisada visualmente: `artifacts/validation/standards-live/actual-standards-initial.png`; se ven las barras de 2-Anisidine y 2-Mehtoxy-5-methylaniline, ausentes en las capturas del usuario. Las capturas por PrintWindow en segundo plano pueden devolver contenido negro y no se usan como prueba visual; los informes de controles de esos pasos se conservan por separado.

Para lanzamientos posteriores:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-winui.ps1
```

Las 40 comprobaciones iniciales siguen siendo evidencia de fixtures. Los diez escenarios posteriores corresponden al ejecutable real con sesión autenticada. Se dejaron Estándares y la primera página abiertos. La corrección de barras afecta exclusivamente la presentación de Estándares.

Referencia de la sintaxis utilizada: [Funciones en x:Bind, Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/function-bindings).
