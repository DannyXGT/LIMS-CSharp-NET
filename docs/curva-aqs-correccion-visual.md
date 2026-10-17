# Curva y AQS: formularios, resultados y fichas

Corrección visual localizada del 16 de octubre de 2026. Conserva el calculador, las conversiones, los contratos HTTP, los permisos, la persistencia y el libro de consumos existentes. No aplica SQL ni migraciones en InterDB.

## Archivos de esta corrección

- `Frontend/Lims.Desktop/Views/SolutionGroupEditorDialog.xaml` y `.xaml.cs`: encabezado compacto, orden de campos por proceso, tarjetas, selección de resultados, errores junto a volúmenes y pie accesible.
- `Frontend/Lims.Desktop/Views/SolutionGroupDetailDialog.xaml` y `.xaml.cs`: nombre principal, indicadores, composición seleccionable, resultados por analito, consumos históricos y referencias de movimientos.
- `Frontend/Lims.Desktop/ViewModels/SolutionGroupViewModel.cs`: presentación por solución, selección activa, rol/origen explícitos y mensajes iniciales sin errores de campos sin completar.
- `Frontend/Lims.Desktop/Presentation/SolutionGroupPresentation.cs`: proyecciones de presentación, precisión decimal y volúmenes del recipiente.
- `Frontend/Lims.Desktop/Controls/SolutionGroupVisuals.cs`: recipiente nativo, tabla multianalito, referencias estructuradas y disponibilidad por origen.
- `Frontend/Lims.Desktop/Views/SolutionGroupPage.xaml.cs`: referencia al diálogo activo para verificar la selección y el desplazamiento en las fichas nativas.
- `Tests/Frontend/Lims.Desktop.Tests/SolutionGroupViewModelTests.cs`: regresiones de alícuotas, aforo, precisión, edición y errores de guardado.
- `Tests/Frontend/Lims.Desktop.VisualHarness/{App.xaml.cs,FixtureSolutionGroupApi.cs,Lims.Desktop.VisualHarness.csproj}`: prueba de las vistas reales con servicios aislados en memoria, segundo origen y fallo de servidor controlado.
- `scripts/validate-curve-aqs-final.ps1`, `scripts/validate-curve-aqs-shared.ps1` y `scripts/validate-curve-aqs-references.ps1`: reproducción de escenarios, consumo compartido, referencias y capturas.

## Comportamiento

Curva organiza soluciones de calibración y su código; AQS inicia por el método que filtra las Intermedias y organiza controles de calidad. Cada una permite agregar soluciones o un estándar interno con origen, rol, alícuota y aforo explícitos. No se añade una regla química nueva ni se exige un número fijo de niveles. TrazaLab se consultó solo como referencia funcional.

El selector muestra código y nombre corto. Las referencias completas se consultan como campos separados: origen, método, identificador, analito, CAS y lote. La tabla de concentraciones muestra origen y resultado por analito para la solución seleccionada. Los valores se toman del calculador existente; `G29` preserva el valor decimal sin redondeo intermedio.

El recipiente representa la incorporación antes del aforo: 25 mL incorporados, 100 mL finales y 75 mL pendientes son tres datos explícitos. La disponibilidad del origen se muestra por separado. Azul indica incorporación válida; advertencia a partir del 90 % es únicamente un umbral visual; rojo indica aforo excedido y Guardar permanece bloqueado. El porcentaje real puede superar 100 %; solo se limita la altura del dibujo.

Los consumos se toman de `Calculation.Debits`, agrupados por ID real. Tres soluciones multianalito de 2, 3 y 5 mL contra el mismo origen producen un único débito de 10 mL: 100 → 90 mL. La cantidad de analitos no multiplica ese débito. Las fichas muestran saldos históricos de los movimientos registrados, sin hacerlos pasar por saldos actuales.

Las fichas muestran aforos individuales (por ejemplo, `3 × 100 mL`), sin sumar soluciones distintas para inventar un volumen único. Seleccionar una solución cambia el recipiente y su tabla. Los datos de auditoría se limitan al responsable, creación y movimientos reales de la respuesta. No se inventan modificaciones.

## Límites del contrato existente

No existe un campo de solvente en el contrato de Curva/AQS; esta corrección no añade un campo que no se guardaría. El método sigue determinando los orígenes elegibles, también para el estándar interno. No se amplían compatibilidades entre métodos ni se agregan reglas de mezcla. Cada tarjeta sigue siendo una dilución con aforo independiente.

