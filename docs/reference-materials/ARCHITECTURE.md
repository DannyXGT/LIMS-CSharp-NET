# Arquitectura de Estándares

La vertical conserva la arquitectura del LIMS:

`WinUI 3 → ASP.NET Core API → Application → Domain/Infrastructure → PostgreSQL`

WinUI consume exclusivamente la API. `ReferenceMaterial` navega a `ReferenceMethod`, `ReferenceUnit` y `ReferenceLocation`; la temperatura de almacenamiento es texto propio del material. El repositorio sólo resuelve opciones activas para altas y ediciones.

La API expone listado, detalle, alta, edición, reemplazo y archivo, además de catálogos activos. `Guid Version` mantiene la concurrencia optimista. Autenticación, usuarios y permisos existentes no se modifican.

Stock, Intermedias, Curvas, AQS, Alertas, Compras e Inventario están fuera de esta fase.
