using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using InspectorArchivos.Database;

namespace InspectorArchivos.Forms
{
    /// <summary>
    /// Ventana de solo lectura que muestra toda la información de las tablas
    /// "Scans" y "ScanErrors", ordenadas por defecto del último escaneo al más
    /// antiguo. Haciendo doble clic sobre el encabezado de cualquier columna de
    /// la cuadrícula de Scans, esta se reordena por esa columna (alternando
    /// ascendente/descendente en cada doble clic).
    /// </summary>
    public class ScansErrorsForm : Form
    {
        private readonly DataGridView _gridScans = new DataGridView();
        private readonly DataGridView _gridErrors = new DataGridView();
        private readonly DataTable _scansTable = new DataTable();
        private readonly DataTable _errorsTable = new DataTable();

        private string _scansSortColumn;
        private bool _scansSortAscending;

        public ScansErrorsForm(Repository repo)
        {
            Text = "Scans y errores de escaneo";
            Width = 1300;
            Height = 780;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 500);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 380,
                Panel1MinSize = 150,
                Panel2MinSize = 150
            };

            var lblScans = new Label
            {
                Text = "Scans (del último al más antiguo — doble clic en un encabezado de columna para ordenar)",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 22,
                Padding = new Padding(4, 5, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            };
            ConfigureGrid(_gridScans);
            _gridScans.Dock = DockStyle.Fill;
            _gridScans.ColumnHeaderMouseDoubleClick += GridScans_ColumnHeaderMouseDoubleClick;

            var panelScans = new Panel { Dock = DockStyle.Fill };
            panelScans.Controls.Add(_gridScans);
            panelScans.Controls.Add(lblScans);
            split.Panel1.Controls.Add(panelScans);

            var lblErrors = new Label
            {
                Text = "Errores de escaneo (ScanErrors, del último escaneo al más antiguo)",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 22,
                Padding = new Padding(4, 5, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            };
            ConfigureGrid(_gridErrors);
            _gridErrors.Dock = DockStyle.Fill;

            var panelErrors = new Panel { Dock = DockStyle.Fill };
            panelErrors.Controls.Add(_gridErrors);
            panelErrors.Controls.Add(lblErrors);
            split.Panel2.Controls.Add(panelErrors);

            Controls.Add(split);

            CargarDatos(repo);
        }

        private static void ConfigureGrid(DataGridView grid)
        {
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = SystemColors.Window;
            grid.AllowUserToOrderColumns = true;
        }

        private void CargarDatos(Repository repo)
        {
            // --- Tabla Scans ---
            _scansTable.Columns.Add("Id", typeof(long));
            _scansTable.Columns.Add("Guid", typeof(string));
            _scansTable.Columns.Add("RootPaths", typeof(string));
            _scansTable.Columns.Add("Scope", typeof(string));
            _scansTable.Columns.Add("StartedAt", typeof(DateTime));
            _scansTable.Columns.Add("FinishedAt", typeof(DateTime));
            _scansTable.Columns.Add("FileCount", typeof(long));
            _scansTable.Columns.Add("FolderCount", typeof(long));
            _scansTable.Columns.Add("ErrorCount", typeof(long));
            _scansTable.Columns.Add("Status", typeof(string));
            _scansTable.Columns.Add("Notes", typeof(string));

            foreach (var s in repo.GetAllScans())
            {
                var row = _scansTable.NewRow();
                row["Id"] = s.Id;
                row["Guid"] = s.Guid.ToString();
                row["RootPaths"] = s.RootPaths;
                row["Scope"] = s.ScopeLabel;
                row["StartedAt"] = s.StartedAt.ToLocalTime();
                row["FinishedAt"] = s.FinishedAt == DateTime.MinValue
                    ? (object)DBNull.Value
                    : s.FinishedAt.ToLocalTime();
                row["FileCount"] = s.FileCount;
                row["FolderCount"] = s.FolderCount;
                row["ErrorCount"] = s.ErrorCount;
                row["Status"] = s.StatusLabel;
                row["Notes"] = (object)s.Notes ?? DBNull.Value;
                _scansTable.Rows.Add(row);
            }

            _gridScans.DataSource = _scansTable;
            SetHeader(_gridScans, "Id", "Id");
            SetHeader(_gridScans, "Guid", "Guid");
            SetHeader(_gridScans, "RootPaths", "Carpetas");
            SetHeader(_gridScans, "Scope", "Ámbito");
            SetHeader(_gridScans, "StartedAt", "Inicio");
            SetHeader(_gridScans, "FinishedAt", "Fin");
            SetHeader(_gridScans, "FileCount", "Nº Archivos");
            SetHeader(_gridScans, "FolderCount", "Nº Carpetas");
            SetHeader(_gridScans, "ErrorCount", "Nº Errores");
            SetHeader(_gridScans, "Status", "Estado");
            SetHeader(_gridScans, "Notes", "Notas");
            FormatDateColumn(_gridScans, "StartedAt");
            FormatDateColumn(_gridScans, "FinishedAt");
            // La ordenación por clic sencillo se desactiva: se ordena solo con doble
            // clic sobre el encabezado (ver GridScans_ColumnHeaderMouseDoubleClick).
            foreach (DataGridViewColumn c in _gridScans.Columns)
                c.SortMode = DataGridViewColumnSortMode.Programmatic;

            // --- Tabla ScanErrors ---
            _errorsTable.Columns.Add("Id", typeof(long));
            _errorsTable.Columns.Add("ScanId", typeof(long));
            _errorsTable.Columns.Add("Path", typeof(string));
            _errorsTable.Columns.Add("Error", typeof(string));
            _errorsTable.Columns.Add("OccurredAt", typeof(DateTime));

            foreach (var e in repo.GetAllScanErrors())
            {
                var row = _errorsTable.NewRow();
                row["Id"] = e.Id;
                row["ScanId"] = e.ScanId;
                row["Path"] = (object)e.Path ?? DBNull.Value;
                row["Error"] = e.Error;
                row["OccurredAt"] = e.OccurredAt.ToLocalTime();
                _errorsTable.Rows.Add(row);
            }

            _gridErrors.DataSource = _errorsTable;
            SetHeader(_gridErrors, "Id", "Id");
            SetHeader(_gridErrors, "ScanId", "Id Scan");
            SetHeader(_gridErrors, "Path", "Ruta");
            SetHeader(_gridErrors, "Error", "Error");
            SetHeader(_gridErrors, "OccurredAt", "Fecha/hora");
            FormatDateColumn(_gridErrors, "OccurredAt");
        }

        private static void SetHeader(DataGridView grid, string column, string header)
        {
            if (grid.Columns.Contains(column)) grid.Columns[column].HeaderText = header;
        }

        private static void FormatDateColumn(DataGridView grid, string column)
        {
            if (grid.Columns.Contains(column))
                grid.Columns[column].DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
        }

        /// <summary>
        /// Ordena la cuadrícula de Scans por la columna sobre la que se ha hecho
        /// doble clic. Un segundo doble clic sobre la misma columna invierte el
        /// sentido (ascendente/descendente).
        /// </summary>
        private void GridScans_ColumnHeaderMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= _gridScans.Columns.Count) return;
            string colName = _gridScans.Columns[e.ColumnIndex].DataPropertyName;
            if (string.IsNullOrEmpty(colName)) return;

            if (_scansSortColumn == colName)
                _scansSortAscending = !_scansSortAscending;
            else
            {
                _scansSortColumn = colName;
                _scansSortAscending = true;
            }

            _scansTable.DefaultView.Sort = $"[{colName}] {(_scansSortAscending ? "ASC" : "DESC")}";
        }
    }
}

