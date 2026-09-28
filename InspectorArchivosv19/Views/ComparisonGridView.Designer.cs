namespace InspectorArchivos.Views
{
    partial class ComparisonGridView
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.DataGridView dgvComparison;
        private System.Windows.Forms.Label lblPageInfo;
        private System.Windows.Forms.Button btnPrevPage;
        private System.Windows.Forms.Button btnNextPage;
        private System.Windows.Forms.TextBox txtFilter;
        private System.Windows.Forms.ComboBox cmbStatusFilter;
        private System.Windows.Forms.Panel pnlTopBar;
        private System.Windows.Forms.Panel pnlBottomBar;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Código generado por el Diseñador de componentes

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.dgvComparison = new System.Windows.Forms.DataGridView();
            this.lblPageInfo = new System.Windows.Forms.Label();
            this.btnPrevPage = new System.Windows.Forms.Button();
            this.btnNextPage = new System.Windows.Forms.Button();
            this.txtFilter = new System.Windows.Forms.TextBox();
            this.cmbStatusFilter = new System.Windows.Forms.ComboBox();
            this.pnlTopBar = new System.Windows.Forms.Panel();
            this.pnlBottomBar = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.dgvComparison)).BeginInit();
            this.pnlTopBar.SuspendLayout();
            this.pnlBottomBar.SuspendLayout();
            this.SuspendLayout();
            //
            // pnlTopBar
            //
            this.pnlTopBar.Controls.Add(this.txtFilter);
            this.pnlTopBar.Controls.Add(this.cmbStatusFilter);
            this.pnlTopBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlTopBar.Location = new System.Drawing.Point(0, 0);
            this.pnlTopBar.Name = "pnlTopBar";
            this.pnlTopBar.Size = new System.Drawing.Size(800, 36);
            this.pnlTopBar.TabIndex = 0;
            //
            // txtFilter
            //
            this.txtFilter.Location = new System.Drawing.Point(8, 6);
            this.txtFilter.Name = "txtFilter";
            this.txtFilter.Size = new System.Drawing.Size(300, 23);
            this.txtFilter.TabIndex = 0;
            this.txtFilter.PlaceholderText = "Filtrar por ruta o nombre...";
            this.txtFilter.TextChanged += new System.EventHandler(this.txtFilter_TextChanged);
            //
            // cmbStatusFilter
            //
            this.cmbStatusFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbStatusFilter.Items.AddRange(new object[] {
                "ALL",
                "Igual",
                "MismoHashDistintoNombre",
                "SoloOrigen",
                "SoloDestino",
                "DuplicadoOrigen",
                "DuplicadoDestino",
                "DuplicadoAmbos",
                "Desconocido"
            });
            this.cmbStatusFilter.Location = new System.Drawing.Point(316, 6);
            this.cmbStatusFilter.Name = "cmbStatusFilter";
            this.cmbStatusFilter.Size = new System.Drawing.Size(180, 23);
            this.cmbStatusFilter.TabIndex = 1;
            this.cmbStatusFilter.SelectedIndex = 0;
            this.cmbStatusFilter.SelectedIndexChanged += new System.EventHandler(this.cmbStatusFilter_SelectedIndexChanged);
            //
            // dgvComparison
            //
            this.dgvComparison.AllowUserToAddRows = false;
            this.dgvComparison.AllowUserToDeleteRows = false;
            this.dgvComparison.AllowUserToOrderColumns = true;
            this.dgvComparison.AllowUserToResizeColumns = true;
            this.dgvComparison.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.None;
            this.dgvComparison.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvComparison.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvComparison.Location = new System.Drawing.Point(0, 36);
            this.dgvComparison.MultiSelect = true;
            this.dgvComparison.Name = "dgvComparison";
            this.dgvComparison.ReadOnly = true;
            this.dgvComparison.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvComparison.Size = new System.Drawing.Size(800, 360);
            this.dgvComparison.TabIndex = 1;
            this.dgvComparison.ColumnHeaderMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dgvComparison_ColumnHeaderMouseClick);
            //
            // pnlBottomBar
            //
            this.pnlBottomBar.Controls.Add(this.lblPageInfo);
            this.pnlBottomBar.Controls.Add(this.btnPrevPage);
            this.pnlBottomBar.Controls.Add(this.btnNextPage);
            this.pnlBottomBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlBottomBar.Location = new System.Drawing.Point(0, 396);
            this.pnlBottomBar.Name = "pnlBottomBar";
            this.pnlBottomBar.Size = new System.Drawing.Size(800, 36);
            this.pnlBottomBar.TabIndex = 2;
            //
            // lblPageInfo
            //
            this.lblPageInfo.AutoSize = true;
            this.lblPageInfo.Location = new System.Drawing.Point(8, 10);
            this.lblPageInfo.Name = "lblPageInfo";
            this.lblPageInfo.Size = new System.Drawing.Size(60, 15);
            this.lblPageInfo.TabIndex = 0;
            this.lblPageInfo.Text = "Página 1 de 1";
            //
            // btnPrevPage
            //
            this.btnPrevPage.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.btnPrevPage.Location = new System.Drawing.Point(600, 5);
            this.btnPrevPage.Name = "btnPrevPage";
            this.btnPrevPage.Size = new System.Drawing.Size(90, 26);
            this.btnPrevPage.TabIndex = 1;
            this.btnPrevPage.Text = "◄ Anterior";
            this.btnPrevPage.UseVisualStyleBackColor = true;
            this.btnPrevPage.Click += new System.EventHandler(this.btnPrevPage_Click);
            //
            // btnNextPage
            //
            this.btnNextPage.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            this.btnNextPage.Location = new System.Drawing.Point(700, 5);
            this.btnNextPage.Name = "btnNextPage";
            this.btnNextPage.Size = new System.Drawing.Size(90, 26);
            this.btnNextPage.TabIndex = 2;
            this.btnNextPage.Text = "Siguiente ►";
            this.btnNextPage.UseVisualStyleBackColor = true;
            this.btnNextPage.Click += new System.EventHandler(this.btnNextPage_Click);
            //
            // ComparisonGridView
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dgvComparison);
            this.Controls.Add(this.pnlTopBar);
            this.Controls.Add(this.pnlBottomBar);
            this.Name = "ComparisonGridView";
            this.Size = new System.Drawing.Size(800, 432);
            ((System.ComponentModel.ISupportInitialize)(this.dgvComparison)).EndInit();
            this.pnlTopBar.ResumeLayout(false);
            this.pnlTopBar.PerformLayout();
            this.pnlBottomBar.ResumeLayout(false);
            this.pnlBottomBar.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion
    }
}

