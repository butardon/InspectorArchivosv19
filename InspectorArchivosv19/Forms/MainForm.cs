using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Presentation;
using InspectorArchivos.Database;
using InspectorArchivos.Forms;
using InspectorArchivos.Models;
using InspectorArchivos.Models;
using InspectorArchivos.Services;
using InspectorArchivos.Utils;


namespace InspectorArchivos.Forms
{
    /// <summary>
    /// Ventana principal de InspectorArchivosv7.
    /// Permite gestionar carpetas de origen/destino (incluyendo arrastrar y soltar),
    /// escanear registrando el proceso, construir una tabla comparativa (cuadrícula
    /// virtual) con filtros y ordenación por columna, exportar a Excel y realizar
    /// copia/movimiento/eliminación. La interfaz se construye por código.
    /// </summary>
    public class MainForm : Form
    {

        //private static readonly HashSet<string> _pathColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        //{
        //    "CarpetaPadreOrigen", "CarpetaPadreDestino", "RutaOrigen", "RutaDestino"
        //};
        public static String exeversion = GetExecutableTitle();
        private static readonly HashSet<string> _folderColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CarpetaPadreOrigen", "CarpetaPadreDestino"
        };
        private readonly string _passwordJsonPath;
        private Database.Database _db;
        private Repository _repo;
        private Scanner _scanner;
        private readonly FileOperationsService _ops = new FileOperationsService();
        private readonly AppSettings _settings = AppSettings.Load();

        private CancellationTokenSource _cts;

        // Carpetas
        private readonly ListBox _originFolders = new ListBox();
        private readonly ListBox _destFolders = new ListBox();

        // Escaneo
        private readonly PercentProgressBar _progress = new PercentProgressBar();
        private readonly Label _lblFiles = new Label();
        private readonly Label _lblTime = new Label();
        private readonly System.Windows.Forms.Timer _uiTimer = new System.Windows.Forms.Timer();
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly Button _btnScanOrigin = new Button();
        private readonly Button _btnScanDest = new Button();
        private readonly Button _btnScanBoth = new Button();
        private readonly Button _btnVerUltimo = new Button();
        private readonly Button _btnCancel = new Button();

        // Estadísticas por lado
        private readonly Label _lblOriginStats = new Label();
        private readonly Label _lblDestStats = new Label();
        private readonly Label _lblRecordCount = new Label();
        private ScanRecord _lastOriginScan;
        private ScanRecord _lastDestScan;

        // Operaciones
        private readonly TextBox _txtCustomDest = new TextBox();
        private readonly Button _btnBrowseDest = new Button();
        private readonly TextBox _txtTrash = new TextBox();
        private readonly Button _btnBrowseTrash = new Button();
        private readonly CheckBox _chkHash = new CheckBox();
        private readonly CheckBox _chkMediaOnly = new CheckBox();
        private readonly CheckBox _chkNonMediaOnly = new CheckBox();
        private readonly Button _btnFusionFechas = new Button();

        // Título de carpeta / nombrado personalizado en Copiar-Mover
        private readonly TextBox _txtTituloCarpeta = new TextBox();
        private readonly CheckBox _chkPorFechas = new CheckBox();
        private readonly CheckBox _chkAnadirTitulo = new CheckBox();
        // checkbox para filtrar medios (imagen/video)
        //private CheckBox _chkMediaOnly;

        // Comparativa
        private readonly DataGridView _grid = new DataGridView();
        private readonly CheckBox _chkSelAll = new CheckBox();
        private readonly Button _btnCopySel = new Button();
        private readonly Button _btnMoveSel = new Button();
        private readonly Button _btnExcel = new Button();
        private readonly Button _btnScansErrors = new Button();
        private readonly Button _btnTrash = new Button();
        private readonly Button _btnRevisarDuplicados = new Button();
        private readonly Button _btnLimpiarFiltros = new Button();
        private readonly Label _lblSelectedCount = new Label();
        private readonly ComboBox _cmbProcedencia = new ComboBox();
        private readonly CheckBox _chkVersionar = new CheckBox();
        private readonly Button _btnFechasDist = new Button();
        private readonly CheckBox _chkArchivosOrigen = new CheckBox();
        private readonly CheckBox _chkArchivosDestino = new CheckBox();

        // Fila de filtros/orden sincronizada con las columnas de la cuadrícula.
        private enum FilterKind { None, Text, NumberRange, DateRange }
        private readonly Panel _filterRow = new Panel();
        private readonly Dictionary<string, Panel> _colPanels = new Dictionary<string, Panel>();
        private readonly Dictionary<string, TextBox> _fltText = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _fltFrom = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _fltTo = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _ordNum = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _ordDir = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, FilterKind> _kind = new Dictionary<string, FilterKind>();

        // Log
        private readonly TextBox _log = new TextBox();

        // Datos
        private List<ComparisonRow> _allRows = new List<ComparisonRow>();
        private List<ComparisonRow> _view = new List<ComparisonRow>();
        private string[] _originRoots = Array.Empty<string>();
        private string[] _destRoots = Array.Empty<string>();

        // Modo revisión de duplicados: sustituye la comparativa por la lista de
        // duplicados coloreada, en el orden calculado por DuplicateReviewService.
        private bool _duplicatesReviewMode;
        private List<ComparisonRow> _duplicateReviewRows = new List<ComparisonRow>();

        // Modo filtro por arrastrar y soltar: al soltar archivos/carpetas del
        // explorador sobre la cuadrícula comparativa, se filtra _allRows a las
        // filas cuyo lado (origen o destino) coincide exactamente con alguno de
        // los archivos arrastrados (NombrePc + ruta completa + nombre y
        // extensión + fecha de modificación + fecha de creación + atributos).
        private bool _dragFilterActive;
        private List<ComparisonRow> _dragFilteredRows = new List<ComparisonRow>();

        public MainForm(string passwordJsonPath)
        {   
            _passwordJsonPath = passwordJsonPath;
            Text = exeversion;
            // Ancho inicial ajustado, de nuevo, a la línea de referencia (amarilla)
            // indicada por el usuario (antes 1380, luego 1530, luego 1700): al
            // arrancar, el formulario ya no se abre más ancho que esa línea, con lo
            // que ni el grid de comparación ni el recuadro inferior de anotaciones
            // (ambos Dock=Fill) reservan espacio en blanco sobrante a la derecha.
            Width = 1200;
            Height = 1160;
            // Ancho mínimo reducido a propósito: por debajo del ancho "normal" (1530)
            // las filas de controles (scanPanel, carpeta destino/papelera, barra de
            // acciones) pasan a ocupar 2 líneas (ver BuildUi) en vez de truncarse,
            // así que el formulario puede seguir estrechándose sin perder controles.
            MinimumSize = new Size(900, 760);
            StartPosition = FormStartPosition.CenterScreen;

            _db = Program.OpenDatabase(_passwordJsonPath);
            _repo = new Repository(_db);
            _scanner = new Scanner(_repo);

            // Cargar valores por omision desde DefaultsService
            var defs = DefaultsService.Load();
            try
            {
                _chkHash.Checked = defs.CalcularHashBlake3;
            }
            catch { }
            try
            {
                _chkMediaOnly.Checked = defs.SoloImagenes;
                _chkNonMediaOnly.Checked = defs.SoloNoImagenes;
            }
            catch { }
            try { _txtCustomDest.Text = defs.CarpetaDestinoPrioritaria; } catch { }
            try { _txtTituloCarpeta.Text = defs.TituloCarpeta; } catch { }
            try { _cmbProcedencia.SelectedItem = defs.EleccionProcedencia; } catch { }
            try { _chkArchivosOrigen.Checked = defs.ArchivosOrigen; } catch { }
            try { _chkArchivosDestino.Checked = defs.ArchivosDestino; } catch { }
            try { _chkPorFechas.Checked = defs.PorFechas; } catch { }
            try { _chkAnadirTitulo.Checked = defs.AnadirTitulo; } catch { }
            try { _chkVersionar.Checked = defs.Versionar; } catch { }
            try { _txtTrash.Text = defs.CarpetaPapelera; } catch { }
            BuildUi();
            RestoreColumnLayout();

            // Inicializar estado del checkbox de medios desde settings
            try
            {
                _chkMediaOnly.Checked = _settings.MediaOnly;
                MediaExtensionsService.RestrictToMedia = _settings.MediaOnly;
                _chkNonMediaOnly.Checked = _settings.SoloNoImagenes;
                FileEnumerator.OnlyNonMedia = _settings.SoloNoImagenes;
            }
            catch { }
            try
            {
                if (string.IsNullOrEmpty(_txtTrash.Text)) _txtTrash.Text=_settings.CarpetaPapelera;
                if (string.IsNullOrEmpty(_txtTrash.Text)) _txtTrash.Text = Path.Combine(AppContext.BaseDirectory, "Papelera");
            }
            catch { }

            

            _uiTimer.Interval = 500;
            _uiTimer.Tick += (s, e) =>
            {
                if (_sw.IsRunning)
                    _lblTime.Text = "Tiempo: " + FormatUtils.Time(_sw.Elapsed);
            };

            Shown += (s, e) => LayoutFilterRow();
        }

