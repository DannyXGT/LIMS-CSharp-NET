# Arquitectura de Materiales de Referencia

## Arquitectura encontrada

La solución existente es un monolito modular en una sola `Lims.sln`, con dependencias hacia adentro:

| Proyecto | Responsabilidad observada |
|---|---|
| `Lims.Desktop` | WinUI 3, sesión local, `HttpClient`, ViewModels y navegación |
| `Lims.DesignSystem` | tokens, brushes, tipografía y estilos compartidos |
| `Lims.Api` | composición, autenticación/autorización, rate limiting, observabilidad y endpoints |
| `Lims.Application` | casos de uso y puertos, sin EF ni WinUI |
| `Lims.Domain` | entidades y reglas invariantes |
| `Lims.Infrastructure` | EF Core/Npgsql, repositorios y servicios técnicos |
| `Lims.Contracts` | DTO, errores y nombres de permisos compartidos |

La autenticación aprobada ya resuelve Login → API → InterDB, emite el perfil con permisos y conserva el refresh token mediante la infraestructura existente. El nuevo módulo no introduce una identidad paralela. Las tablas heredadas `usuarios`, `roles`, `departamentos` y `usuarios_permisos` siguen fuera de migraciones EF.

La revisión de Trazalab cubrió `REVISION_TECNICA_TRAZALAB.md`, `MANUAL_USUARIO.md`, el diagrama Draw.io y los formularios/lógica Python de Estándares, Stock, Intermedias, Curvas y AQS. Se extrajeron campos, validaciones y 21 reglas computacionales. No se reutilizaron clases PySide6, SQL, tablas ni estructura del programa anterior.

## Diseño objetivo

Materiales de Referencia es un módulo del monolito modular existente; `Trazalab` es únicamente su identidad visible e histórica.

```text
WinUI 3
  -> HttpClient autorizado
  -> ASP.NET Core /api/reference-materials
  -> ReferenceMaterialService
  -> IReferenceMaterialRepository
  -> EF Core / PostgreSQL
```

No existe acceso directo desde WinUI a PostgreSQL. La identidad y sesión son las ya aprobadas: el módulo consume `UserProfile`, el claim `sub` y los permisos existentes sin modificar login, JWT, refresh tokens, PasswordVault, Argon2 ni rate limiting.

La autorización se aplica dos veces: la UI deshabilita acciones no disponibles y la API exige una policy explícita por endpoint. El actor de creación, edición o archivo proviene exclusivamente del `sub` autenticado; nunca del payload del cliente.

`ReferenceMaterial` es el agregado inicial. El repositorio abstrae EF Core y la API devuelve contratos paginados. La versión `Guid` implementa concurrencia optimista en edición/archivo. Las reglas repetibles están en dominio y las restricciones críticas también existen como checks PostgreSQL.

## Primera vertical

- MainShell y navegación profesional.
- Resumen sin métricas ficticias.
- Estándares: listado paginado, búsqueda, filtros, detalle, alta, edición, archivado y reemplazo explícito.
- Saldo inicial real en la misma unidad de presentación, protegido por CHECK y preparado para el descuento transaccional de Stock.
- API y UI protegidas por permisos explícitos.
- EF Core controla el esquema nuevo mediante `CreateReferenceMaterials`.

La navegación sólo expone Estándares mientras las siguientes verticales no tengan un caso de uso completo. No se muestran pantallas placeholder como funcionalidad.

Endpoints de esta vertical:

| Método y ruta | Permiso |
|---|---|
| `GET /api/reference-materials` | `reference_materials.view` |
| `GET /api/reference-materials/{id}` | `reference_materials.view` |
| `POST /api/reference-materials` | `reference_materials.create` |
| `PUT /api/reference-materials/{id}` | `reference_materials.edit` |
| `POST /api/reference-materials/{id}/archive` | `reference_materials.archive` |
| `POST /api/reference-materials/{id}/replacement` | `reference_materials.create` + `reference_materials.archive` |

No existe endpoint `DELETE`.

## Límites futuros

El motor único de inventario y consumos será una fase independiente. El Audit Trail debe ser infraestructura global del LIMS. Las fórmulas de preparaciones no deben vivir en ViewModels y se incorporarán mediante un motor versionable cuando esas fases comiencen.

El contrato de tablas para Stock, Intermedias, Curvas y AQS está definido en `docs/materiales-referencia-db.md` y `artifacts/sql/materiales_referencia.sql`. Cada vertical debe materializar ese contrato con una sola autoridad de saldo y una estrategia transaccional/concurrente, en lugar de reproducir cuatro algoritmos históricos.
