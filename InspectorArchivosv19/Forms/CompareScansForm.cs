using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using InspectorArchivos.Models;
using System.Collections.Generic;

namespace InspectorArchivos.Forms
{
    public partial class CompareScansForm : Form
    {
        private BindingList<Scan> _originList = new();
        private BindingList<Scan> _destinationList = new();

        public int? SelectedOriginId { get; private set; }
        public int? SelectedDestinationId { get; private set; }

        public CompareScansForm(IEnumerable<Scan> scans)
        {
            InitializeComponent();
            InitializeGridViews();
            LoadScans(scans);
        }

        private void InitializeComponent()
        {
            this.dgvOrigin = new System.Windows.Forms.DataGridView();
            this.dgvDestination = new System.Windows.Forms.DataGridView();
            this.btnOk = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgvOrigin)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDestination)).BeginInit();
            this.SuspendLayout();
            // 
            // dgvOrigin
            // 
            this.dgvOrigin.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)));
            this.dgvOrigin.Location = new System.Drawing.Point(12, 12);
            this.dgvOrigin.MultiSelect = false;
            this.dgvOrigin.Name = "dgvOrigin";
            this.dgvOrigin.ReadOnly = true;
            this.dgvOrigin.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvOrigin.Size = new System.Drawing.Size(420, 400);
            this.dgvOrigin.TabIndex = 0;
            this.dgvOrigin.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvOrigin_CellDoubleClick);
            this.dgvOrigin.SelectionChanged += new System.EventHandler(this.Dgv_SelectionChanged);
            // 
            // dgvDestination
            // 
            this.dgvDestination.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvDestination.Location = new System.Drawing.Point(450, 12);
            this.dgvDestination.MultiSelect = false;
            this.dgvDestination.Name = "dgvDestination";
            this.dgvDestination.ReadOnly = true;
            this.dgvDestination.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvDestination.Size = new System.Drawing.Size(420, 400);
            this.dgvDestination.TabIndex = 1;
            this.dgvDestination.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvDestination_CellDoubleClick);
            this.dgvDestination.SelectionChanged += new System.EventHandler(this.Dgv_SelectionChanged);
            // 
            // btnOk
            // 
            this.btnOk.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.btnOk.Enabled = false;
            this.btnOk.Location = new System.Drawing.Point(360, 430);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(120, 30);
            this.btnOk.TabIndex = 2;
            this.btnOk.Text = "OK";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.btnCancel.Location = new System.Drawing.Point(500, 430);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(120, 30);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "Cancelar";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // CompareScansForm
            // 
            this.ClientSize = new System.Drawing.Size(884, 472);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.dgvDestination);
            this.Controls.Add(this.dgvOrigin);
            this.MinimumSize = new System.Drawing.Size(900, 510);
            this.Name = "CompareScansForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Seleccionar escaneos para comparar";
            ((System.ComponentModel.ISupportInitialize)(this.dgvOrigin)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDestination)).EndInit();
            this.ResumeLayout(false);

        }

        private void InitializeGridViews()
        {
            dgvOrigin.AutoGenerateColumns = false;
            dgvDestination.AutoGenerateColumns = false;

            var colId = new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Scan.Id),
                HeaderText = "Id",
                Width = 80
            };
            var colDate = new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Scan.CreatedAt),
                HeaderText = "Fecha",
                Width = 160,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "g" }
            };
            var colScope = new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Scan.Scope),
                HeaderText = "Procedencia",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            var colDesc = new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Scan.Description),
                HeaderText = "Descripción",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            dgvOrigin.Columns.AddRange(new DataGridViewColumn[] { colId.Clone() as DataGridViewColumn, colDate.Clone() as DataGridViewColumn, colScope.Clone() as DataGridViewColumn, colDesc.Clone() as DataGridViewColumn });
            dgvDestination.Columns.AddRange(new DataGridViewColumn[] { colId, colDate, colScope,  colDesc });

            dgvOrigin.DataSource = _originList;
            dgvDestination.DataSource = _destinationList;
        }

        private void LoadScans(IEnumerable<Scan> scans)
        {
            var ordered = scans.OrderByDescending(s => s.Id).ToList();
            _originList.Clear();
            _destinationList.Clear();
            foreach (var s in ordered)
            {
                var scope = s.Scope?.Trim();
                if (string.Equals(scope, "origen", StringComparison.OrdinalIgnoreCase))
                {
                    _originList.Add(s);
                }
                else if (string.Equals(scope, "destino", StringComparison.OrdinalIgnoreCase))
                {
                    _destinationList.Add(s);
                }
            }
        }

        private void Dgv_SelectionChanged(object? sender, EventArgs e)
        {
            SelectedOriginId = dgvOrigin.CurrentRow?.DataBoundItem is Scan so ? ((Scan)so).Id : null;
            SelectedDestinationId = dgvDestination.CurrentRow?.DataBoundItem is Scan sd ? sd.Id : null;
            btnOk.Enabled = SelectedOriginId.HasValue && SelectedDestinationId.HasValue;
        }

        private void DgvOrigin_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            // Si se hace doble click y ya hay selección en la otra lista, aceptar.
            if (SelectedOriginId.HasValue && SelectedDestinationId.HasValue)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void DgvDestination_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (SelectedOriginId.HasValue && SelectedDestinationId.HasValue)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            if (SelectedOriginId.HasValue && SelectedDestinationId.HasValue)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private DataGridView dgvOrigin;
        private DataGridView dgvDestination;
        private Button btnOk;
        private Button btnCancel;
    }
}
