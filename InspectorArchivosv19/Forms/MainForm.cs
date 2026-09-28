using System;
using System.Configuration;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using InspectorArchivos.Database;
using InspectorArchivos.Grid;
using InspectorArchivos.Services;

namespace InspectorArchivos
{
    public partial class MainForm : Form
    {
        private readonly ComparisonService _comparisonService;
        private VirtualComparisonSource _virtualSource;
        private System.Windows.Forms.Timer _debounceTimer;

        private string _currentSortColumn = "id";
        private bool _currentSortAscending = true;

        public MainForm()
        {
            InitializeComponent();

            _comparisonService = new ComparisonService();

            _debounceTimer = new System.Windows.Forms.Timer();
            _debounceTimer.Interval = 400;
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                SetStatusText("Conectando e inicializando esquemas de PostgreSQL...");

                string connString = ConfigurationManager.ConnectionStrings["PostgresConnection"]?.ConnectionString
                    ?? "Host=localhost;Database=inspector_db;Username=postgres;Password=postgres;Pooling=true;";

                DbFactory.Initialize(connString);
                await DatabaseSchemaInitializer.EnsureOptimizedSchemaAsync();

                ConfigureDataGridView();

                _virtualSource = new VirtualComparisonSource(_comparisonService, pageSize: 100);
                _virtualSource.OnDataUpdated += VirtualSource_OnDataUpdated;

                await RefreshGridAsync();

                SetStatusText($"Listo. Total de registros: {GetGridRowCount():N0}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar la aplicación: {ex.Message}", "Error de Conexión", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatusText("Error en la inicialización.");
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ConfigureDataGridView()
        {
            var grid = GetGridControl();
            if (grid == null) return;

            grid.DoubleBuffered(true);
            grid.VirtualMode = true;
            grid.AutoGenerateColumns = false;
            grid.Columns.Clear();

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "ID", DataPropertyName = "Id", Width = 70, SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOrName", HeaderText = "Nombre (Origen)", DataPropertyName = "OrName", Width = 180, SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOrPath", HeaderText = "Ruta Relativa (Origen)", DataPropertyName = "OrRelativePath", Width = 220, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colOrSize", HeaderText = "Tamaño (Origen)", DataPropertyName = "OrSize", Width = 100, SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDeName", HeaderText = "Nombre (Destino)", DataPropertyName = "DeName", Width = 180, SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDePath", HeaderText = "Ruta Relativa (Destino)", DataPropertyName = "DeRelativePath", Width = 220, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDeSize", HeaderText = "Tamaño (Destino)", DataPropertyName = "DeSize", Width = 100, SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colMatch", HeaderText = "Tipo Coincidencia", DataPropertyName = "MatchType", Width = 130, SortMode = DataGridViewColumnSortMode.Programmatic });

            grid.CellValueNeeded += DgvComparison_CellValueNeeded;
            grid.ColumnHeaderMouseClick += DgvComparison_ColumnHeaderMouseClick;
        }

        private void DgvComparison_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (_virtualSource == null || e.RowIndex >= _virtualSource.TotalRows) return;

            var row = _virtualSource.GetRow(e.RowIndex);
            if (row == null)
            {
                e.Value = "...";
                return;
            }

            e.Value = e.ColumnIndex switch
            {
                0 => row.Id,
                1 => row.OrName ?? "-",
                2 => row.OrRelativePath ?? "-",
                3 => row.OrSize.HasValue ? FormatBytes(row.OrSize.Value) : "-",
                4 => row.DeName ?? "-",
                5 => row.DeRelativePath ?? "-",
                6 => row.DeSize.HasValue ? FormatBytes(row.DeSize.Value) : "-",
                7 => row.MatchType ?? "-",
                _ => null
            };
        }

        private async void DgvComparison_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = GetGridControl();
            if (grid == null) return;

            var column = grid.Columns[e.ColumnIndex];
            if (column.SortMode == DataGridViewColumnSortMode.NotSortable) return;

            string targetProp = column.DataPropertyName;

            if (_currentSortColumn == targetProp)
            {
                _currentSortAscending = !_currentSortAscending;
            }
            else
            {
                _currentSortColumn = targetProp;
                _currentSortAscending = true;
            }

            await RefreshGridAsync();
        }

        private void txtFilter_TextChanged(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            await RefreshGridAsync();
        }

