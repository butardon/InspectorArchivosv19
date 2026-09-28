using System;
using System.Collections.Generic;
using InspectorArchivos.Database;
using InspectorArchivos.Models;

namespace InspectorArchivos.Database
{
    /// <summary>
    /// Fuente de datos virtual para el DataGridView en modo VirtualMode.
    /// Carga páginas de datos desde PostgreSQL bajo demanda, evitando
    /// cargar las 540K+ filas de golpe en memoria.
    ///
    /// Uso:
    ///   var source = new VirtualComparisonSource(_gridSql, originId, destId,
    ///       showOrigin, showDest, textFilters, fromFilters, toFilters, ordering);
    ///   _grid.RowCount = source.TotalCount;
    ///   // En Grid_CellValueNeeded: source.GetRow(e.RowIndex)
    /// </summary>
    public sealed class VirtualComparisonSource
    {
        private readonly ComparisonGridQueryService _service;
        private readonly long _originScanId;
        private readonly long _destinationScanId;
        private readonly bool _showOrigin;
        private readonly bool _showDestination;
        private readonly IDictionary<string, string> _textFilters;
        private readonly IDictionary<string, string> _fromFilters;
        private readonly IDictionary<string, string> _toFilters;
        private readonly IList<(string col, bool desc)> _ordering;

        private readonly int _pageSize;
        private readonly Dictionary<int, List<ComparisonRow>> _pageCache = new();
        private int _cachedPageCount = -1;

        /// <summary>Número total de filas que cumplen los filtros.</summary>
        public int TotalCount { get; }

        public VirtualComparisonSource(
            ComparisonGridQueryService service,
            long originScanId,
            long destinationScanId,
            bool showOrigin,
            bool showDestination,
            IDictionary<string, string> textFilters,
            IDictionary<string, string> fromFilters,
            IDictionary<string, string> toFilters,
            IList<(string col, bool desc)> ordering,
            int pageSize = 2000)
        {
            _service = service;
            _originScanId = originScanId;
            _destinationScanId = destinationScanId;
            _showOrigin = showOrigin;
            _showDestination = showDestination;
            _textFilters = textFilters;
            _fromFilters = fromFilters;
            _toFilters = toFilters;
            _ordering = ordering;
            _pageSize = pageSize;

            // Contar el total de filas (consulta COUNT rápida)
            TotalCount = _service.QueryCount(
                _originScanId, _destinationScanId,
                _showOrigin, _showDestination,
                _textFilters, _fromFilters, _toFilters);
        }

        /// <summary>
        /// Obtiene la fila en el índice global especificado.
        /// Carga la página correspondiente bajo demanda y la cachea.
        /// </summary>
        public ComparisonRow GetRow(int index)
        {
            if (index < 0 || index >= TotalCount) return null;

            int page = index / _pageSize;

            if (!_pageCache.TryGetValue(page, out var rows))
            {
                int offset = page * _pageSize;
                rows = _service.QueryPaged(
                    _originScanId, _destinationScanId,
                    _showOrigin, _showDestination,
                    _textFilters, _fromFilters, _toFilters,
                    _ordering, offset, _pageSize);

                _pageCache[page] = rows;
            }

            int localIndex = index % _pageSize;
            if (localIndex < rows.Count)
                return rows[localIndex];

            return null;
        }

        /// <summary>
        /// Devuelve todas las filas seleccionadas (para operaciones de copia/mover/eliminar).
        /// Carga todas las páginas necesarias.
        /// </summary>
        public List<ComparisonRow> GetAllRows()
        {
            var all = new List<ComparisonRow>(TotalCount);
            for (int page = 0; page * _pageSize < TotalCount; page++)
            {
                if (!_pageCache.TryGetValue(page, out var rows))
                {
                    int offset = page * _pageSize;
                    rows = _service.QueryPaged(
                        _originScanId, _destinationScanId,
                        _showOrigin, _showDestination,
                        _textFilters, _fromFilters, _toFilters,
                        _ordering, offset, _pageSize);
                    _pageCache[page] = rows;
                }
                all.AddRange(rows);
            }
            return all;
        }

        /// <summary>Limpia la caché de páginas (al cambiar filtros u ordenación).</summary>
        public void ClearCache() => _pageCache.Clear();
    }
}
