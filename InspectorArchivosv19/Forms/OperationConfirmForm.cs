using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace InspectorArchivos.Forms
{
    /// <summary>
    /// Un elemento (un lado -origen o destino- de una fila de la comparativa)
    /// implicado en una operación de Copiar/Mover pendiente de confirmar.
    /// </summary>
    public class OperationConfirmItem
    {
        /// <summary>Ruta completa del archivo de donde procede.</summary>
        public string Origen { get; set; }
        /// <summary>Ruta completa donde se generará el archivo (vista previa).</summary>
        public string Destino { get; set; }
        /// <summary>"Pendiente" si se va a procesar, o el motivo por el que se omitirá.</summary>
        public string Estado { get; set; }
        /// <summary>True si el elemento tiene un destino calculado y se procesará.</summary>
        public bool Procesable { get; set; } = true;
    }

    /// <summary>
    /// Ventana de confirmación mostrada antes de ejecutar una operación de
    /// Copiar o Mover. Muestra, para cada archivo implicado, la ruta completa de
    /// origen (izquierda) y la ruta completa de destino que se generará
    /// (derecha), para poder comprobar que los archivos afectados son los
    /// esperados antes de confirmar o cancelar la operación.
    ///
    /// Las columnas se pueden ordenar ascendente/descendente haciendo doble
    /// clic sobre su encabezado (alternando el sentido en cada doble clic), y
    /// el valor de cualquier celda se puede copiar al portapapeles con el menú
    /// contextual "Copiar valor".
    /// </summary>
    public class OperationConfirmForm : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly DataTable _table = new DataTable();
        private readonly Button _btnCancelar = new Button();
        private readonly Button _btnConfirmar = new Button();
        private readonly Label _lblResumen = new Label();
        private readonly CheckBox _chkRevisado = new CheckBox();

        private string _sortColumn;
        private bool _sortAscending = true;

        /// <summary>
        /// Estado del checkbox "Revisado" en el momento de cerrar el formulario. Solo
        /// tiene sentido consultarlo cuando <see cref="Form.ShowDialog()"/> devolvió
        /// <see cref="DialogResult.OK"/> (es decir, se confirmó la operación): el
        /// llamador debe marcar en Historial 'S' si es true, o 'N' si es false. Si se
        /// cancela la operación, no debe generarse ninguna marca en Historial.
        /// </summary>
        public bool Revisado => _chkRevisado.Checked;

        public OperationConfirmForm(string operationName, IReadOnlyList<OperationConfirmItem> items)
        {
            Text = $"Confirmar {operationName?.ToLowerInvariant()}";
            Width = 1200;
            Height = 700;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 420);
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lblTitulo = new Label
            {
                Text = $"Se va a {operationName?.ToLowerInvariant()} {items.Count:N0} archivo(s). " +
                       "Origen a la izquierda, destino que se generará a la derecha. " +
                       "Revise la lista y confirme o cancele la operación.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                Padding = new Padding(6, 8, 6, 0),
                Font = new Font(Font, FontStyle.Bold)
            };

            // Checkbox "Revisado", en la zona superior derecha del formulario, junto al
            // título. Al confirmar (Copiar/Mover/Eliminar) su estado determina la marca
            // ('S' o 'N') que se grabará en Historial.Revisado para los archivos
            // implicados; si se cancela, no se graba ninguna marca.
            _chkRevisado.Text = "Revisado";
            _chkRevisado.AutoSize = true;
            _chkRevisado.Checked = true;
            _chkRevisado.Dock = DockStyle.Right;
            _chkRevisado.TextAlign = ContentAlignment.MiddleRight;
            _chkRevisado.Padding = new Padding(0, 8, 10, 0);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            topPanel.Controls.Add(lblTitulo);
            topPanel.Controls.Add(_chkRevisado);

            int noProcesables = 0;
            foreach (var it in items) if (!it.Procesable) noProcesables++;

            _lblResumen.Dock = DockStyle.Top;
            _lblResumen.AutoSize = false;
            _lblResumen.Height = 22;
            _lblResumen.Padding = new Padding(6, 0, 0, 4);
            _lblResumen.Text = noProcesables == 0
                ? $"Total: {items.Count:N0} archivo(s), todos con destino calculado correctamente."
                : $"Total: {items.Count:N0} archivo(s); {noProcesables:N0} no se procesará(n) (ver columna Estado, en rojo).";

            ConfigureGrid();

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            _btnCancelar.Text = "Cancelar";
            _btnCancelar.AutoSize = true;
            _btnCancelar.Padding = new Padding(10, 4, 10, 4);
            _btnCancelar.DialogResult = DialogResult.Cancel;

            _btnConfirmar.Text = "Confirmar";
            _btnConfirmar.AutoSize = true;
            _btnConfirmar.Padding = new Padding(10, 4, 10, 4);
            _btnConfirmar.DialogResult = DialogResult.OK;

            var buttonsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 10, 10, 0)
            };
            buttonsFlow.Controls.Add(_btnConfirmar);
            buttonsFlow.Controls.Add(_btnCancelar);
            bottomPanel.Controls.Add(buttonsFlow);

            Controls.Add(_grid);
            Controls.Add(bottomPanel);
            Controls.Add(_lblResumen);
            Controls.Add(topPanel);

            AcceptButton = _btnConfirmar;
            CancelButton = _btnCancelar;

            CargarDatos(items);
        }

        private static readonly HashSet<string> _pathColumns = new HashSet<string> { "origen", "destino" };

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.AllowUserToOrderColumns = true;
            _grid.ColumnHeaderMouseDoubleClick += Grid_ColumnHeaderMouseDoubleClick;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellPainting += Grid_CellPainting;

            var ctx = new ContextMenuStrip();
            var miCopy = new ToolStripMenuItem("Copiar valor");
            miCopy.Click += (s, e) => CopyCurrentCellValue();
            ctx.Items.Add(miCopy);
            _grid.ContextMenuStrip = ctx;
        }

        private void CargarDatos(IReadOnlyList<OperationConfirmItem> items)
        {
            _table.Columns.Add("origen", typeof(string));
            _table.Columns.Add("destino", typeof(string));
            _table.Columns.Add("Estado", typeof(string));

            foreach (var it in items)
            {
                var row = _table.NewRow();
                row["origen"] = it.Origen ?? "";
                row["destino"] = it.Destino ?? "";
                row["Estado"] = it.Estado ?? "";
                _table.Rows.Add(row);
            }

            _grid.DataSource = _table;

            _grid.Columns["origen"].HeaderText = "Origen (ruta completa)";
            _grid.Columns["destino"].HeaderText = "Destino (ruta completa)";
            _grid.Columns["Estado"].HeaderText = "Estado";

            _grid.Columns["origen"].Width = 460;
            _grid.Columns["destino"].Width = 460;
            _grid.Columns["Estado"].Width = 220;

            foreach (var pathCol in _pathColumns)
                _grid.Columns[pathCol].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            // La ordenación por clic sencillo se desactiva: se ordena solo con doble
            // clic sobre el encabezado (ver Grid_ColumnHeaderMouseDoubleClick), que
            // alterna ascendente/descendente en cada doble clic sobre la misma columna.
            foreach (DataGridViewColumn c in _grid.Columns)
                c.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        private void Grid_ColumnHeaderMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count) return;
            string colName = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (string.IsNullOrEmpty(colName)) return;

            if (_sortColumn == colName)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = colName;
                _sortAscending = true;
            }

            _table.DefaultView.Sort = $"[{colName}] {(_sortAscending ? "ASC" : "DESC")}";
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            if (_grid.Rows[e.RowIndex].DataBoundItem is not DataRowView view) return;
            string estado = view["Estado"] as string;
            if (!string.IsNullOrEmpty(estado) && estado != "Pendiente")
                e.CellStyle.ForeColor = Color.Firebrick;
        }

        /// <summary>
        /// Para las columnas Origen/Destino, dibuja el texto a mano igual que en la
        /// cuadrícula comparativa principal (ver MainForm.Grid_CellPainting): cuando
        /// la ruta no cabe en el ancho de columna, se recorta por la IZQUIERDA para
        /// que se vea el tramo final del path (nombre de archivo/carpeta), que es la
        /// parte más útil para comprobar la operación.
        /// </summary>
        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string colName = _grid.Columns[e.ColumnIndex].Name;
            if (!_pathColumns.Contains(colName)) return;

            string text = e.Value?.ToString() ?? "";
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);

            if (text.Length > 0)
            {
                var pad = _grid.Columns[e.ColumnIndex].DefaultCellStyle.Padding;
                var rect = new Rectangle(
                    e.CellBounds.X + pad.Left,
                    e.CellBounds.Y + pad.Top,
                    e.CellBounds.Width - pad.Left - pad.Right,
                    e.CellBounds.Height - pad.Top - pad.Bottom);

                var flags = TextFormatFlags.Right
                            | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPrefix;

                Color color = selected ? SystemColors.HighlightText : e.CellStyle.ForeColor;
                var font = e.CellStyle.Font ?? _grid.Font;

                var gs = e.Graphics.Save();
                e.Graphics.SetClip(rect);
                TextRenderer.DrawText(e.Graphics, text, font, rect, color, flags);
                e.Graphics.Restore(gs);
            }

            e.Handled = true;
        }

        /// <summary>Copia al portapapeles el valor de la celda actualmente seleccionada.</summary>
        private void CopyCurrentCellValue()
        {
            if (_grid.CurrentCell?.Value == null) return;
            string val = _grid.CurrentCell.Value.ToString();
            if (!string.IsNullOrEmpty(val))
                Clipboard.SetText(val);
        }
    }
}


