using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InspectorArchivos.Services;

namespace InspectorArchivos.Grid
{
    public class VirtualComparisonSource
    {
        private readonly ComparisonService _service;
        private readonly int _pageSize;
        private readonly Dictionary<int, List<ComparisonRow>> _pageCache = new();
        private readonly HashSet<int> _pendingPages = new();

        private string _filterText = "";
        private string _sortColumn = "id";
        private bool _sortAscending = true;

        public int TotalRows { get; private set; }
        public event Action OnDataUpdated;

        public VirtualComparisonSource(ComparisonService service, int pageSize = 100)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _pageSize = pageSize > 0 ? pageSize : 100;
        }

        public async Task InitializeOrRefreshAsync(string filterText = "", string sortColumn = "id", bool ascending = true)
        {
            _filterText = filterText ?? "";
            _sortColumn = sortColumn ?? "id";
            _sortAscending = ascending;
            _pageCache.Clear();
            _pendingPages.Clear();

            var firstPage = await _service.GetPagedRowsAsync(0, _pageSize, _filterText, _sortColumn, _sortAscending);
            TotalRows = firstPage.TotalCount;
            _pageCache[0] = firstPage.Items;

            OnDataUpdated?.Invoke();
        }

        public ComparisonRow GetRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= TotalRows) return null;

            int pageIndex = rowIndex / _pageSize;
            int itemIndex = rowIndex % _pageSize;

            if (_pageCache.TryGetValue(pageIndex, out var page))
            {
                if (itemIndex < page.Count) return page[itemIndex];
                return null;
            }

            if (!_pendingPages.Contains(pageIndex))
            {
                _pendingPages.Add(pageIndex);
                _ = FetchPageInBackgroundAsync(pageIndex);
            }

            return null;
        }

        private async Task FetchPageInBackgroundAsync(int pageIndex)
        {
            try
            {
                var result = await _service.GetPagedRowsAsync(pageIndex, _pageSize, _filterText, _sortColumn, _sortAscending);
                _pageCache[pageIndex] = result.Items;
            }
            catch
            {
                // Resiliencia ante desconexiones temporales
            }
            finally
            {
                _pendingPages.Remove(pageIndex);
                OnDataUpdated?.Invoke();
            }
        }
    }
}