        /// <summary>
        /// Devuelve el nombre del ejecutable (.exe) que ha arrancado la aplicación,
        /// SIN la extensión, para usarlo como título de la ventana principal. Así,
        /// si se renombra el .exe (p. ej. "InspectorArchivosv8.exe"), el título se
        /// actualiza automáticamente sin tocar el código. Si por algún motivo no se
        /// puede determinar (entorno restringido, etc.), se usa un nombre por defecto.
        /// </summary>
        private static string GetExecutableTitle()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath))
                    return Path.GetFileNameWithoutExtension(exePath);
            }
            catch
            {
                // Si no se puede acceder al proceso/módulo, se usa el nombre por defecto.
            }
            return "InspectorArchivos";
        }

        // =====================================================================
        // Construcción de la interfaz
        // =====================================================================

        private void BuildUi()
        {
            
            var foldersPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };

            foldersPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50));

            foldersPanel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50));

           
            foldersPanel.Controls.Add(
                BuildFoldersGroup(
                    "Carpetas de ORIGEN",
                    _originFolders,
                    ScanScope.origen,
                    _btnScanOrigin,
                    "Escanear Origen",
                    _lblOriginStats),
                0, 0);

            foldersPanel.Controls.Add(
                BuildFoldersGroup(
                    "Carpetas de DESTINO",
                    _destFolders,
                    ScanScope.destino,
                    _btnScanDest,
                    "Escanear Destino",
                    _lblDestStats),
                1, 0);

            EnableFolderDrop(_originFolders);
            EnableFolderDrop(_destFolders);

            // ---------------------------------------------------------------
            // Fila de escaneo: FlowLayoutPanel que se ajusta ("wrap") a varias
            // líneas cuando el ancho disponible no es suficiente para mostrar
            // todos los controles en una sola línea, en vez de recortarlos u
            // ocultarlos tras una barra de desplazamiento horizontal. AutoSize
            // hace que la fila que lo contiene (topContainer) crezca o
            // encoja según el número de líneas realmente necesarias.
            // ---------------------------------------------------------------
            var scanPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 3, 6, 3)
            };

            _chkHash.Text = "Calcular hash BLAKE3";
            _chkHash.Checked = true;
            _chkHash.AutoSize = true;
            _chkHash.Anchor = AnchorStyles.Left;
            scanPanel.Controls.Add(_chkHash);

            // Checkbox para filtrar solo imagenes/videos
            _chkMediaOnly.Text = "Solo imágenes";
            _chkMediaOnly.Checked = true;
            _chkMediaOnly.CheckedChanged += (s, e) => ApplyMediaFilterSelection(_chkMediaOnly, _chkNonMediaOnly);
            _chkMediaOnly.AutoSize = true;
            _chkMediaOnly.Anchor = AnchorStyles.Left;
            scanPanel.Controls.Add(_chkMediaOnly);

            _chkNonMediaOnly.Text = "Solo NO imágenes";
            _chkNonMediaOnly.AutoSize = true;
            _chkNonMediaOnly.Anchor = AnchorStyles.Left;
            _chkNonMediaOnly.CheckedChanged += (s, e) => ApplyMediaFilterSelection(_chkNonMediaOnly, _chkMediaOnly);
            scanPanel.Controls.Add(_chkNonMediaOnly);

            _btnCancel.Text = "Cancelar escaneo";
            _btnCancel.AutoSize = true;
            _btnCancel.Click += (s, e) => CancelScan();
            scanPanel.Controls.Add(_btnCancel);

            _btnScanBoth.Text = "Escanear Origen y Destino";
            _btnScanBoth.AutoSize = true;
            _btnScanBoth.Anchor = AnchorStyles.Left;
            _btnScanBoth.Click += async (s, e) => await ScanBothAsync();
            scanPanel.Controls.Add(_btnScanBoth);

            _btnFusionFechas.Text = "Fusión Fechas";
            _btnFusionFechas.AutoSize = true;
            _btnFusionFechas.Click += async (s, e) => await FusionFechasAsync();
            scanPanel.Controls.Add(_btnFusionFechas);

            _btnVerUltimo.Text = "Ver último escáner";
            _btnVerUltimo.AutoSize = true;
            _btnVerUltimo.Anchor = AnchorStyles.Left;
            _btnVerUltimo.Click += (s, e) => VerUltimoEscaner();
            scanPanel.Controls.Add(_btnVerUltimo);

            _btnScansErrors.Text = "Ver Scans / Errores";
            _btnScansErrors.AutoSize = true;
            _btnScansErrors.Anchor = AnchorStyles.Left;
            _btnScansErrors.Click += (s, e) => ShowScansErrors();
            scanPanel.Controls.Add(_btnScansErrors);

            _lblFiles.Text = "Archivos: 0 / 0";
            _lblFiles.AutoSize = true;
            _lblFiles.Anchor = AnchorStyles.Left;
            scanPanel.Controls.Add(_lblFiles);

            // "Progreso: NN%" ya no es una etiqueta aparte: el porcentaje se dibuja
            // superpuesto sobre la propia barra de progreso (ver PercentProgressBar
            // más abajo, donde se añade _progress a opsRow1). "Tiempo" se ha
            // reubicado también en esa misma línea, justo después de la barra, para
            // que no se expanda hacia la derecha en esta fila.

            // ---------------------------------------------------------------
            // Fila "Carpeta destino (prioritaria) / Título Carpeta / contadores"
            // y fila "Carpeta papelera / nombrado por fechas". Se construyen como
            // FlowLayoutPanel (en vez de TableLayoutPanel de columnas fijas) para
            // que, igual que scanPanel y actionLeft, puedan pasar a ocupar 2
            // líneas cuando el usuario reduzca el ancho del formulario, sin que
            // ningún control quede truncado ni oculto.
            // ---------------------------------------------------------------
            var opsRow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 4, 6, 2)
            };

            var lblDest = new Label
            {
                Text = "Carpeta destino (prioritaria, opcional):",
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3)
            };
            opsRow1.Controls.Add(lblDest);

            // Ancho reducido (antes ocupaba todo el espacio disponible con
            // Dock=Fill): así la fila cabe cómodamente dentro del nuevo ancho
            // del formulario, dejando sitio a "Título Carpeta" y a los
            // contadores "Seleccionados"/"Mostrados" en la misma línea.
            _txtCustomDest.Width = 260;
            _txtCustomDest.Margin = new Padding(3, 4, 3, 3);
            _txtCustomDest.PlaceholderText = "Vacío = usar carpetas relativas origen/destino";
            EnableFolderDropTextBox(_txtCustomDest);
            _txtCustomDest.DragDrop += (s, e) =>
            {
                // Si se arrastra una carpeta con nombre FechaTitulo (aaaa-mm-ddTitulo),
                // extraer la parte despues del fecha y, si está marcado Añadir Titulo,
                // pegarlo en _txtTituloCarpeta.
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    var p = paths[0];
                    if (Directory.Exists(p) && _chkAnadirTitulo.Checked)
                    {
                        var name = Path.GetFileName(p);
                        // buscar patrón yyyy-mm-dd seguido de texto
                        if (name.Length > 10 && name[4] == '-' && name[7] == '-')
                        {
                            var possible = name.Substring(10);
                            if (!string.IsNullOrWhiteSpace(possible)) _txtTituloCarpeta.Text = possible;
                        }
                    }
                }
            };
            opsRow1.Controls.Add(_txtCustomDest);

            _btnBrowseDest.Text = "...";
            _btnBrowseDest.Width = 30;
            _btnBrowseDest.Margin = new Padding(0, 4, 12, 3);
            _btnBrowseDest.Click += (s, e) => BrowseFolder(_txtCustomDest);
            opsRow1.Controls.Add(_btnBrowseDest);

            var lblTitulo = new Label
            {
                Text = "Título Carpeta:",
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3)
            };
            opsRow1.Controls.Add(lblTitulo);

            _txtTituloCarpeta.Width = 150;
            _txtTituloCarpeta.Margin = new Padding(3, 4, 16, 3);
            opsRow1.Controls.Add(_txtTituloCarpeta);

            // BARRA DE PROGRESO: reubicada en esta línea (antes en scanPanel),
            // con más ancho disponible ahora que "Seleccionados"/"Mostrados"
            // se han desplazado a opsRow2.
            _progress.Visible = true;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Value = 0;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Width = 180;
            _progress.Height = 23;
            _progress.Margin = new Padding(8, 5, 8, 5);
            _progress.Anchor = AnchorStyles.Left;
            opsRow1.Controls.Add(_progress);

            // "Tiempo" reubicado en esta línea, inmediatamente detrás de la barra de
            // progreso (antes en scanPanel, donde se expandía hacia la derecha).
            _lblTime.Text = "Tiempo: 00:00:00";
            _lblTime.AutoSize = true;
            _lblTime.Anchor = AnchorStyles.Left;
            _lblTime.Margin = new Padding(3, 8, 3, 3);
            opsRow1.Controls.Add(_lblTime);

            var opsRow2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 2, 6, 4)
            };

            var lblTrash = new Label
            {
                Text = "Carpeta papelera:",
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3)
            };
            opsRow2.Controls.Add(lblTrash);

            // Ancho reducido, igual que _txtCustomDest.
            _txtTrash.Width = 260;
            _txtTrash.Margin = new Padding(3, 4, 3, 3);
            opsRow2.Controls.Add(_txtTrash);

            _btnBrowseTrash.Text = "...";
            _btnBrowseTrash.Width = 30;
            _btnBrowseTrash.Margin = new Padding(0, 4, 16, 3);
            _btnBrowseTrash.Click += (s, e) => BrowseFolder(_txtTrash);
            opsRow2.Controls.Add(_btnBrowseTrash);

            _chkPorFechas.Text = "Por fechas";
            _chkPorFechas.AutoSize = true;
            _chkPorFechas.Margin = new Padding(3, 8, 10, 3);
            opsRow2.Controls.Add(_chkPorFechas);

            _chkAnadirTitulo.Text = "Añadir Título";
            _chkAnadirTitulo.AutoSize = true;
            _chkAnadirTitulo.Margin = new Padding(0, 8, 10, 3);
            opsRow2.Controls.Add(_chkAnadirTitulo);

            _chkVersionar.Text = "Versionar";
            _chkVersionar.AutoSize = true;
            _chkVersionar.Margin = new Padding(0, 8, 3, 3);
            opsRow2.Controls.Add(_chkVersionar);

            // Botón "Fechas Dist.": recoge todas las carpetas/archivos de ORIGEN,
            // calcula la fecha de modificación válida (GetValidModificationDate) de
            // cada archivo y muestra en un formulario aparte la lista de fechas
            // distintas, ordenadas de mayor a menor, en formato aaaa-mm-dd.
            _btnFechasDist.Text = "Fechas Dist.";
            _btnFechasDist.AutoSize = true;
            _btnFechasDist.Margin = new Padding(6, 4, 10, 3);
            _btnFechasDist.Click += (s, e) => ShowFechasDistintas();
            opsRow2.Controls.Add(_btnFechasDist);

            // "Seleccionados" y "Mostrados": reubicados en esta línea (antes en
            // opsRow1, junto a "Título Carpeta"), pegados al final de la fila
            // que contiene "Carpeta papelera", "Por fechas", "Añadir Título",
            // "Versionar" y "Fechas Dist.".
            _lblSelectedCount.Text = "Seleccionados: 0";
            _lblSelectedCount.AutoSize = true;
            _lblSelectedCount.Margin = new Padding(12, 8, 12, 3);
            opsRow2.Controls.Add(_lblSelectedCount);

            _lblRecordCount.Text = "Mostrados: 0 de 0";
            _lblRecordCount.AutoSize = true;
            _lblRecordCount.Margin = new Padding(3, 8, 3, 3);
            opsRow2.Controls.Add(_lblRecordCount);

            // ---------------------------------------------------------------
            // Barra de acciones (Copiar/Mover/Papelera/Duplicados/Excel + checks
            // de procedencia). También como FlowLayoutPanel con wrap, para que
            // se reparta en 2 líneas si hace falta en vez de perder botones.
            // ---------------------------------------------------------------
            var actionLeft = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 2, 6, 2)
            };

            _btnCopySel.Text = "Copiar";
            _btnCopySel.AutoSize = true;
            _btnCopySel.Click += (s, e) => CopyMoveSelected(move: false);

            _btnMoveSel.Text = "Mover";
            _btnMoveSel.AutoSize = true;
            _btnMoveSel.Click += (s, e) => CopyMoveSelected(move: true);

            _btnTrash.Text = "Eliminar (mover a papelera)";
            _btnTrash.AutoSize = true;
            _btnTrash.Click += (s, e) => OperateTrash();

            _btnExcel.Text = "Exportar a Excel";
            _btnExcel.AutoSize = true;
            _btnExcel.Click += (s, e) => ExportToExcel();

            _chkArchivosOrigen.Text = "Archivos Origen";
            _chkArchivosOrigen.AutoSize = true;
            _chkArchivosOrigen.Checked = true;
            _chkArchivosOrigen.Margin = new Padding(3, 7, 0, 0);
            _chkArchivosOrigen.CheckedChanged += (s, e) => ApplyFilterAndSort();

            _chkArchivosDestino.Text = "Archivos Destino";
            _chkArchivosDestino.AutoSize = true;
            //_chkArchivosDestino.Checked = true;
            _chkArchivosDestino.Checked = false;
            _chkArchivosDestino.Margin = new Padding(3, 7, 0, 0);
            _chkArchivosDestino.CheckedChanged += (s, e) => ApplyFilterAndSort();
      
            var lblProcedencia = new Label
            {
                Text = "Elección de procedencia",
                AutoSize = true,
                Margin = new Padding(10, 8, 4, 0)
            };

            _cmbProcedencia.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbProcedencia.Width = 90;
            _cmbProcedencia.Items.Clear();
            _cmbProcedencia.Items.AddRange(new object[] { "origen", "destino", "Ambos" });
            _cmbProcedencia.SelectedItem = "origen";
            _cmbProcedencia.Margin = new Padding(0, 3, 8, 0);

            _btnRevisarDuplicados.Text = "Revisar Duplicados";
            _btnRevisarDuplicados.AutoSize = true;
            _btnRevisarDuplicados.Click += (s, e) => RevisarDuplicados();


            actionLeft.Controls.Add(_btnCopySel);
            actionLeft.Controls.Add(_btnMoveSel);
            actionLeft.Controls.Add(_btnTrash);
            actionLeft.Controls.Add(_btnExcel);
            actionLeft.Controls.Add(_chkArchivosOrigen);
            actionLeft.Controls.Add(_chkArchivosDestino);
            actionLeft.Controls.Add(lblProcedencia);
            actionLeft.Controls.Add(_cmbProcedencia);
            actionLeft.Controls.Add(_btnRevisarDuplicados);

            BuildGrid();
            BuildFilterRow();

            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.ReadOnly = true;
            _log.Font = new System.Drawing.Font(FontFamily.GenericMonospace, 8);

            _btnLimpiarFiltros.Text = "Limpiar";
            _btnLimpiarFiltros.Click += (s, e) => LimpiarFiltrosYOrden();

            _filterRow.Dock = DockStyle.Fill;
            _filterRow.BackColor = SystemColors.Control;
            _filterRow.Margin = new Padding(0);

            var gridContainer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            gridContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            gridContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _grid.Dock = DockStyle.Fill;
            gridContainer.Controls.Add(_filterRow, 0, 0);
            gridContainer.Controls.Add(_grid, 0, 1);

            // ---------------------------------------------------------------
            // topContainer: la fila de carpetas (foldersPanel) mantiene una
            // altura fija (es una lista, no necesita "wrap"); el resto de filas
            // (scanPanel, opsRow1, opsRow2 y actionLeft) son AutoSize, de modo
            // que si al redimensionar el formulario alguna de ellas pasa a
            // ocupar 2 líneas, topContainer crece automáticamente esa cantidad
            // exacta -sin recortar ni ocultar ningún control- y es la
            // cuadrícula comparativa (Percent 100 en "root") la que cede esa
            // altura, tal y como se pidió.
            // ---------------------------------------------------------------
            var topContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            topContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));  // foldersPanel
            topContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // scanPanel
            topContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // opsRow1
            topContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // opsRow2
            topContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // actionLeft
            topContainer.Controls.Add(foldersPanel, 0, 0);
            topContainer.Controls.Add(scanPanel, 0, 1);
            topContainer.Controls.Add(opsRow1, 0, 2);
            topContainer.Controls.Add(opsRow2, 0, 3);
            topContainer.Controls.Add(actionLeft, 0, 4);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            // La fila de topContainer es AutoSize (se ajusta a su contenido,
            // incluidas las posibles 2ª líneas de scanPanel/opsRow1/opsRow2/
            // actionLeft); la cuadrícula comparativa (Percent 100) aprovecha
            // siempre el resto del alto disponible del formulario.
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.Controls.Add(topContainer, 0, 0);
            root.Controls.Add(gridContainer, 0, 1);
            root.Controls.Add(_log, 0, 2);
            Controls.Add(root);
            // NOTA: se ha retirado el MessageBox.Show de diagnóstico que había aquí.
            // Visible=true no demuestra que un control se vea: cualquier control
            // recién creado es Visible=true por defecto aunque su tamaño real
            // acabe siendo 0x0. El problema real era de layout (altura de fila
            // + AutoSize de los labels), no de visibilidad.
        }

        private GroupBox BuildFoldersGroup(
            string title,
            ListBox list,
            ScanScope scope,
            Button scanButton,
            string scanText,
            Label statsLabel)
        {
            var gb = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(0)
            };

            panel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            panel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 90));

            panel.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));

            panel.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 30));

            panel.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 15));

            list.Dock = DockStyle.Fill;
            list.SelectionMode = SelectionMode.MultiExtended;
            list.IntegralHeight = false;

            panel.Controls.Add(list, 0, 0);

            var btns = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            btns.Controls.Add(AutoBtn(
                "Carpeta",
                () => AddFolder(list),
                82));

            btns.Controls.Add(AutoBtn(
                "Archivos",
                () => AddFiles(list),
                82));

            btns.Controls.Add(AutoBtn(
                "Quitar",
                () => RemoveFolder(list),
                82));

            btns.Controls.Add(AutoBtn(
                "Limpiar",
                () => list.Items.Clear(),
                82));

            panel.Controls.Add(btns, 1, 0);

            scanButton.Text = scanText;
            scanButton.AutoSize = true;
            scanButton.Dock = DockStyle.Fill;
            scanButton.Click += async (s, e) =>
                await ScanSingleAsync(scope, list);

            panel.Controls.Add(scanButton, 0, 1);
            panel.SetColumnSpan(scanButton, 2);

            statsLabel.Dock = DockStyle.Fill;
            statsLabel.AutoSize = false;
            statsLabel.Text =
                "Encontrados: 0 | Resultantes (únicos): 0 | " +
                "Carpetas: 0 | Tiempo: 00:00:00";

            statsLabel.TextAlign = ContentAlignment.MiddleLeft;

            panel.Controls.Add(statsLabel, 0, 2);
            panel.SetColumnSpan(statsLabel, 2);

            gb.Controls.Add(panel);

            return gb;
        }

        private static Button AutoBtn(string text, Action onClick, int width)
        {
            var b = new Button { Text = text, Width = width, AutoSize = true };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void BuildGrid()
        {
            _grid.AllowUserToOrderColumns = true;
            _grid.AllowUserToResizeColumns = true;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = false;
            _grid.VirtualMode = true;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.MultiSelect = true;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);

            AddCol("Sel", "", 68, FilterKind.None, isCheckBox: true);
            AddCol("Candidatura", "Candidatura", 90, FilterKind.Text);
            AddCol("Estado", "Estado", 140, FilterKind.Text);
            AddCol("CarpetaPadreOrigen", "Carpeta padre (origen)", 200, FilterKind.Text);
            AddCol("CarpetaPadreDestino", "Carpeta padre (destino)", 200, FilterKind.Text);
            AddCol("RutaOrigen", "Ruta origen", 220, FilterKind.Text);
            AddCol("RutaDestino", "Ruta destino", 220, FilterKind.Text);
            AddCol("Nombre", "Nombre", 180, FilterKind.Text);
            AddCol("Extension", "Ext", 60, FilterKind.Text);
            AddCol("TamanoOrigen", "Tamaño origen (bytes)", 130, FilterKind.NumberRange);
            AddCol("TamanoDestino", "Tamaño destino (bytes)", 130, FilterKind.NumberRange);
            AddCol("FechaModOrigen", "F. modificación origen", 150, FilterKind.DateRange);
            AddCol("FechaModDestino", "F. modificación destino", 150, FilterKind.DateRange);
            AddCol("HashOrigen", "Hash origen", 130, FilterKind.Text);
            AddCol("HashDestino", "Hash destino", 130, FilterKind.Text);
            AddCol("Fingerprint", "Fingerprint", 120, FilterKind.Text);
            AddCol("EsDuplicado", "Duplicado", 70, FilterKind.Text);
            AddCol("NombrePc", "NombrePc", 110, FilterKind.Text);
            AddCol("RevisadoOrigen", "Revisado origen", 100, FilterKind.Text);
            AddCol("RevisadoDestino", "Revisado destino", 100, FilterKind.Text);

            foreach (var pathCol in new[] { "RutaOrigen", "CarpetaPadreOrigen", "RutaDestino", "CarpetaPadreDestino" })
                _grid.Columns[pathCol].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            _grid.CellValueNeeded += Grid_CellValueNeeded;
            _grid.CellValuePushed += Grid_CellValuePushed;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellPainting += Grid_CellPainting;
            // Interceptar cambio en filtros de texto para soportar comodines iniciales/finales
            foreach (var t in _fltText.Values) t.TextChanged += (s, e) => ApplyFilterAndSort();

            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell != null
                    && _grid.Columns[_grid.CurrentCell.ColumnIndex].Name == "Sel")
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    _grid.EndEdit();
                }
            };

            _grid.Scroll += (s, e) => LayoutFilterRow();
            _grid.ColumnWidthChanged += (s, e) => LayoutFilterRow();
            _grid.ColumnDisplayIndexChanged += (s, e) => LayoutFilterRow();
            _grid.SizeChanged += (s, e) => LayoutFilterRow();

            // Hacer que el botón derecho seleccione la celda bajo el cursor
            // (así "Copiar valor" y "Explorador" actúan sobre la celda pulsada)
            _grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _grid.CurrentCell = _grid[e.ColumnIndex, e.RowIndex];
                }
            };

            var ctx = new ContextMenuStrip();

            var miCopy = new ToolStripMenuItem("Copiar valor");
            miCopy.Click += (s, e) => CopyCurrentCellValue();
            ctx.Items.Add(miCopy);

            var miExplorer = new ToolStripMenuItem("Explorador");
            miExplorer.Click += (s, e) => AbrirEnExplorador();
            ctx.Items.Add(miExplorer);

            ctx.Opening += (s, e) =>
            {
                var colName = GetCurrentCellColumnName();
                miExplorer.Visible = colName != null && _pathColumns.Contains(colName);
            };

            _grid.ContextMenuStrip = ctx;

            EnableGridDrop();
        }

        private string GetCurrentCellColumnName()
        {
            var cell = _grid.CurrentCell;
            if (cell == null) return null;
            return _grid.Columns[cell.ColumnIndex].Name;
        }

        private void AbrirEnExplorador()
        {
            var colName = GetCurrentCellColumnName();
            if (colName == null || !_pathColumns.Contains(colName)) return;

            var valor = _grid.CurrentCell?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(valor)) return;

            bool esColumnaCarpeta = _folderColumns.Contains(colName);

            try
            {
                if (esColumnaCarpeta)
                {
                    if (Directory.Exists(valor))
                    {
                        Process.Start("explorer.exe", $"\"{valor}\"");
                        return;
                    }
                }
                else
                {
                    if (File.Exists(valor))
                    {
                        Process.Start("explorer.exe", $"/select,\"{valor}\"");
                        return;
                    }
                    if (Directory.Exists(valor))
                    {
                        Process.Start("explorer.exe", $"\"{valor}\"");
                        return;
                    }
                }

                // Fallback: la ruta no existe -> buscamos la carpeta padre existente más cercana
                var carpeta = EncontrarCarpetaExistenteMasCercana(valor);
                if (carpeta != null)
                {
                    Process.Start("explorer.exe", $"\"{carpeta}\"");
                }
                else
                {
                    MessageBox.Show("No se ha encontrado la ruta ni ninguna carpeta padre existente.",
                        "Explorador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el explorador: {ex.Message}",
                    "Explorador", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string EncontrarCarpetaExistenteMasCercana(string ruta)
        {
            try
            {
                var dir = Path.GetDirectoryName(ruta) ?? ruta;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(dir)) return dir;
                    var padre = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(padre) || padre == dir) break;
                    dir = padre;
                }
            }
            catch
            {
                // ignoramos errores de ruta malformada, se tratará como "no encontrada"
            }
            return null;
        }

        private void AddCol(string name, string header, int width, FilterKind kind, bool isCheckBox = false)
        {
            DataGridViewColumn col;
            if (isCheckBox)
                col = new DataGridViewCheckBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = false };
            else
                col = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = true };

            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(col);
            _kind[name] = kind;
        }

        // =====================================================================
        // Fila de filtros y orden
        // =====================================================================

        private void BuildFilterRow()
        {
            _filterRow.Controls.Clear();
            _colPanels.Clear();
            _fltText.Clear();
            _fltFrom.Clear();
            _fltTo.Clear();
            _ordNum.Clear();
            _ordDir.Clear();

            foreach (DataGridViewColumn c in _grid.Columns)
            {
                string name = c.Name;
                var kind = _kind[name];
                var p = new Panel { BorderStyle = BorderStyle.FixedSingle };
                _colPanels[name] = p;

                if (name == "Sel")
                {
                    _btnLimpiarFiltros.Text = "Limpiar";
                    _btnLimpiarFiltros.Margin = new Padding(2, 2, 2, 0);

                    _chkSelAll.Text = "SEL";
                    _chkSelAll.AutoSize = true;
                    _chkSelAll.Margin = new Padding(3, 0, 0, 0);
                    _chkSelAll.CheckedChanged -= ChkSelAll_CheckedChanged;
                    _chkSelAll.CheckedChanged += ChkSelAll_CheckedChanged;

                    p.Controls.Add(_btnLimpiarFiltros);
                    p.Controls.Add(_chkSelAll);
                }

                if (kind == FilterKind.Text)
                {
                    var t = new TextBox { PlaceholderText = "filtro" };
                    t.TextChanged += (s, e) => ApplyFilterAndSort();
                    _fltText[name] = t;
                    p.Controls.Add(t);
                }
                else if (kind == FilterKind.NumberRange || kind == FilterKind.DateRange)
                {
                    var from = new TextBox { PlaceholderText = "desde" };
                    var to = new TextBox { PlaceholderText = "hasta" };
                    from.TextChanged += (s, e) => ApplyFilterAndSort();
                    to.TextChanged += (s, e) => ApplyFilterAndSort();
                    _fltFrom[name] = from;
                    _fltTo[name] = to;
                    p.Controls.Add(from);
                    p.Controls.Add(to);
                }

                if (kind != FilterKind.None)
                {
                    var on = new TextBox { PlaceholderText = "#", TextAlign = HorizontalAlignment.Center };
                    var od = new TextBox { PlaceholderText = "A/D", TextAlign = HorizontalAlignment.Center, MaxLength = 1 };
                    on.TextChanged += (s, e) => ApplyFilterAndSort();
                    od.TextChanged += (s, e) => ApplyFilterAndSort();
                    _ordNum[name] = on;
                    _ordDir[name] = od;
                    p.Controls.Add(on);
                    p.Controls.Add(od);
                }

                _filterRow.Controls.Add(p);
            }
        }

        private void LimpiarFiltrosYOrden()
        {
            foreach (var t in _fltText.Values) t.Text = string.Empty;
            foreach (var t in _fltFrom.Values) t.Text = string.Empty;
            foreach (var t in _fltTo.Values) t.Text = string.Empty;
            foreach (var t in _ordNum.Values) t.Text = string.Empty;
            foreach (var t in _ordDir.Values) t.Text = string.Empty;

            bool salioDeFiltroArrastre = _dragFilterActive;
            _dragFilterActive = false;
            _dragFilteredRows = new List<ComparisonRow>();

            ApplyFilterAndSort();
            Log("Filtros y ordenación de columnas limpiados." + (salioDeFiltroArrastre ? " Se ha salido del filtro por arrastre." : ""));
        }

        private void LayoutFilterRow()
        {
            if (_filterRow.Controls.Count == 0) return;
            int x = -_grid.HorizontalScrollingOffset;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex))
            {
                if (!_colPanels.TryGetValue(col.Name, out var p)) continue;
                p.Location = new Point(x, 0);
                p.Width = Math.Max(1, col.Width);
                p.Height = _filterRow.Height;
                LayoutColPanel(col.Name);
                x += col.Width;
            }
        }

        private void LayoutColPanel(string name)
        {
            var p = _colPanels[name];
            var kind = _kind[name];
            int w = p.Width;
            const int pad = 2, line1Y = 2, line2Y = 26, h = 20;

            if (name == "Sel")
            {
                _btnLimpiarFiltros.SetBounds(2, 2, Math.Max(10, p.Width - 4), 20);
                _chkSelAll.SetBounds(3, 26, Math.Max(30, p.Width - 6), 18);
                return;
            }

            if (kind == FilterKind.Text && _fltText.TryGetValue(name, out var t))
            {
                t.SetBounds(pad, line1Y, Math.Max(10, w - 2 * pad), h);
            }
            else if (kind == FilterKind.NumberRange || kind == FilterKind.DateRange)
            {
                int half = Math.Max(10, (w - 3 * pad) / 2);
                _fltFrom[name].SetBounds(pad, line1Y, half, h);
                _fltTo[name].SetBounds(2 * pad + half, line1Y, half, h);
            }

            if (kind != FilterKind.None)
            {
                _ordNum[name].SetBounds(pad, line2Y, 30, h);
                _ordDir[name].SetBounds(2 * pad + 30, line2Y, 28, h);
            }
        }

        // =====================================================================
        // Carpetas y arrastrar/soltar
        // =====================================================================

        private void AddFolder(ListBox list)
        {
            using var dlg = new FolderBrowserDialog { Description = "Seleccionar carpeta" };
            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                dlg.SelectedPath = _settings.LastFolder;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string p = dlg.SelectedPath;
                _settings.LastFolder = p;
                if (!list.Items.Contains(p)) list.Items.Add(p);
            }
        }

        private void ApplyMediaFilterSelection(CheckBox changed, CheckBox other)
        {
            if (changed.Checked && other.Checked)
            {
                changed.CheckedChanged -= (s, e) => ApplyMediaFilterSelection(changed, other);
                changed.Checked = false;
                MessageBox.Show(this, "Solo puede marcarse uno de los filtros de imágenes.", "Filtro incompatible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MediaExtensionsService.RestrictToMedia = _chkMediaOnly.Checked;
            FileEnumerator.OnlyNonMedia = _chkNonMediaOnly.Checked;
        }

        private void AddFiles(ListBox list)
        {
            using var dlg = new OpenFileDialog { Title = "Seleccionar archivos", Multiselect = true };
            if (!string.IsNullOrWhiteSpace(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                dlg.InitialDirectory = _settings.LastFolder;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var path in dlg.FileNames)
                if (!list.Items.Contains(path)) list.Items.Add(path);
            _settings.LastFolder = Path.GetDirectoryName(dlg.FileName) ?? _settings.LastFolder;
        }

        private void RemoveFolder(ListBox list)
        {
            var toRemove = list.SelectedItems.Cast<string>().ToList();
            foreach (var p in toRemove) list.Items.Remove(p);
        }

        private void BrowseFolder(TextBox target)
        {
            using var dlg = new FolderBrowserDialog { Description = "Seleccionar carpeta" };
            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                dlg.SelectedPath = _settings.LastFolder;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                target.Text = dlg.SelectedPath;
                _settings.LastFolder = dlg.SelectedPath;
            }
        }

        private void EnableFolderDrop(ListBox list)
        {
            list.AllowDrop = true;
            list.DragEnter += (s, e) => e.Effect = HasDroppedFileSystemEntries(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            list.DragDrop += (s, e) =>
            {
                foreach (var f in GetDroppedPaths(e.Data))
                {
                    if (!list.Items.Contains(f)) list.Items.Add(f);
                    _settings.LastFolder = f;
                }
            };
        }

        private void EnableFolderDropTextBox(TextBox tb)
        {
            tb.AllowDrop = true;
            tb.DragEnter += (s, e) => e.Effect = HasDroppedFolders(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            tb.DragDrop += (s, e) =>
            {
                var fs = GetDroppedFolders(e.Data);
                if (fs.Count > 0)
                {
                    tb.Text = fs[0];
                    _settings.LastFolder = fs[0];
                }
            };
        }

        private static bool HasDroppedFolders(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            if (data.GetData(DataFormats.FileDrop) is not string[] paths) return false;
            foreach (var p in paths) if (Directory.Exists(p)) return true;
            return false;
        }

        private static List<string> GetDroppedFolders(IDataObject data)
        {
            var res = new List<string>();
            if (data?.GetData(DataFormats.FileDrop) is string[] paths)
                foreach (var p in paths)
                    if (Directory.Exists(p) && !res.Contains(p)) res.Add(p);
            return res;
        }

        private static List<string> GetDroppedPaths(IDataObject data)
        {
            var result = new List<string>();
            if (data?.GetData(DataFormats.FileDrop) is string[] paths)
                foreach (var path in paths)
                    if ((Directory.Exists(path) || File.Exists(path)) && !result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
            return result;
        }

        private static bool IsValidScanEntry(string path) => Directory.Exists(path) || File.Exists(path);

        private static List<string> GetScanEntries(ListBox list) => list.Items.Cast<string>()
            .Where(IsValidScanEntry).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // =====================================================================
        // Arrastrar y soltar sobre la cuadrícula comparativa: filtra la
        // comparativa a los archivos que coinciden con lo arrastrado.
        // =====================================================================

        private void EnableGridDrop()
        {
            _grid.AllowDrop = true;
            _grid.DragEnter += (s, e) => e.Effect = HasDroppedFileSystemEntries(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            _grid.DragDrop += (s, e) => ApplyDragDropFilter(e.Data);
        }

        private static bool HasDroppedFileSystemEntries(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            return data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0;
        }

        /// <summary>
        /// Al soltar archivos/carpetas del explorador sobre la cuadrícula comparativa:
        /// recorre todas las carpetas y subcarpetas arrastradas (además de los archivos
        /// sueltos), y filtra el escaneo en curso (_allRows) a las filas cuyo lado de
        /// origen o de destino coincide EXACTAMENTE con alguno de los archivos
        /// arrastrados en: NombrePc, ruta completa, nombre de archivo y extensión,
        /// fecha de modificación, fecha de creación y atributos. Las filas que
        /// coinciden se seleccionan y son las únicas que se muestran en la cuadrícula;
        /// si no hay coincidencia con lo arrastrado, la fila no se muestra.
        /// </summary>
        private void ApplyDragDropFilter(IDataObject data)
        {
            if (_allRows.Count == 0)
            {
                Log("No hay una comparativa cargada: escanee origen y destino antes de arrastrar archivos sobre la cuadrícula.");
                return;
            }

            if (data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            var droppedFiles = new List<FileInfo>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        foreach (var fi in FileEnumerator.Enumerate(new[] { p }, CancellationToken.None,
                            (path, ex) => Log($"Aviso: no se pudo leer '{path}' al procesar el arrastre: {ex.Message}")))
                            droppedFiles.Add(fi);
                    }
                    else if (File.Exists(p))
                    {
                        droppedFiles.Add(new FileInfo(p));
                    }
                }
                catch (Exception ex)
                {
                    Log($"Aviso: error procesando el elemento arrastrado '{p}': {ex.Message}");
                }
            }

            if (droppedFiles.Count == 0)
            {
                Log("El arrastre no contenía archivos (solo carpetas vacías o elementos no accesibles).");
                return;
            }

            // El nombre de equipo del archivo arrastrado es el de esta misma máquina,
            // ya que el arrastre se realiza localmente desde el explorador de Windows.
            bool SideMatches(FileInfo fi, string ruta, DateTime? fechaModLado,
                DateTime? fechaCreacionLado)
            {
                if (string.IsNullOrEmpty(ruta)) return false;
                if (!string.Equals(Path.GetFullPath(fi.FullName), Path.GetFullPath(ruta), StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(fi.Name, Path.GetFileName(ruta), StringComparison.OrdinalIgnoreCase)) return false;
                // Comparación de fechas tolerante: evita falsos negativos por distinto
                // DateTimeKind (Local/Utc/Unspecified) entre la fecha recién leída del
                // disco y la guardada en el escaneo, y por diferencias de sub-segundo
                // debidas a la resolución del sistema de archivos (p. ej. FAT32) o a
                // copias/transferencias que redondean milisegundos/ticks. Ver DateUtils.
                if (!fechaModLado.HasValue || !DateUtils.AreFileTimesEquivalent(fi.LastWriteTime, fechaModLado.Value)) return false;
                if (!fechaCreacionLado.HasValue || !DateUtils.AreFileTimesEquivalent(fi.CreationTime, fechaCreacionLado.Value)) return false;
                return true;
            }

            var matched = new List<ComparisonRow>();
            foreach (var row in _allRows)
            {
                bool matchAny = false;
                foreach (var fi in droppedFiles)
                {
                    if (SideMatches(fi, row.RutaOrigen, row.FechaModOrigen, row.FechaCreacionOrigen)
                        || SideMatches(fi, row.RutaDestino, row.FechaModDestino, row.FechaCreacionDestino))
                    {
                        matchAny = true;
                        break;
                    }
                }
                if (matchAny)
                {
                    row.Selected = true;
                    matched.Add(row);
                }
            }

            _dragFilteredRows = matched;
            _dragFilterActive = true;
            // ApplyFilterAndSort() toma como base _dragFilteredRows (por estar
            // _dragFilterActive activo) y le aplica RowPassesFilters con el estado
            // ACTUAL de los filtros de la cuadrícula (texto/rango/fecha y checkboxes
            // de procedencia). Así, en _view solo quedan las filas que a la vez:
            // (a) coinciden con lo arrastrado, y (b) ya pasaban los filtros que
            // estaban activos en la cuadrícula en el momento del arrastre.
            ApplyFilterAndSort();
            Log($"Arrastrar y soltar: {droppedFiles.Count:N0} archivo(s) analizados; {matched.Count:N0} fila(s) coincidentes con el escaneo; {_view.Count:N0} mostrada(s) tras aplicar también los filtros ya activos en la cuadrícula.");
        }

        // =====================================================================
        // Escaneo
        // =====================================================================

        private async Task ScanSingleAsync(ScanScope scope, ListBox folderList)
        {
            var roots = GetScanEntries(folderList);
            if (roots.Count == 0) { Log("No hay carpetas válidas para escanear."); return; }

            await RunScanAsync(scope, folderList);
        }

        private async Task ScanBothAsync()
        {
            var oRoots = GetScanEntries(_originFolders);
            var dRoots = GetScanEntries(_destFolders);
            if (oRoots.Count == 0 && dRoots.Count == 0) { Log("No hay carpetas válidas para escanear."); return; }

            ScanRecord oScan = null;
            if (oRoots.Count > 0)
                oScan = await RunScanAsync(ScanScope.origen, _originFolders);

            if (oScan == null || oScan.Status != ScanStatus.Cancelled)
            {
                if (dRoots.Count > 0)
                    await RunScanAsync(ScanScope.destino, _destFolders);
            }
        }

        private async Task FusionFechasAsync()
        {
            var origins = GetScanEntries(_originFolders);
            var destinations = GetScanEntries(_destFolders);
            string trash = _txtTrash.Text?.Trim();
            if (origins.Count == 0 || destinations.Count == 0 || string.IsNullOrWhiteSpace(trash))
            {
                MessageBox.Show(this, "Indica al menos un origen, un destino y la carpeta papelera.", "Fusión Fechas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cts = new CancellationTokenSource();
            SetScanUI(true);
            try
            {
                var fusion = new DateFusionService(_repo);
                var result = await Task.Run(() => fusion.Execute(origins, destinations, trash, _chkHash.Checked, _cts.Token,
                    message => BeginInvoke(new Action(() => Log("Fusión: " + message)))));
                Log($"Fusión finalizada: {result.Staged} agrupados; {result.MovedToTrash} movidos a Movidos; {result.PendingReview} pendientes. Resultado: {result.ResultFolder}");
                MessageBox.Show(this, $"Fusión terminada. Pendientes de revisión: {result.PendingReview}\n{result.ResultFolder}", "Fusión Fechas", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { Log("Fusión cancelada."); }
            catch (Exception ex) { Log("Error en Fusión Fechas: " + ex.Message); MessageBox.Show(this, ex.Message, "Fusión Fechas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetScanUI(false); }
        }

        private async Task<ScanRecord> RunScanAsync(ScanScope scope, ListBox folderList)
        {
            var roots = GetScanEntries(folderList);
            if (roots.Count == 0)
            {
                Log("No hay carpetas válidas para escanear.");
                return null;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            SetScanUI(running: true);

            var progress = new Progress<ScanProgress>(p =>
            {
                long total = p.TotalEstimated;
                long done = p.FilesProcessed;
                _lblFiles.Text = total > 0 ? $"Archivos: {done:N0} / {total:N0}" : $"Archivos: {done:N0}";
                _lblTime.Text = "Tiempo: " + FormatUtils.Time(p.Elapsed);
                if (total > 0)
                {
                    int pct = (int)(done * 100 / total);
                    if (pct > 100) pct = 100;
                    _progress.OverlayText = null; // el porcentaje se calcula y dibuja solo
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Maximum = 100;
                    _progress.Value = pct;
                }
                else
                {
                    _progress.OverlayText = p.Phase;
                    _progress.Style = ProgressBarStyle.Marquee;
                }
            });

            _scanner = new Scanner(_repo, computeHash: _chkHash.Checked);
            Log($"Exclusiones activas: {ExclusionService.PatternCount:N0} patrón(es) cargados desde \"{ExclusionService.FilePath}\".");

            _progress.Value = 0;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.OverlayText = "Enumerando...";
            _lblFiles.Text = "Archivos: 0 / 0";
            _sw.Restart();
            _uiTimer.Start();

            ScanRecord result = null;
            try
            {
                result = await _scanner.ScanAsync(roots, scope, progress, ct,
                    (path, ex) => BeginInvoke(new Action(() => Log($"[ERROR acceso] {path}: {ex.Message}"))));

                var scan = result;
                if (scan.Status == ScanStatus.Cancelled)
                {
                    if (scan.Scope == ScanScope.origen) _lastOriginScan = scan; else _lastDestScan = scan;
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.OverlayText = "Cancelado";
                    Log($"Escaneo {scan.ScopeLabel} cancelado: {scan.FileCount:N0} archivos procesados, {scan.ErrorCount:N0} errores.");
                    UpdateStatsLabels();
                }
                else
                {
                    Log($"Escaneo {scan.ScopeLabel} finalizado: {scan.FileCount:N0} archivos, {scan.FolderCount:N0} carpetas, {scan.ErrorCount:N0} errores. GUID: {scan.Guid}");
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.OverlayText = null; // el porcentaje (100%) se dibuja solo
                    _progress.Value = 100;

                    if (scan.Scope == ScanScope.origen) _lastOriginScan = scan; else _lastDestScan = scan;

                    // Mantenimiento: conserva solo los últimos Repository.ScansToKeepPerScope
                    // escaneos válidos (Completed) de este ámbito (Origen/Destino), eliminando
                    // los más antiguos de Scans (arrastrando en cascada sus Files y ScanErrors)
                    // para no acumular registros innecesarios.
                    try
                    {
                        int removed = _repo.PruneOldScans(scan.Scope, Repository.ScansToKeepPerScope);
                        if (removed > 0)
                            Log($"Mantenimiento: eliminados {removed} escaneo(s) antiguo(s) de {scan.ScopeLabel} (se conservan los últimos {Repository.ScansToKeepPerScope}).");
                    }
                    catch (Exception ex)
                    {
                        Log($"Aviso: no se pudo depurar escaneos antiguos de {scan.ScopeLabel}: {ex.Message}");
                    }

                    UpdateStatsLabels();
                    TryBuildComparison();
                }
            }
            catch (OperationCanceledException)
            {
                Log("Escaneo cancelado.");
                _progress.OverlayText = "Cancelado";
            }
            catch (Exception ex)
            {
                Log("Error en escaneo: " + ex.Message);
                _progress.OverlayText = "Error";
            }
            finally
            {
                _sw.Stop();
                _uiTimer.Stop();
                _lblTime.Text = "Tiempo: " + FormatUtils.Time(_sw.Elapsed);
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = 100;
                if (_progress.Value > 100) _progress.Value = 100;
                SetScanUI(running: false);
            }

            return result;
        }

        private void CancelScan()
        {
            _cts?.Cancel();
            Log("Solicitada cancelación del escaneo...");
        }

        private void SetScanUI(bool running)
        {
            _btnScanOrigin.Enabled = !running;
            _btnScanDest.Enabled = !running;
            _btnScanBoth.Enabled = !running;
            _btnFusionFechas.Enabled = !running;
            _btnCancel.Enabled = running;
        }

        private void UpdateStatsLabels()
        {
            RenderStats(_lblOriginStats, _lastOriginScan, isOrigin: true);
            RenderStats(_lblDestStats, _lastDestScan, isOrigin: false);
        }

        private void RenderStats(Label lbl, ScanRecord scan, bool isOrigin)
        {
            long files = scan?.FileCount ?? 0;
            long folders = scan?.FolderCount ?? 0;
            string time = scan != null && scan.FinishedAt > DateTime.MinValue
                ? FormatUtils.Time(scan.FinishedAt - scan.StartedAt)
                : "00:00:00";
            long unique = scan != null ? _repo.CountUniqueFiles(scan.Id) : 0;
            string label = isOrigin ? "ORIGEN" : "DESTINO";
            lbl.Text = $"{label} — Encontrados: {files:N0} | Resultantes (únicos): {unique:N0} | Carpetas: {folders:N0} | Tiempo: {time}";
        }

        // =====================================================================
        // Comparativa
        // =====================================================================

        private async void VerUltimoEscaner()
        {
            var origins = _repo.GetScansByScope(ScanScope.origen)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();
            var dests = _repo.GetScansByScope(ScanScope.destino)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();

            if (origins.Count == 0 || dests.Count == 0)
            {
                Log("Ver último escáner: no hay escaneos guardados de origen y destino. No se modifica la tabla.");
                MessageBox.Show(this, "No hay datos de escaneos anteriores guardados (origen y destino).", "Ver último escáner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // recupera la lista de carpetas escaneadas a partir de la tabla scans, una lista por tipo de origen/destino, para poder sustituir la información del ListBox de carpetas en caso de que el usuario quiera volver a escanear.
            _originRoots = origins[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            //List<string> lorigin = origins[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
          
            //if (lorigin.Count > 0)
            if (_originRoots.Length > 0)
                {
                _originFolders.Items.Clear();
                //foreach (var f in lorigin)
                foreach (var f in _originRoots)
                {
                    if (!_originFolders.Items.Contains(f)) _originFolders.Items.Add(f);
                }
            }
            //List<string> ldestino = dests[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _destRoots = dests[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            //if (ldestino.Count > 0)
            if (_destRoots.Length > 0)
            {
                _destFolders.Items.Clear();
                //foreach (var f in ldestino)
                foreach (var f in _destRoots)
                {
                    if (!_destFolders.Items.Contains(f)) _destFolders.Items.Add(f);
                }
            }

            // Carga los escaneos desde la BD
            var scanRecords = await GetScansAsync(); // List<ScanRecord>
                                                     // Proyección explícita de ScanRecord -> Scan
            var scans = scanRecords.Select(sr => new Scan
            {
                Id = (int)sr.Id,
                CreatedAt = sr.StartedAt,
                Scope = sr.Scope.ToString(),
                Description = sr.Notes ?? string.Empty,
                // Mapea aquí cualquier otra propiedad necesaria, por ejemplo RootPaths, Scope, etc.
            }).ToList();

            using var dlg = new CompareScansForm(scans);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                int originId = dlg.SelectedOriginId!.Value;
                int destinationId = dlg.SelectedDestinationId!.Value;
                TryBuildComparison(originId, destinationId);
                Log($"Ver último escáner: cargando la comparativa a partir de los escaneos origen {originId} y destino {destinationId}.");

            }
            //else
            //{
            //    TryBuildComparison(); // comportamiento por defecto: usar los últimos escaneos
            //    Log("Ver último escáner: cargando la comparativa a partir de los últimos escaneos guardados.");
            //}
            
            //TryBuildComparison();
        }

        private async Task<List<ScanRecord>> GetScansAsync()
        {
            // Obtener escaneos usando el repositorio (compatible con Postgres).
            // Si tu repositorio expone métodos async, reemplaza estas llamadas por los métodos async correspondientes.
            var origins = _repo.GetScansByScope(ScanScope.origen)
                .Where(s => s.Status == ScanStatus.Completed);

            var dests = _repo.GetScansByScope(ScanScope.destino)
                .Where(s => s.Status == ScanStatus.Completed);

            var combined = origins
                .Concat(dests)
                .OrderByDescending(s => s.Id)
                .ToList();

            await Task.Yield();
            return combined;
        }
        private void RevisarDuplicados()
        {
            if (_duplicatesReviewMode)
            {
                _duplicatesReviewMode = false;
                _duplicateReviewRows = new List<ComparisonRow>();
                Log("Saliendo del modo revisión de duplicados; se restaura la comparativa.");
                ApplyFilterAndSort();
                return;
            }

            bool includeOrigin = _chkArchivosOrigen.Checked;
            bool includeDest = _chkArchivosDestino.Checked;
            if (!includeOrigin && !includeDest)
            {
                MessageBox.Show(this, "Marca Origen, Destino o ambos para revisar duplicados.",
                    "Revisar Duplicados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var origins = _repo.GetScansByScope(ScanScope.origen)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();
            var dests = _repo.GetScansByScope(ScanScope.destino)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();

            long? originScanId = origins.Count > 0 ? origins[0].Id : (long?)null;
            long? destScanId = dests.Count > 0 ? dests[0].Id : (long?)null;

            if (includeOrigin && !originScanId.HasValue)
            {
                MessageBox.Show(this, "No hay un escaneo de ORIGEN completado para revisar duplicados.",
                    "Revisar Duplicados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (includeDest && !destScanId.HasValue)
            {
                MessageBox.Show(this, "No hay un escaneo de DESTINO completado para revisar duplicados.",
                    "Revisar Duplicados", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string proc = SelectedProcedencia();
            string modo = includeOrigin && includeDest
                ? $"ambos lados (procedencia: {proc})"
                : includeOrigin ? "solo origen" : "solo destino";
            Log($"Revisar Duplicados: analizando duplicados ({modo})...");

            var service = new DuplicateReviewService(_repo, Log);
            var result = service.Review(originScanId, destScanId, includeOrigin, includeDest, proc);

            if (result.GroupCount == 0)
            {
                Log("Revisar Duplicados: no se han encontrado duplicados.");
                MessageBox.Show(this, "No se han encontrado duplicados.",
                    "Revisar Duplicados", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _duplicatesReviewMode = true;
            _dragFilterActive = false;
            _dragFilteredRows = new List<ComparisonRow>();
            _duplicateReviewRows = result.Rows;
            _originRoots = origins.Count > 0
                ? origins[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();
            _destRoots = dests.Count > 0
                ? dests[0].RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();

            Log($"Revisar Duplicados: {result.GroupCount:N0} grupos, {result.DuplicateFileCount:N0} archivos. Entrando en modo revisión (verde = conservar, amarillo = duplicado).");
            ApplyFilterAndSort();
            _grid.Invalidate();
            UpdateSelectedCount();
        }

        //private void TryBuildComparison()
        //{
        //    if (_duplicatesReviewMode)
        //    {
        //        _duplicatesReviewMode = false;
        //        Log("Saliendo del modo revisión de duplicados (nueva comparativa).");
        //    }

        //    var origins = _repo.GetScansByScope(ScanScope.origen)
        //        .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();
        //    var dests = _repo.GetScansByScope(ScanScope.destino)
        //        .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();

        //    if (origins.Count == 0 || dests.Count == 0)
        //    {
        //        Log("Falta un escaneo completado de origen o destino para comparar.");
        //        return;
        //    }

        //    Log($"Construyendo tabla comparativa (origen #{origins[0].Id} vs destino #{dests[0].Id})...");
        //    _lastOriginScan = origins[0];
        //    _lastDestScan = dests[0];
        //    _dragFilterActive = false;
        //    _dragFilteredRows = new List<ComparisonRow>();
        //    _allRows = _repo.BuildComparison(origins[0].Id, dests[0].Id);
        //    _originRoots = origins[0].RootPaths.Split('|', StringSplitOptions.RemoveEmptyEntries);
        //    _destRoots = dests[0].RootPaths.Split('|', StringSplitOptions.RemoveEmptyEntries);
        //    _chkArchivosOrigen.Checked = true;
        //    _chkArchivosDestino.Checked = true;
        //    ApplyFilterAndSort();
        //    UpdateStatsLabels();
        //    Log($"Comparativa lista: {_allRows.Count:N0} filas.");
        //}
        private void TryBuildComparison(int? originId = null, int? destinationId = null)
        {
            if (_duplicatesReviewMode)
            {
                _duplicatesReviewMode = false;
                Log("Saliendo del modo revisión de duplicados (nueva comparativa).");
            }

            var origins = _repo.GetScansByScope(ScanScope.origen)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();
            var dests = _repo.GetScansByScope(ScanScope.destino)
                .Where(s => s.Status == ScanStatus.Completed).OrderByDescending(s => s.Id).ToList();

            if (origins.Count == 0 || dests.Count == 0)
            {
                Log("Falta un escaneo completado de origen o destino para comparar.");
                return;
            }

            // Si se proporcionan ids, buscar ese registro; si no, usar el último (mayor id)
            var originScan = originId.HasValue ? origins.FirstOrDefault(s => s.Id == originId.Value) : origins.FirstOrDefault();
            var destScan = destinationId.HasValue ? dests.FirstOrDefault(s => s.Id == destinationId.Value) : dests.FirstOrDefault();

            if (originScan == null)
            {
                Log($"No se encontró el escaneo de origen solicitado (Id {originId}).");
                return;
            }

            if (destScan == null)
            {
                Log($"No se encontró el escaneo de destino solicitado (Id {destinationId}).");
                return;
            }

            Log($"Construyendo tabla comparativa (origen #{originScan.Id} vs destino #{destScan.Id})...");
            _lastOriginScan = originScan;
            _lastDestScan = destScan;
            _dragFilterActive = false;
            _dragFilteredRows = new List<ComparisonRow>();
            _allRows = _repo.BuildComparison(originScan.Id, destScan.Id);
            _originRoots = originScan.RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _destRoots = destScan.RootPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _chkArchivosOrigen.Checked = true;
            //_chkArchivosDestino.Checked = true;
            _chkArchivosDestino.Checked = false;
            ApplyFilterAndSort();
            UpdateStatsLabels();
            Log($"Comparativa lista: {_allRows.Count:N0} filas.");
        }
        private void ApplyFilterAndSort()
        {
            if (_duplicatesReviewMode)
            {
                _view = new List<ComparisonRow>(_duplicateReviewRows);
                _grid.RowCount = _view.Count;
                _grid.Invalidate();
                _lblRecordCount.Text = $"Duplicados mostrados: {_view.Count:N0} (revisión)";
                UpdateSelectedCount();
                return;
            }

            var baseline = _dragFilterActive ? _dragFilteredRows : _allRows;
            var filtered = baseline.Where(RowPassesFilters).ToList();

            var ordering = BuildOrdering();
            IOrderedEnumerable<ComparisonRow> ordered = null;
            foreach (var (col, desc) in ordering)
            {
                var cmp = GetComparer(col);
                if (cmp == null) continue;
                Comparison<ComparisonRow> c = desc ? (a, b) => cmp(b, a) : cmp;
                var kc = Comparer<ComparisonRow>.Create(c);
                ordered = ordered == null ? filtered.OrderBy(r => r, kc) : ordered.ThenBy(r => r, kc);
            }

            _view = ordered == null ? filtered : ordered.ToList();

            _grid.RowCount = _view.Count;
            _grid.Invalidate();
            _lblRecordCount.Text = _dragFilterActive
                ? $"Mostrados: {_view.Count:N0} de {_dragFilteredRows.Count:N0} (filtro por arrastre)"
                : $"Mostrados: {_view.Count:N0} de {_allRows.Count:N0}";
            UpdateSelectedCount();
        }

        private bool RowPassesFilters(ComparisonRow r)
        {
            bool hasOrigen = !string.IsNullOrEmpty(r.RutaOrigen);
            bool hasDestino = !string.IsNullOrEmpty(r.RutaDestino);
            bool procedenciaOk = (_chkArchivosOrigen.Checked && hasOrigen)
                                 || (_chkArchivosDestino.Checked && hasDestino);
            if (!procedenciaOk) return false;

            foreach (var kv in _kind)
            {
                string name = kv.Key;
                switch (kv.Value)
                {
                    case FilterKind.Text:
                        var t = _fltText[name].Text.Trim();
                        if (t.Length > 0)
                        {
                            // Si el filtro empieza y acaba con *, busqueda por "contiene"
                            if (t.StartsWith("*") && t.EndsWith("*") && t.Length > 2)
                            {
                                var core = t.Substring(1, t.Length - 2);
                                if (!Contains(GetCellValue(r, name), core)) return false;
                            }
                            else
                            {
                                // Comportamiento por defecto: coincidencia por prefijo
                                var val = GetCellValue(r, name);
                                if (string.IsNullOrEmpty(val) || !val.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return false;
                            }
                        }
                        break;

                    case FilterKind.NumberRange:
                        {
                            long? v = GetNumericValue(r, name);
                            var fromS = _fltFrom[name].Text.Trim();
                            var toS = _fltTo[name].Text.Trim();
                            if (fromS.Length > 0 && TryParseLong(fromS, out long lo))
                                if (!v.HasValue || v.Value < lo) return false;
                            if (toS.Length > 0 && TryParseLong(toS, out long hi))
                                if (!v.HasValue || v.Value > hi) return false;
                            break;
                        }

                    case FilterKind.DateRange:
                        {
                            DateTime? v = GetDateValue(r, name);
                            var fromS = _fltFrom[name].Text.Trim();
                            var toS = _fltTo[name].Text.Trim();
                            if (fromS.Length > 0 && DateTime.TryParse(fromS, out var lo))
                                if (!v.HasValue || v.Value < lo) return false;
                            if (toS.Length > 0 && DateTime.TryParse(toS, out var hi))
                            {
                                var end = hi.Date.AddDays(1).AddTicks(-1);
                                if (!v.HasValue || v.Value > end) return false;
                            }
                            break;
                        }
                }
            }
            return true;
        }

        private List<(string col, bool desc)> BuildOrdering()
        {
            int maxCols = _grid.Columns.Count;
            var entries = new List<(int order, string col, bool desc)>();
            foreach (var kv in _ordNum)
            {
                string dir = _ordDir[kv.Key].Text.Trim().ToUpperInvariant();
                if (dir.Length == 0) continue;

                var s = kv.Value.Text.Trim();
                if (!int.TryParse(s, out int ord) || ord < 1 || ord > maxCols)
                {
                    Log("Aviso: orden inválido; el número debe estar entre 1 y el número de columnas. No se aplica ordenación.");
                    return new List<(string, bool)>();
                }

                bool desc = dir == "D";
                entries.Add((ord, kv.Key, desc));
            }

            if (entries.Count == 0) return new List<(string, bool)>();

            var dup = entries.GroupBy(e => e.order).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                string cols = string.Join(", ", dup.Select(e => e.col));
                Log($"Error: número de orden duplicado ({dup.Key}) en las columnas: {cols}. No se permite el mismo número en dos columnas. No se aplica ordenación.");
                return new List<(string, bool)>();
            }

            var orders = entries.Select(e => e.order).OrderBy(o => o).ToList();
            bool valid = true;
            for (int i = 0; i < orders.Count; i++)
                if (orders[i] != i + 1) { valid = false; break; }

            if (!valid)
            {
                Log("Aviso: los números de orden deben ser una secuencia contigua desde 1 (sin huecos ni repeticiones). No se aplica ordenación.");
                return new List<(string, bool)>();
            }

            return entries.OrderBy(e => e.order).Select(e => (e.col, e.desc)).ToList();
        }

        private static bool TryParseLong(string s, out long value)
            => long.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value);

        private static long? GetNumericValue(ComparisonRow r, string col) => col switch
        {
            "TamanoOrigen" => r.TamanoOrigen,
            "TamanoDestino" => r.TamanoDestino,
            _ => null
        };

        private static DateTime? GetDateValue(ComparisonRow r, string col) => col switch
        {
            "FechaModOrigen" => r.FechaModOrigen,
            "FechaModDestino" => r.FechaModDestino,
            _ => null
        };

        private static bool Contains(string value, string text)
            => !string.IsNullOrEmpty(value) && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Grid_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var row = _view[e.RowIndex];
            if (_grid.Columns[e.ColumnIndex].Name == "Sel")
            {
                e.Value = row.Selected;
                return;
            }
            e.Value = GetCellValue(row, _grid.Columns[e.ColumnIndex].Name);
        }

        private static readonly HashSet<string> _pathColumns = new HashSet<string>
        {
            "RutaOrigen", "CarpetaPadreOrigen", "RutaDestino", "CarpetaPadreDestino"
        };

        private Color? GetRowBackColor(ComparisonRow row)
        {
            if (!_duplicatesReviewMode || row?.Candidatura == null) return null;
            return row.Candidatura.Value == 1 ? Color.LightGreen : Color.Khaki;
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            var back = GetRowBackColor(_view[e.RowIndex]);
            if (back.HasValue)
            {
                e.CellStyle.BackColor = back.Value;
                e.CellStyle.SelectionBackColor = ControlPaint.Dark(back.Value);
            }
        }

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string colName = _grid.Columns[e.ColumnIndex].Name;
            if (!_pathColumns.Contains(colName)) return;

            string text = e.Value?.ToString() ?? "";
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);

            Color? reviewBack = e.RowIndex < _view.Count ? GetRowBackColor(_view[e.RowIndex]) : null;
            if (reviewBack.HasValue && !selected)
            {
                using var b = new SolidBrush(reviewBack.Value);
                e.Graphics.FillRectangle(b, e.CellBounds);
            }

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
                var color = selected
                    ? SystemColors.HighlightText
                    : _grid.Columns[e.ColumnIndex].DefaultCellStyle.ForeColor;
                var font = _grid.Columns[e.ColumnIndex].DefaultCellStyle.Font ?? _grid.Font;

                var gs = e.Graphics.Save();
                e.Graphics.SetClip(rect);
                TextRenderer.DrawText(e.Graphics, text, font, rect, color, flags);
                e.Graphics.Restore(gs);
            }

            e.Handled = true;
        }

        private void Grid_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name == "Sel")
            {
                _view[e.RowIndex].Selected = e.Value is bool b ? b : Convert.ToBoolean(e.Value);
                UpdateSelectedCount();
            }
        }

        private static string GetCellValue(ComparisonRow r, string col)
        {
            return col switch
            {
                "Sel" => r.Selected ? "True" : "False",
                "Candidatura" => r.Candidatura?.ToString() ?? "",
                "Estado" => r.EstadoLabel,
                "CarpetaPadreOrigen" => r.CarpetaPadreOrigen ?? "",
                "CarpetaPadreDestino" => r.CarpetaPadreDestino ?? "",
                "RutaOrigen" => r.RutaOrigen ?? "",
                "RutaDestino" => r.RutaDestino ?? "",
                "Nombre" => r.Nombre ?? "",
                "Extension" => r.Extension ?? "",
                "TamanoOrigen" => r.TamanoOrigen.HasValue ? r.TamanoOrigen.Value.ToString("N0") : "",
                "TamanoDestino" => r.TamanoDestino.HasValue ? r.TamanoDestino.Value.ToString("N0") : "",
                "FechaModOrigen" => r.FechaModOrigen?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                "FechaModDestino" => r.FechaModDestino?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                "HashOrigen" => r.HashOrigen ?? "",
                "HashDestino" => r.HashDestino ?? "",
                "Fingerprint" => r.Fingerprint ?? "",
                "EsDuplicado" => r.EsDuplicado ? "Sí" : "No",
                "NombrePc" => r.NombrePc ?? "",
                "RevisadoOrigen" => r.RevisadoOrigen ?? "",
                "RevisadoDestino" => r.RevisadoDestino ?? "",
                _ => ""
            };
        }

        private Comparison<ComparisonRow> GetComparer(string col)
        {
            return col switch
            {
                "Candidatura" => (a, b) => Nullable.Compare(a.Candidatura, b.Candidatura),
                "Estado" => (a, b) => string.Compare(a.EstadoLabel, b.EstadoLabel, StringComparison.OrdinalIgnoreCase),
                "CarpetaPadreOrigen" => (a, b) => string.Compare(a.CarpetaPadreOrigen ?? "", b.CarpetaPadreOrigen ?? "", StringComparison.OrdinalIgnoreCase),
                "CarpetaPadreDestino" => (a, b) => string.Compare(a.CarpetaPadreDestino ?? "", b.CarpetaPadreDestino ?? "", StringComparison.OrdinalIgnoreCase),
                "RutaOrigen" => (a, b) => string.Compare(a.RutaOrigen ?? "", b.RutaOrigen ?? "", StringComparison.OrdinalIgnoreCase),
                "RutaDestino" => (a, b) => string.Compare(a.RutaDestino ?? "", b.RutaDestino ?? "", StringComparison.OrdinalIgnoreCase),
                "Nombre" => (a, b) => string.Compare(a.Nombre ?? "", b.Nombre ?? "", StringComparison.OrdinalIgnoreCase),
                "Extension" => (a, b) => string.Compare(a.Extension ?? "", b.Extension ?? "", StringComparison.OrdinalIgnoreCase),
                "TamanoOrigen" => (a, b) => Nullable.Compare(a.TamanoOrigen, b.TamanoOrigen),
                "TamanoDestino" => (a, b) => Nullable.Compare(a.TamanoDestino, b.TamanoDestino),
                "FechaModOrigen" => (a, b) => Nullable.Compare(a.FechaModOrigen, b.FechaModOrigen),
                "FechaModDestino" => (a, b) => Nullable.Compare(a.FechaModDestino, b.FechaModDestino),
                "HashOrigen" => (a, b) => string.Compare(a.HashOrigen ?? "", b.HashOrigen ?? "", StringComparison.OrdinalIgnoreCase),
                "HashDestino" => (a, b) => string.Compare(a.HashDestino ?? "", b.HashDestino ?? "", StringComparison.OrdinalIgnoreCase),
                "Fingerprint" => (a, b) => string.Compare(a.Fingerprint ?? "", b.Fingerprint ?? "", StringComparison.Ordinal),
                "EsDuplicado" => (a, b) => a.EsDuplicado.CompareTo(b.EsDuplicado),
                "NombrePc" => (a, b) => string.Compare(a.NombrePc ?? "", b.NombrePc ?? "", StringComparison.OrdinalIgnoreCase),
                "RevisadoOrigen" => (a, b) => string.Compare(a.RevisadoOrigen ?? "", b.RevisadoOrigen ?? "", StringComparison.OrdinalIgnoreCase),
                "RevisadoDestino" => (a, b) => string.Compare(a.RevisadoDestino ?? "", b.RevisadoDestino ?? "", StringComparison.OrdinalIgnoreCase),
                _ => null
            };
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string val = GetCellValue(_view[e.RowIndex], _grid.Columns[e.ColumnIndex].Name);
            if (!string.IsNullOrEmpty(val))
            {
                Clipboard.SetText(val);
                Log($"Valor copiado al portapapeles: {Truncate(val, 100)}");
            }
        }

        private void CopyCurrentCellValue()
        {
            if (_grid.CurrentCell == null) return;
            string val = GetCellValue(_view[_grid.CurrentCell.RowIndex], _grid.Columns[_grid.CurrentCell.ColumnIndex].Name);
            if (!string.IsNullOrEmpty(val))
            {
                Clipboard.SetText(val);
                Log($"Valor copiado: {Truncate(val, 100)}");
            }
        }

        // =====================================================================
        // Fechas de modificación distintas (Origen)
        // =====================================================================

        /// <summary>
        /// Recoge todas las carpetas y archivos de ORIGEN (_originFolders), calcula
        /// para cada archivo su fecha de modificación "válida" mediante
        /// <see cref="GetValidModificationDate"/> y muestra, en un formulario aparte,
        /// la lista de fechas distintas (sin hora), ordenadas de mayor a menor, en
        /// formato aaaa-mm-dd.
        /// NOTA: se asume que GetValidModificationDate(FileInfo) ya existe en algún
        /// punto del proyecto (mencionado en la petición original) y devuelve un
        /// DateTime no-nullable. Si su firma real es distinta (otra clase, otro
        /// espacio de nombres, o devuelve DateTime?), ajusta únicamente la llamada
        /// marcada más abajo.
        /// </summary>
        private void ShowFechasDistintas()
        {
            var roots = GetScanEntries(_originFolders);
            if (roots.Count == 0)
            {
                MessageBox.Show(this, "No hay carpetas o archivos de ORIGEN válidos.",
                    "Fechas Dist.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Comparador que ordena de forma DESCENDENTE (mayor a menor).
            var fechas = new SortedSet<DateTime>(Comparer<DateTime>.Create((a, b) => b.CompareTo(a)));
            int procesados = 0, errores = 0;
            
            try
            {
                foreach (var fi in FileEnumerator.Enumerate(roots, CancellationToken.None,
                    (path, ex) =>
                    {
                        errores++;
                        Log($"Aviso: no se pudo leer '{path}' al calcular fechas distintas: {ex.Message}");
                    }))
                {
                    try
                    {
                        // === Ajustar aquí si la firma real de GetValidModificationDate difiere ===

                        DateTime? fechaExif = null; //fecha de toma de la imagen.
                        if (MediaExtensionsService.RestrictToMedia)
                            fechaExif = DateUtils.TryGetExifDateTaken(fi.FullName.ToString());                        
                        DateTime fecha = DateUtils.GetValidModificationDate(fi.FullName, fi.LastWriteTime, fi.CreationTime, fechaExif, MediaExtensionsService.RestrictToMedia);
                        fechas.Add(fecha.Date);
                        procesados++;
                    }
                    catch (Exception ex)
                    {
                        errores++;
                        Log($"Aviso: no se pudo calcular la fecha de modificación de '{fi.FullName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error al calcular fechas distintas: " + ex.Message);
                MessageBox.Show(this, "Error al calcular fechas distintas: " + ex.Message,
                    "Fechas Dist.", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log($"Fechas Dist.: {procesados:N0} archivo(s) analizados, {fechas.Count:N0} fecha(s) distintas, {errores:N0} error(es).");

            if (fechas.Count == 0)
            {
                MessageBox.Show(this, "No se han encontrado archivos en las carpetas de ORIGEN.",
                    "Fechas Dist.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var lista = fechas.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
            using var f = new FechasDistintasForm(lista);
            f.ShowDialog(this);
        }

        // =====================================================================
        // Selección
        // =====================================================================

        private void ChkSelAll_CheckedChanged(object sender, EventArgs e)
        {
            SetAllSelected(_chkSelAll.Checked);
        }

        private void SetAllSelected(bool value)
        {
            foreach (var r in _view) r.Selected = value;
            _grid.Invalidate();
            UpdateSelectedCount();
        }

        private void SyncSelAllCheck()
        {
            bool any = _view.Count > 0;
            bool all = any && _view.All(r => r.Selected);

            _chkSelAll.CheckedChanged -= ChkSelAll_CheckedChanged;
            _chkSelAll.Checked = all;
            _chkSelAll.Enabled = any;
            _chkSelAll.CheckedChanged += ChkSelAll_CheckedChanged;
        }

        private void UpdateSelectedCount()
        {
            int n = _view.Count(r => r.Selected);
            _lblSelectedCount.Text = $"Seleccionados: {n:N0}";
            SyncSelAllCheck();
        }

        // =====================================================================
        // Operaciones de archivo
        // =====================================================================

        private string SelectedProcedencia() => _cmbProcedencia.SelectedItem?.ToString() ?? "destino";

        private IEnumerable<(string path, string[] roots, bool isOrigin)> SidesFor(ComparisonRow r, string proc)
        {
            if (_duplicatesReviewMode)
            {
                if (!string.IsNullOrEmpty(r.RutaOrigen)) yield return (r.RutaOrigen, _originRoots, true);
                else if (!string.IsNullOrEmpty(r.RutaDestino)) yield return (r.RutaDestino, _destRoots, false);
                yield break;
            }

            bool wantO = proc == "origen" || proc == "Ambos";
            bool wantD = proc == "destino" || proc == "Ambos";
            if (wantO && !string.IsNullOrEmpty(r.RutaOrigen)) yield return (r.RutaOrigen, _originRoots, true);
            if (wantD && !string.IsNullOrEmpty(r.RutaDestino)) yield return (r.RutaDestino, _destRoots, false);
        }

        /// <summary>
        /// Calcula, sin ejecutar ninguna operación real sobre el disco (salvo
        /// comprobaciones de existencia de archivo para el versionado), el destino
        /// que se generaría para cada lado (origen/destino) de cada fila
        /// seleccionada, con la configuración actual de Copiar/Mover. Se usa para
        /// mostrar la vista previa de confirmación antes de ejecutar la operación.
        /// </summary>
        private List<OperationConfirmItem> BuildOperationPreview(
            List<ComparisonRow> selected, string proc, string dest, bool destValida, bool useCustomNaming)
        {
            var items = new List<OperationConfirmItem>();
            foreach (var r in selected)
            {
                foreach (var side in SidesFor(r, proc))
                {
                    string destBase;
                    // Cuando la Carpeta Destino (Prioritaria) tiene valor, se usa tal
                    // cual como destino de los archivos: no se busca/replica ninguna
                    // ruta relativa de carpetas (relRoot = null => destino "plano").
                    // La ruta relativa solo se calcula y aplica cuando NO hay carpeta
                    // destino prioritaria y hay que reconstruir la ruta relativa
                    // origen->destino (o destino->origen) en la carpeta correspondiente.
                    string relRoot;
                    if (destValida)
                    {
                        destBase = dest;
                        relRoot = null;
                    }
                    else
                    {
                        relRoot = FindRoot(side.path, side.roots);
                        destBase = ResolveRelativeDestBase(side.isOrigin);
                        if (destBase == null)
                        {
                            items.Add(new OperationConfirmItem
                            {
                                Origen = side.path,
                                Destino = "(no se procesará)",
                                Estado = $"No existe la carpeta de {(side.isOrigin ? "destino" : "origen")} correspondiente para replicar la ruta relativa (carpeta destino vacía).",
                                Procesable = false
                            });
                            continue;
                        }
                    }

                    string plannedPath;
                    if (useCustomNaming)
                    {
                        DateTime? fechaMod = side.isOrigin ? r.FechaModOrigen : r.FechaModDestino;
                        var (_, _, destPath) = ComputeNamedDestination(side.path, destBase, fechaMod);
                        plannedPath = destPath;
                    }
                    else
                    {
                        plannedPath = FileOperationsService.PreviewDestination(side.path, destBase, relRoot, _chkVersionar.Checked);
                    }

                    items.Add(new OperationConfirmItem
                    {
                        Origen = side.path,
                        Destino = plannedPath,
                        Estado = "Pendiente",
                        Procesable = true
                    });
                }
            }
            return items;
        }

        private void CopyMoveSelected(bool move)
        {
            var selected = _view.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No hay filas seleccionadas.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dest = _txtCustomDest.Text?.Trim();
            bool destValida = !string.IsNullOrEmpty(dest) && PathUtils.IsValidDirectory(dest);
            if (!destValida)
            {
                // Si el usuario ha escrito algo en el textbox pero la ruta no es válida,
                // preguntar si quiere crearla. Si responde sí, intentar crearla y actualizar
                // destValida sólo si la creación tiene éxito.
                if (!string.IsNullOrEmpty(dest))
                {
                    var resp = MessageBox.Show(
                        this,
                        $"La carpeta destino \"{dest}\" no existe o no es válida. ¿Desea crearla?",
                        "Crear carpeta destino",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (resp == DialogResult.Yes)
                    {
                        try
                        {
                            Directory.CreateDirectory(dest);
                            // Volver a validar la ruta creada.
                            if (PathUtils.IsValidDirectory(dest))
                            {
                                destValida = true;
                                Log($"Carpeta destino creada: {dest}");
                            }
                            else
                            {
                                MessageBox.Show(this, $"No se pudo crear la carpeta destino: {dest}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                Log($"Error: no se pudo validar la carpeta creada: {dest}");
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, $"Error al crear la carpeta destino: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Log($"Excepción al crear carpeta destino '{dest}': {ex}");
                        }
                    }
                }

                Log("Aviso: la carpeta destino está vacía o no es válida. Para cada archivo se usará la carpeta relativa de origen a destino (o de destino a origen, según su procedencia), siempre que exista la correspondiente carpeta de origen/destino.");
            }

            string proc = SelectedProcedencia();
            bool versionar = _chkVersionar.Checked;
            bool useCustomNaming = _chkPorFechas.Checked || _chkAnadirTitulo.Checked;

            var preview = BuildOperationPreview(selected, proc, dest, destValida, useCustomNaming);
            if (preview.Count == 0)
            {
                MessageBox.Show(this, "No hay archivos que procesar para la procedencia seleccionada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool revisado;
            using (var confirmForm = new OperationConfirmForm(move ? "Mover" : "Copiar", preview))
            {
                if (confirmForm.ShowDialog(this) != DialogResult.OK)
                {
                    Log($"Operación {(move ? "mover" : "copiar")} cancelada por el usuario tras revisar la lista de archivos implicados.");
                    return;
                }
                revisado = confirmForm.Revisado;
            }
            string valorRevisado = revisado ? "S" : "N";

            int ok = 0, fail = 0;
            var consumed = new List<ComparisonRow>();
            foreach (var r in selected)
            {
                bool actedOnAny = false;
                foreach (var side in SidesFor(r, proc))
                {
                    actedOnAny = true;
                    // Marca en Historial.Revisado ('S'/'N' según el checkbox del formulario
                    // de confirmación) el registro correspondiente a este lado del archivo,
                    // en el momento de confirmar la operación (independientemente de si la
                    // copia/movimiento concreto tiene éxito o falla a continuación).
                    MarkHistorialRevisado(r, side.isOrigin, valorRevisado);

                    // relRoot: carpeta raíz de origen/destino del escaneo de donde
                    // procede el archivo; se usa para la copia de seguridad en
                    // Papelera/Movidos (que sí conserva siempre la ruta relativa,
                    // independientemente de la Carpeta Destino Prioritaria).
                    string relRoot = FindRoot(side.path, side.roots);

                    // relRootForDest: ruta relativa a aplicar sobre la Carpeta Destino
                    // Prioritaria o la carpeta relativa resuelta. Cuando hay Carpeta
                    // Destino Prioritaria (destValida), NO se busca ni se replica
                    // ninguna ruta relativa: los archivos van directamente a esa
                    // carpeta (destino "plano").
                    string destBase;
                    string relRootForDest;
                    if (destValida)
                    {
                        destBase = dest;
                        relRootForDest = null;
                    }
                    else
                    {
                        relRootForDest = relRoot;
                        destBase = ResolveRelativeDestBase(side.isOrigin);
                        if (destBase == null)
                        {
                            fail++;
                            Log($"Fallo: {side.path}: no existe la carpeta de {(side.isOrigin ? "destino" : "origen")} correspondiente para replicar la ruta relativa (carpeta destino vacía).");
                            continue;
                        }
                    }

                    if (move) BackupMovedFile(side.path, relRoot);

                    if (useCustomNaming)
                    {
                        DateTime? fechaMod = side.isOrigin ? r.FechaModOrigen : r.FechaModDestino;
                        var res2 = CopyOrMoveWithNaming(side.path, destBase, fechaMod, move);
                        if (res2.success)
                        {
                            ok++;
                            Log($"{(move ? "Movido" : "Copiado")}: {side.path} -> {res2.finalPath}");
                            if (move)
                            {
                                if (side.isOrigin) r.RutaOrigen = null; else r.RutaDestino = null;
                                if (revisado) DeleteFromFilesTable(r, side.isOrigin);
                            }
                        }
                        else
                        {
                            fail++;
                            Log($"Fallo: {side.path}: {res2.error}");
                        }
                    }
                    else
                    {
                        var res = move ? _ops.Move(side.path, destBase, relRootForDest, versionar) : _ops.Copy(side.path, destBase, relRootForDest, versionar);
                        if (res.Success)
                        {
                            ok++;
                            Log($"{(move ? "Movido" : "Copiado")}: {res.Source} -> {res.Destination}" + (string.IsNullOrEmpty(res.Warning) ? "" : $" ({res.Warning})"));
                            if (move)
                            {
                                if (side.isOrigin) r.RutaOrigen = null; else r.RutaDestino = null;
                                if (revisado) DeleteFromFilesTable(r, side.isOrigin);
                            }
                        }
                        else
                        {
                            fail++;
                            Log($"Fallo: {res.Source}: {res.Error}");
                        }
                    }
                }

                if (!actedOnAny) continue;
                if (move && string.IsNullOrEmpty(r.RutaOrigen) && string.IsNullOrEmpty(r.RutaDestino))
                    consumed.Add(r);
            }

            var source = _duplicatesReviewMode ? _duplicateReviewRows : _allRows;
            foreach (var r in consumed) source.Remove(r);
            ApplyFilterAndSort();
            Log($"Operación {(move ? "mover" : "copiar")} ({(_duplicatesReviewMode ? "revisión" : proc)}): {ok} OK, {fail} fallos.");
        }

        /// <summary>
        /// Cuando la carpeta destino (prioritaria) está vacía o no es válida, resuelve
        /// la carpeta y nombre de destino que generaría el nombrado personalizado (por fechas / con título) para un
        /// archivo dado. Se usa tanto para la vista previa de confirmación como,
        /// dentro de <see cref="CopyOrMoveWithNaming"/>, para la ejecución real.
        /// </summary>
        private (string destFolder, string fileName, string destPath) ComputeNamedDestination(string sourcePath, string destBaseFolder, DateTime? fechaMod)
        {
            string originalFileName = Path.GetFileName(sourcePath);
            string titulo = _txtTituloCarpeta.Text?.Trim() ?? "";
            bool porFechas = _chkPorFechas.Checked;
            bool anadirTitulo = _chkAnadirTitulo.Checked;

            string destFolder = destBaseFolder;
            string fileName = originalFileName;

            if (porFechas)
            {
                string fechaStr = (fechaMod ?? DateTime.Now).ToString("yyyy-MM-dd");
                string subFolderName = fechaStr + titulo;
                destFolder = Path.Combine(destBaseFolder, subFolderName);
                if (anadirTitulo)
                    fileName = subFolderName + "#" + originalFileName;
            }
            else if (anadirTitulo)
            {
                fileName = titulo + "#" + originalFileName;
            }

            string destPath = Path.Combine(destFolder, fileName);

            if (_chkVersionar.Checked && File.Exists(destPath))
            {
                string dir = Path.GetDirectoryName(destPath);
                string baseName = Path.GetFileNameWithoutExtension(destPath);
                string ext = Path.GetExtension(destPath);
                int i = 1;
                string candidate;
                do
                {
                    candidate = Path.Combine(dir, $"{baseName}_{i}{ext}");
                    i++;
                } while (File.Exists(candidate));
                destPath = candidate;
            }

            return (destFolder, fileName, destPath);
        }

        private (bool success, string finalPath, string error) CopyOrMoveWithNaming(string sourcePath, string destBaseFolder, DateTime? fechaMod, bool move)
        {
            try
            {
                var (destFolder, _, destPath) = ComputeNamedDestination(sourcePath, destBaseFolder, fechaMod);

                Directory.CreateDirectory(destFolder);

                if (!_chkVersionar.Checked && File.Exists(destPath))
                    File.Delete(destPath);

                if (move)
                    File.Move(sourcePath, destPath);
                else
                    File.Copy(sourcePath, destPath, overwrite: true);

                return (true, destPath, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Calcula, sin ejecutar ninguna operación real sobre el disco, el destino en
        /// la papelera que se generaría para cada lado (origen/destino) de cada fila
        /// seleccionada. Se usa para mostrar la vista previa de confirmación antes de
        /// ejecutar la eliminación (mover a papelera), igual que se hace para
        /// Copiar/Mover en <see cref="BuildOperationPreview"/>.
        /// </summary>
        private List<OperationConfirmItem> BuildTrashPreview(List<ComparisonRow> selected, string proc, string trash)
        {
            var items = new List<OperationConfirmItem>();
            foreach (var r in selected)
            {
                foreach (var side in SidesFor(r, proc))
                {
                    string relRoot = FindRoot(side.path, side.roots);
                    string plannedPath = FileOperationsService.PreviewTrashDestination(side.path, relRoot, trash);
                    items.Add(new OperationConfirmItem
                    {
                        Origen = side.path,
                        Destino = plannedPath,
                        Estado = "Pendiente",
                        Procesable = true
                    });
                }
            }
            return items;
        }

        private void OperateTrash()
        {
            var selected = _view.Where(r => r.Selected).ToList();
            if (selected.Count == 0) { Log("No hay filas seleccionadas."); return; }
            string trash = _txtTrash.Text?.Trim();
            if (string.IsNullOrEmpty(trash)) { Log("No hay carpeta de papelera definida."); return; }
            Directory.CreateDirectory(trash);

            string proc = SelectedProcedencia();

            var preview = BuildTrashPreview(selected, proc, trash);
            if (preview.Count == 0)
            {
                MessageBox.Show(this, "No hay archivos que procesar para la procedencia seleccionada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool revisado;
            using (var confirmForm = new OperationConfirmForm("Eliminar", preview))
            {
                if (confirmForm.ShowDialog(this) != DialogResult.OK)
                {
                    Log("Operación eliminar (papelera) cancelada por el usuario tras revisar la lista de archivos implicados.");
                    return;
                }
                revisado = confirmForm.Revisado;
            }
            string valorRevisado = revisado ? "S" : "N";

            int ok = 0, fail = 0;
            var consumed = new List<ComparisonRow>();
            foreach (var r in selected)
            {
                bool actedOnAny = false;
                foreach (var side in SidesFor(r, proc))
                {
                    actedOnAny = true;
                    // Marca en Historial.Revisado el registro correspondiente a este lado
                    // del archivo, en el momento de confirmar la eliminación.
                    MarkHistorialRevisado(r, side.isOrigin, valorRevisado);

                    string relRoot = FindRoot(side.path, side.roots);
                    var res = _ops.SendToTrash(side.path, relRoot, trash);
                    if (res.Success)
                    {
                        ok++;
                        Log($"A papelera: {res.Source} -> {res.Destination}" + (string.IsNullOrEmpty(res.Warning) ? "" : $" ({res.Warning})"));
                        if (side.isOrigin) r.RutaOrigen = null; else r.RutaDestino = null;
                        if (revisado) DeleteFromFilesTable(r, side.isOrigin);
                    }
                    else
                    {
                        fail++;
                        Log($"Fallo papelera: {res.Source}: {res.Error}");
                    }
                }

                if (!actedOnAny) continue;
                if (string.IsNullOrEmpty(r.RutaOrigen) && string.IsNullOrEmpty(r.RutaDestino))
                    consumed.Add(r);
            }

            var source = _duplicatesReviewMode ? _duplicateReviewRows : _allRows;
            foreach (var r in consumed) source.Remove(r);
            ApplyFilterAndSort();
            Log($"Eliminación a papelera ({(_duplicatesReviewMode ? "revisión" : proc)}): {ok} OK, {fail} fallos.");
        }

        /// <summary>
        /// Marca (best-effort) en Historial.Revisado el registro correspondiente al
        /// lado (origen/destino) de la fila indicada, usando la misma clave única que
        /// Historial (NombrePc + FullPath + Size + CreationTime + LastWriteTime +
        /// Attributes). Se invoca al confirmar una operación de Copiar/Mover/Eliminar,
        /// con "S" o "N" según el estado del checkbox "Revisado" del formulario de
        /// confirmación. No lanza si el archivo no existe en Historial (por ejemplo,
        /// porque nunca se calculó su hash): simplemente no hay nada que marcar.
        /// </summary>
        private void MarkHistorialRevisado(ComparisonRow r, bool isOrigin, string valor)
        {
            try
            {
                string fullPath = isOrigin ? r.RutaOrigen : r.RutaDestino;
                long? size = isOrigin ? r.TamanoOrigen : r.TamanoDestino;
                DateTime? creation = isOrigin ? r.FechaCreacionOrigen : r.FechaCreacionDestino;
                DateTime? lastWrite = isOrigin ? r.FechaModOrigen : r.FechaModDestino;
                if (string.IsNullOrEmpty(fullPath) || !size.HasValue || !creation.HasValue
                    || !lastWrite.HasValue)
                    return;

                _repo.UpdateHistorialRevisado(fullPath, size.Value,
                    creation.Value, lastWrite.Value, valor);
            }
            catch
            {
                // Best-effort: la marca de revisión en Historial no debe interrumpir
                // ni invalidar la operación principal de Copiar/Mover/Eliminar.
            }
        }

        /// <summary>
        /// Elimina de la tabla Files (para que no se vuelva a tener en cuenta en la
        /// próxima comparativa) el registro correspondiente al lado (origen/destino)
        /// de la fila indicada. Se invoca solo cuando la operación de Eliminar
        /// (papelera) o Mover se ha confirmado con la marca "Revisado" activada, y
        /// solo tras completarse con éxito la operación sobre ese lado. Best-effort:
        /// si no se conoce el Id de Files para ese lado (por ejemplo, filas sin hash
        /// calculado en según qué combinaciones), no hace nada.
        /// </summary>
        private void DeleteFromFilesTable(ComparisonRow r, bool isOrigin)
        {
            try
            {
                long? fileId = isOrigin ? r.FileIdOrigen : r.FileIdDestino;
                if (!fileId.HasValue) return;
                _repo.DeleteFile(fileId.Value);
                if (isOrigin) r.FileIdOrigen = null; else r.FileIdDestino = null;
            }
            catch
            {
                // Best-effort: no debe interrumpir ni invalidar la operación principal.
            }
        }

        private static string FindRoot(string path, string[] roots)
        {
            if (roots == null) return null;
            foreach (var r in roots)
                if (!string.IsNullOrEmpty(r) && path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                    return r;
            return null;
        }

        // =====================================================================
        // Exportar a Excel
        // =====================================================================

        private void ExportToExcel()
        {
            var selected = _view.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No hay filas seleccionadas para exportar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new FolderBrowserDialog { Description = "Seleccionar carpeta para el archivo Excel" };
            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
                dlg.SelectedPath = _settings.LastFolder;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string folder = dlg.SelectedPath;
            _settings.LastFolder = folder;
            string file = Path.Combine(folder, $"Comparativa_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            try
            {
                var cols = _grid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.Name != "Sel")
                    .OrderBy(c => c.DisplayIndex)
                    .ToList();

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Comparativa");
                for (int i = 0; i < cols.Count; i++)
                    ws.Cell(1, i + 1).Value = cols[i].HeaderText;
                ws.Row(1).Style.Font.Bold = true;

                for (int rIdx = 0; rIdx < selected.Count; rIdx++)
                    for (int c = 0; c < cols.Count; c++)
                        ws.Cell(rIdx + 2, c + 1).Value = GetCellValue(selected[rIdx], cols[c].Name);

                ws.Columns().AdjustToContents();
                wb.SaveAs(file);

                Log($"Exportado a Excel: {file} ({selected.Count:N0} filas).");
                MessageBox.Show(this, $"Exportadas {selected.Count:N0} filas a:\n{file}", "Exportar a Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Error exportando a Excel: " + ex.Message);
                MessageBox.Show(this, "Error exportando a Excel: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowScansErrors()
        {
            using var f = new ScansErrorsForm(_repo);
            f.ShowDialog(this);
        }

        // =====================================================================
        // Persistencia de columnas
        // =====================================================================

        private void RestoreColumnLayout()
        {
            var names = _grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Name).ToHashSet();

            foreach (DataGridViewColumn c in _grid.Columns)
                if (_settings.ColumnWidths.TryGetValue(c.Name, out int w) && w > 10)
                    c.Width = w;

            foreach (var kv in _settings.ColumnOrder.Where(kv => names.Contains(kv.Key)).OrderBy(kv => kv.Value))
            {
                int di = Math.Max(0, Math.Min(_grid.Columns.Count - 1, kv.Value));
                try { _grid.Columns[kv.Key].DisplayIndex = di; } catch { }
            }
        }

        private void SaveColumnLayout()
        {
            foreach (DataGridViewColumn c in _grid.Columns)
            {
                _settings.ColumnWidths[c.Name] = c.Width;
                _settings.ColumnOrder[c.Name] = c.DisplayIndex;
            }
        }

        // =====================================================================
        // Utilidades
        // =====================================================================

        private void Log(string msg)
        {
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            SaveColumnLayout();

            // Persistir valores de UI en settings.json
            try
            {
                _settings.CalcularHashBlake3 = _chkHash.Checked;
                _settings.SoloImagenes = _chkMediaOnly.Checked;
                _settings.SoloNoImagenes = _chkNonMediaOnly.Checked;
                _settings.CarpetaDestinoPrioritaria = _txtCustomDest.Text?.Trim() ?? "";
                _settings.TituloCarpeta = _txtTituloCarpeta.Text?.Trim() ?? "";
                _settings.EleccionProcedencia = _cmbProcedencia.SelectedItem?.ToString() ?? _settings.EleccionProcedencia;
                _settings.ArchivosOrigen = _chkArchivosOrigen.Checked;
                _settings.ArchivosDestino = _chkArchivosDestino.Checked;
                _settings.PorFechas = _chkPorFechas.Checked;
                _settings.AnadirTitulo = _chkAnadirTitulo.Checked;
                _settings.Versionar = _chkVersionar.Checked;
                _settings.CarpetaPapelera = _txtTrash.Text?.Trim() ?? "";
                _settings.MediaOnly = _chkMediaOnly?.Checked ?? false;
            }
            catch { }

            _settings.Save();
            _uiTimer.Stop();
            _uiTimer.Dispose();
            _db?.Dispose();
            base.OnFormClosing(e);
        }

        // =====================================================================
        // Barra de progreso con el porcentaje (u otro texto de estado breve)
        // dibujado superpuesto sobre la propia barra, en vez de en una etiqueta
        // aparte. Cuando OverlayText está vacío y el estilo no es Marquee, se
        // calcula y dibuja automáticamente el porcentaje ("NN%") a partir de
        // Value/Minimum/Maximum. Cuando OverlayText tiene contenido (p. ej.,
        // "Enumerando...", "Cancelado", "Error"), se muestra ese texto en su
        // lugar, tanto en modo Marquee como en modo Continuous.
        // =====================================================================
        private sealed class PercentProgressBar : ProgressBar
        {
            private string _overlayText;

            public string OverlayText
            {
                get => _overlayText;
                set
                {
                    if (_overlayText == value) return;
                    _overlayText = value;
                    Invalidate();
                }
            }

            public PercentProgressBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            // ProgressBar no expone eventos/overrides virtuales para Value, Maximum,
            // Minimum ni Style, así que se ocultan (new) para forzar el repintado del
            // control (Invalidate) cada vez que el formulario cambia alguno de ellos;
            // de lo contrario, al ser un control UserPaint, no se actualizaría solo.
            public new int Value
            {
                get => base.Value;
                set { base.Value = value; Invalidate(); }
            }

            public new int Maximum
            {
                get => base.Maximum;
                set { base.Maximum = value; Invalidate(); }
            }

            public new int Minimum
            {
                get => base.Minimum;
                set { base.Minimum = value; Invalidate(); }
            }

            public new ProgressBarStyle Style
            {
                get => base.Style;
                set { base.Style = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var rect = ClientRectangle;

                using (var bg = new SolidBrush(SystemColors.ControlLightLight))
                    g.FillRectangle(bg, rect);

                double percent = 0;
                if (Style != ProgressBarStyle.Marquee && Maximum > Minimum)
                    percent = Math.Max(0, Math.Min(1, (double)(Value - Minimum) / (Maximum - Minimum)));

                int fillWidth = Style == ProgressBarStyle.Marquee ? 0 : (int)(rect.Width * percent);
                if (fillWidth > 0)
                {
                    var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
                    using (var fg = new SolidBrush(Color.FromArgb(0, 158, 65)))
                        g.FillRectangle(fg, fillRect);
                }

                using (var pen = new Pen(SystemColors.ControlDark))
                    g.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);

                string text = OverlayText;
                if (string.IsNullOrEmpty(text) && Style != ProgressBarStyle.Marquee)
                    text = $"{(int)Math.Round(percent * 100)}%";

                if (!string.IsNullOrEmpty(text))
                {
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var textBrush = new SolidBrush(Color.Black))
                        g.DrawString(text, Font, textBrush, rect, fmt);
                }
            }
        }

        // Add this method to MainForm (or the relevant class if BackupMovedFile should be elsewhere)
        private void BackupMovedFile(string path, string relRoot)
        {
            // Implement backup logic here, or leave empty if not needed yet.
            // Example placeholder:
            // File.Copy(path, Path.Combine(backupDirectory, relRoot, Path.GetFileName(path)), true);
        }

        // Add this method to your MainForm class (or wherever appropriate)
        private string ResolveRelativeDestBase(bool isOrigin)
        {
            // Implement logic to resolve the destination base path.
            // Placeholder: adjust as needed for your application logic.
            return isOrigin ? _originFolders.Items.Cast<string>().FirstOrDefault() ?? string.Empty
                            : _destFolders.Items.Cast<string>().FirstOrDefault() ?? string.Empty;
        }
    }
}