## Validación

Los resultados actuales se documentan al finalizar en `artifacts/validation/curve-aqs-final/`. Las capturas proceden del HWND de la aplicación WinUI ejecutando estas mismas vistas con fixtures en memoria. Esa evidencia visual no demuestra escritura en InterDB ni despliegue de la API.

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/validate-curve-aqs-final.ps1 -Action Start
# Misma sesión de escritorio: Forms, Responsive, Details.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/validate-curve-aqs-popups.ps1 -EvidenceFolder curve-aqs-final
```

## Resultado verificado

- Compilación final de solución y harness: **0 errores, 0 advertencias**.
- **362 pruebas correctas**, 0 fallidas y 0 omitidas: Application 134, API 46, Infrastructure 63 y Desktop 119. Las 119 pruebas Desktop se repitieron sobre el ensamblado final de `bin/Debug`, evitando el ensamblado anterior de `bin/x64/Debug`.
- PostgreSQL exclusivamente aislado: `127.0.0.1:55439/lims_stock_test`; incluye regresiones de Stock/Intermedia, transacciones, concurrencia y consumos.
- Ambos formularios a 1920×1080, 1600×900 y 1366×768, además de 1000×768 para comprobar el apilado. Escala real leída de `XamlRoot.RasterizationScale`: **1.25**. Guardar quedó dentro del área visible en los ocho casos.
- Calendarios y selectores de ambos editores abiertos mediante UI Automation a 1366×768. Los calendarios quedaron dentro de la ventana y por encima de Guardar.
- Fichas de ambos tipos, selección de la tercera solución, fichas a 1366×768, referencias largas y auditoría expandida comprobadas en WinUI.
- Exceso 110/100 mL: recipiente rojo, exceso de 10 mL, campos resaltados y guardado bloqueado. Corrección a 25/100 mL: recipiente azul, 75 mL pendientes y resultados actualizados.
- Error de servidor fixture con referencia `VISUAL-409`: un único mensaje de operación y borrador conservado. Nuevo formulario sin mensajes anteriores.

La captura inicial de tres soluciones muestra **100 − (2 + 3 + 5) = 90 mL**. La captura específica posterior del saldo compartido muestra **84 − 10 = 74 mL**, porque se tomó después de guardar los dos grupos fixture; no se reinicia ni se inventa la disponibilidad.

Capturas obligatorias (WinUI real con servicios fixture):

| Escenario | Captura |
| --- | --- |
| Nueva Curva con tres soluciones | [Curva](../artifacts/validation/curve-aqs-final/Curva-three-solutions.png) |
| Nuevo AQS con tres soluciones | [AQS](../artifacts/validation/curve-aqs-final/AQS-three-solutions.png) |
| Estándar interno de segundo origen | [Curva con estándar interno](../artifacts/validation/curve-aqs-final/Curva-internal-standard.png) |
| Aforo excedido | [110 / 100 mL](../artifacts/validation/curve-aqs-final/Curva-excess.png) |
| Ficha Curva guardada | [Ficha Curva](../artifacts/validation/curve-aqs-final/Curva-detail.png) |
| Ficha AQS guardada | [Ficha AQS](../artifacts/validation/curve-aqs-final/AQS-detail.png) |
| AQS 1366×768 | [Formulario AQS](../artifacts/validation/curve-aqs-final/AQS-form-1366x768.png) |
| Resumen del consumo compartido | [Saldo por origen](../artifacts/validation/curve-aqs-final/AQS-shared-consumption.png) |

Evidencia adicional: [referencias largas](../artifacts/validation/curve-aqs-final/Curva-trace-expanded.png), [auditoría disponible](../artifacts/validation/curve-aqs-final/Curva-audit-expanded.png), [error de servidor](../artifacts/validation/curve-aqs-final/AQS-server-error.png), [calendario](../artifacts/validation/curve-aqs-final/AQS-calendar.png), [informe consolidado](../artifacts/validation/curve-aqs-final/verification.json), [pruebas](../artifacts/validation/curve-aqs-redesign-tests.log), [Desktop final](../artifacts/validation/curve-aqs-redesign-desktop-tests.log), [compilación](../artifacts/validation/curve-aqs-redesign-build.log).

Siguen fuera de esta evidencia el despliegue, una sesión autenticada con registros de producción y escritura en InterDB. No se ha aplicado ninguna migración en esta corrección.
