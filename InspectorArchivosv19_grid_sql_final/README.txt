OPTIMIZACION SQL DEL GRID DE COMPARACION

El cuello de botella estaba en ApplyFilterAndSort(): recorria _allRows en C# y despues ordenaba con OrderBy/ThenBy. Ahora PostgreSQL hace WHERE + ORDER BY.

Incluye:
- InspectorArchivosv19/Database/ComparisonGridQueryService.cs
- sql/01_comparison_grid_cache.sql
- MainForm.cs.patch.py

El parche esta preparado contra la MainForm.cs actual del repositorio. Ejecutar desde la raiz:
  python MainForm.cs.patch.py

Repository.cs no necesita cambios para esta fase: BuildComparison() ya realiza la comparacion mediante PostgreSQL. _allRows se conserva para no cambiar las operaciones de copiar/mover/eliminar.