        private async Task RefreshGridAsync()
        {
            try
            {
                SetStatusText("Consultando índice PostgreSQL...");
                string filter = GetFilterText();

                await _virtualSource.InitializeOrRefreshAsync(filter, _currentSortColumn, _currentSortAscending);

                SetGridRowCount(0);
                SetGridRowCount(_virtualSource.TotalRows);
                InvalidateGrid();

                SetStatusText($"Registros filtrados: {GetGridRowCount():N0}");
            }
            catch (Exception ex)
            {
                SetStatusText($"Error al refrescar grid: {ex.Message}");
            }
        }

        private void VirtualSource_OnDataUpdated()
        {
            var grid = GetGridControl();
            if (grid == null) return;

            if (grid.InvokeRequired)
            {
                grid.BeginInvoke(new Action(() => grid.Invalidate()));
            }
            else
            {
                grid.Invalidate();
            }
        }

        private async void btnRebuildCache_Click(object sender, EventArgs e)
        {
            string srcText = GetSourceIdText();
            string tgtText = GetTargetIdText();

            if (!long.TryParse(srcText, out long sourceId) ||
                !long.TryParse(tgtText, out long targetId))
            {
                MessageBox.Show("Por favor, introduce IDs de escaneo válidos.", "Atención", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                SetControlEnabled("btnRebuildCache", false);
                Cursor = Cursors.WaitCursor;
                SetStatusText("Reconstruyendo caché de comparativa en PostgreSQL...");

                await _comparisonService.RebuildComparisonCacheAsync(sourceId, targetId);

                SetStatusText("Caché reconstruida con éxito. Actualizando vista...");
                await RefreshGridAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al reconstruir la caché: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatusText("Error al reconstruir la caché.");
            }
            finally
            {
                SetControlEnabled("btnRebuildCache", true);
                Cursor = Cursors.Default;
            }
        }

        private DataGridView GetGridControl()
        {
            if (Controls.Find("dgvComparison", true).Length > 0)
                return Controls.Find("dgvComparison", true)[0] as DataGridView;
            if (Controls.Find("dataGridView1", true).Length > 0)
                return Controls.Find("dataGridView1", true)[0] as DataGridView;
            return null;
        }

        private void SetStatusText(string text)
        {
            // 1. Buscar Label tradicional
            var ctrls = Controls.Find("lblStatus", true);
            if (ctrls.Length > 0 && ctrls[0] is Label lbl)
            {
                lbl.Text = text;
                return;
            }

            // 2. Buscar dentro de StatusStrip
            foreach (Control c in Controls)
            {
                if (c is StatusStrip statusStrip)
                {
                    if (statusStrip.Items.ContainsKey("lblStatus"))
                    {
                        statusStrip.Items["lblStatus"].Text = text;
                        return;
                    }
                    if (statusStrip.Items.Count > 0)
                    {
                        statusStrip.Items[0].Text = text;
                        return;
                    }
                }
            }
        }

        private string GetFilterText()
        {
            var ctrls = Controls.Find("txtFilter", true);
            if (ctrls.Length > 0) return ctrls[0].Text.Trim();
            ctrls = Controls.Find("txtFiltro", true);
            if (ctrls.Length > 0) return ctrls[0].Text.Trim();
            return "";
        }

        private string GetSourceIdText()
        {
            var ctrls = Controls.Find("txtScanSourceId", true);
            if (ctrls.Length > 0) return ctrls[0].Text;
            return "0";
        }

        private string GetTargetIdText()
        {
            var ctrls = Controls.Find("txtScanTargetId", true);
            if (ctrls.Length > 0) return ctrls[0].Text;
            return "0";
        }

        private int GetGridRowCount()
        {
            var grid = GetGridControl();
            return grid?.RowCount ?? 0;
        }

        private void SetGridRowCount(int count)
        {
            var grid = GetGridControl();
            if (grid != null) grid.RowCount = count;
        }

        private void InvalidateGrid()
        {
            var grid = GetGridControl();
            grid?.Invalidate();
        }

        private void SetControlEnabled(string controlName, bool enabled)
        {
            var ctrls = Controls.Find(controlName, true);
            if (ctrls.Length > 0) ctrls[0].Enabled = enabled;
        }

        private string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            return $"{dblSByte:0.##} {suffix[i]}";
        }
    }

    public static class DataGridViewExtensions
    {
        public static void DoubleBuffered(this DataGridView dgv, bool setting)
        {
            var pi = typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            pi?.SetValue(dgv, setting, null);
        }
    }
}