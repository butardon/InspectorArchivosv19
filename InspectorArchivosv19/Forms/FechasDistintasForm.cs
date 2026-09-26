using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InspectorArchivos.Forms
{
    /// <summary>
    /// Muestra una lista de fechas (formato aaaa-mm-dd) ya recibidas ordenadas
    /// (descendente), sin volver a ordenarlas ni filtrarlas. Usado por el botón
    /// "Fechas Dist." de MainForm.
    /// </summary>
    public class FechasDistintasForm : Form
    {
        public FechasDistintasForm(IEnumerable<string> fechas)
        {
            Text = "Fechas de modificación distintas (Origen)";
            Width = 320;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 10)
            };
            list.Items.AddRange(fechas.ToArray());

            var btnClose = new Button
            {
                Text = "Cerrar",
                Dock = DockStyle.Bottom,
                Height = 30
            };
            btnClose.Click += (s, e) => Close();

            Controls.Add(list);
            Controls.Add(btnClose);
        }
    }
}
