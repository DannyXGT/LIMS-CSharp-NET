# Fórmulas heredadas bajo control

La revisión estática de Trazalab confirmó 21 reglas. En la primera vertical sólo se implementa F-01; las demás quedan como referencia para las fases de inventario y preparaciones.

| ID | Regla heredada | Estado .NET |
|---|---|---|
| F-01 | `total = presentación × unidades` | Implementada con `decimal` |
| F-02 | `mg / 1000 -> g` | Pendiente del motor de unidades |
| F-03 | `µg / 1 000 000 -> g` | Pendiente del motor de unidades |
| F-04 | `kg × 1000 -> g` | Pendiente del motor de unidades |
| F-05 | `L × 1000 -> mL` | Pendiente del motor de unidades |
| F-06 | conversión inversa desde unidad base | Pendiente |
| F-07 | `max(total - usado, 0)` | Pendiente de movimientos |
| F-08 | `(restante / total) × 100` | Pendiente de movimientos |
| F-09 | `(V / 1000) × 100 / pureza` | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-10 | `F-09 / 5` para Disperse Dyes | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-11 | `((masa × 1000)/(V/1000)) × (pureza/100)` | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-12 | ingreso/preparación + 366 días | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-13 | `Cfinal = Corigen × Vtomado / Vaforo` | Pendiente de Preparaciones |
| F-14 | `usado = n × Vtomado` | Pendiente de Preparaciones |
| F-15 | `max(Vfinal - usado, 0)` | Pendiente de movimientos |
| F-16 | porcentaje visual de saldo | Pendiente; no será verdad persistida |
| F-17 | consumo de familia por código/origen | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-18 | saldo global intermedia | Pendiente del motor único de consumos |
| F-19 | preparación + 188 días | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-20 | preparación + 2 días | REQUIERE CONFIRMACIÓN DEL LABORATORIO |
| F-21 | porcentaje antiguo sobre `peso_estimado` | No se migrará sin evidencia de uso vigente |

Fuente contrastada: `REVISION_TECNICA_TRAZALAB.md`, `logic/logica.py`, `form_ingreso_stock.py`, `form_ingreso_intermedio.py`, `FormIngresoIntermediaWorks.py` y `form_AQS.py` del proyecto histórico.
