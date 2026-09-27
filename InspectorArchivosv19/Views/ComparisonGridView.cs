using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using InspectorArchivosv19.Data;
using InspectorArchivosv19.Models;

namespace InspectorArchivosv19.Views
{
    public partial class ComparisonGridView : UserControl
    {
        private readonly ComparisonRepository _repository;
        private long _originScanId;
        private long _destinationScanId;

        private int _currentPage = 1;
        private int _pageSize = 500;
        private int _totalRows = 0;
        private string _sortColumn = "rel_path";
        private bool _sortAscending = true;
        private string _filterText = string.Empty;
        private string _statusFilter = "ALL";

        private List<ComparisonRow> _currentPageRows = new List<ComparisonRow>();

        public ComparisonGridView(ComparisonRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            InitializeComponent();
        }

        public async Task LoadComparisonDataAsync(long originScanId, long destinationScanId, bool forceRebuild = false)
        {
            _originScanId = originScanId;
            _destinationScanId = destinationScanId;

            bool cacheExists = await _repository.IsCacheValidAsync(originScanId, destinationScanId);
            if (!cacheExists || forceRebuild)
            {
                await _repository.PopulateCacheInDatabaseAsync(originScanId, destinationScanId);
            }

            _currentPage = 1;
            await RefreshGridPageAsync();
        }

        private async Task RefreshGridPageAsync()
        {
            _totalRows = await _repository.GetTotalCountAsync(_originScanId, _destinationScanId, _filterText, _statusFilter);

            int offset = (_currentPage - 1) * _pageSize;
            _currentPageRows = await _repository.GetPagedRowsAsync(
                _originScanId,
                _destinationScanId,
                offset,
                _pageSize,
                _sortColumn,
                _sortAscending,
                _filterText,
                _statusFilter
            );

            dataGridViewComparison.DataSource = null;
            dataGridViewComparison.DataSource = _currentPageRows;

            UpdatePaginationControls();
        }

        private void UpdatePaginationControls()
        {
            int totalPages = (int)Math.Ceiling((double)_totalRows / _pageSize);
            if (totalPages == 0) totalPages = 1;

            if (lblPageInfo != null)
                lblPageInfo.Text = $"Página {_currentPage} de {totalPages} (Total: {_totalRows:N0} filas)";

            if (btnPrevPage != null)
                btnPrevPage.Enabled = _currentPage > 1;

            if (btnNextPage != null)
                btnNextPage.Enabled = _currentPage < totalPages;
        }

        private async void btnNextPage_Click(object sender, EventArgs e)
        {
            _currentPage++;
            await RefreshGridPageAsync();
        }

        private async void btnPrevPage_Click(object sender, EventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await RefreshGridPageAsync();
            }
        }

        private async void txtFilter_TextChanged(object sender, EventArgs e)
        {
            if (txtFilter != null)
            {
                _filterText = txtFilter.Text.Trim();
                _currentPage = 1;
                await RefreshGridPageAsync();
            }
        }

        private async void cmbStatusFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbStatusFilter != null && cmbStatusFilter.SelectedItem != null)
            {
                _statusFilter = cmbStatusFilter.SelectedItem.ToString();
                _currentPage = 1;
                await RefreshGridPageAsync();
            }
        }

        private async void dataGridViewComparison_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.ColumnIndex < dataGridViewComparison.Columns.Count)
            {
                string newSortColumn = dataGridViewComparison.Columns[e.ColumnIndex].DataPropertyName;
                if (string.Equals(_sortColumn, newSortColumn, StringComparison.OrdinalIgnoreCase))
                {
                    _sortAscending = !_sortAscending;
                }
                else
                {
                    _sortColumn = newSortColumn;
                    _sortAscending = true;
                }

                await RefreshGridPageAsync();
            }
        }

        public async Task<List<ComparisonRow>> GetSelectedOrAllRowsForActionAsync(bool selectedOnly = false)
        {
            if (selectedOnly)
            {
                var selectedRows = new List<ComparisonRow>();
                foreach (DataGridViewRow row in dataGridViewComparison.SelectedRows)
                {
                    if (row.DataBoundItem is ComparisonRow compRow)
                    {
                        selectedRows.Add(compRow);
                    }
                }
                return selectedRows;
            }

            return await _repository.GetPagedRowsAsync(
                _originScanId,
                _destinationScanId,
                0,
                _totalRows,
                _sortColumn,
                _sortAscending,
                _filterText,
                _statusFilter
            );
        }
    }
}
