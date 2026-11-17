# Aforo común en Curva y AQS

El formulario permite escribir una sola vez el aforo común, encima de las soluciones. Cada solución nueva lo utiliza automáticamente y muestra el valor recibido. No se precarga ningún volumen de preparación.

«Usar aforo diferente» permite introducir un volumen y una unidad propios para una solución. Los cambios posteriores del aforo común respetan esas excepciones. Al desactivar la opción, la solución vuelve al valor común actual.

El aforo común representa el volumen final de cada solución por separado. No suma los aforos ni mezcla sus orígenes. El cliente envía el volumen y la unidad efectivos de cada solución mediante el contrato existente; los cálculos, el estándar interno, los consumos, los saldos y la persistencia siguen utilizando los servicios actuales. No requiere cambios de API ni migraciones adicionales.

La selección del resultado mantiene el mismo título en el selector y en la ficha, incluso después de agregar o eliminar soluciones y cambiar su numeración. Cambiar el aforo común mantiene la solución que se estaba consultando.

Implementación: `Frontend/Lims.Desktop/ViewModels/SolutionGroupViewModel.cs`, `Frontend/Lims.Desktop/Views/SolutionGroupEditorDialog.xaml` y actualización del título seleccionado en `Frontend/Lims.DesignSystem/Controls/LimsSelect.cs`.

Verificación: `Tests/Frontend/Lims.Desktop.Tests/SolutionGroupViewModelTests.cs` cubre propagación, excepciones, retorno al común, mL/L, valores inválidos, exceso del aforo combinado con estándar interno, reinicio del borrador y reintento idempotente. `scripts/validate-common-aforo.ps1` ejercita los controles WinUI con datos ficticios y genera evidencia en `artifacts/validation/common-aforo/`. Esa evidencia no demuestra persistencia en InterDB ni una sesión real del servicio desplegado.

La compilación Debug local se actualizó y se registró con WinAppCLI `--no-launch`, conservando los datos de la aplicación. Se verificó que los SHA256 de `Lims.Desktop.dll`, `Lims.DesignSystem.dll` y `resources.pri` del AppX registrado coinciden con la compilación nueva. Evidencia: `artifacts/validation/common-aforo/registered-client.json`. La actualización del paquete local no verifica el flujo autenticado del servidor.
