# Decisiones abiertas

Para Estándares quedó cerrado:

- métodos: 23 valores de `tabla_metodo`;
- unidades activas: `mL`, `g`, `mg`, `µg`;
- ubicaciones físicas: `Laboratorio`, `Bodega`;
- temperatura: texto directo, sin catálogo ni FK;
- registros exclusivamente nuevos, con creador y modificador obligatorios;
- sin `legacy_id` ni migración de registros operativos.

Sigue requiriendo confirmación del laboratorio la política de identidad/reemplazo basada en CAS, catálogo, lote y múltiples lotes activos. El reemplazo automático continúa deshabilitado.

Stock, Intermedias, Curvas, AQS, Alertas, Compras e Inventario se decidirán en fases posteriores.
