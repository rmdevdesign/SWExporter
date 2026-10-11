using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SwMultiExport.Model;

namespace SwMultiExport.UI
{
    internal sealed class ExportDialog : Form
    {
        private static readonly Color Accent = Color.FromArgb(31, 98, 218);
        private static readonly Color Ink = Color.FromArgb(26, 37, 56);
        private static readonly Color Muted = Color.FromArgb(91, 105, 124);
        private readonly TextBox directory = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox fileName = new TextBox { Dock = DockStyle.Fill };
        private readonly NumericUpDown scale = new NumericUpDown { DecimalPlaces = 4, Minimum = 0.0001m, Maximum = 1000000, Value = 1, Width = 160 };
        private readonly CheckBox glb = Format("GLB", "Modele et textures reunis");
        private readonly CheckBox gltf = Format("glTF", "Modele avec fichiers associes");
        private readonly CheckBox fbx = Format("FBX", "Modele avec dossier de textures");
        private readonly RadioButton automatic = new RadioButton { Text = "Automatique", Checked = true, AutoSize = true };
        private readonly RadioButton adjustable = new RadioButton { Text = "Reglable", AutoSize = true };
        private readonly RadioButton original = new RadioButton { Text = "Maillage original", AutoSize = true };
        private readonly TrackBar keep = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, TickFrequency = 25, Dock = DockStyle.Fill, AutoSize = false, Height = 38 };
        private readonly NumericUpDown keepNumber = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 50, Width = 80 };
        private readonly Label detail = new Label { AutoSize = true, ForeColor = Muted, Dock = DockStyle.Fill };
        private readonly Label targetNote = new Label { AutoSize = true, ForeColor = Muted, Dock = DockStyle.Fill };
        private readonly Label summary = new Label { AutoSize = true, ForeColor = Muted, Dock = DockStyle.Fill };
        private readonly TableLayoutPanel manualControls = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 2 };
        public ExportOptions Options { get; private set; }

        public ExportDialog(string sourcePath, string title)
        {
            Text = "SW Multi Export";
            Font = new Font("Segoe UI", 10);
            ForeColor = Ink;
            BackColor = Color.FromArgb(242, 245, 250);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1080, 800);
            MinimumSize = new Size(320, 240);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            directory.Text = string.IsNullOrEmpty(sourcePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(sourcePath);
            fileName.Text = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(sourcePath) ? title : sourcePath);
            foreach (char c in Path.GetInvalidFileNameChars()) fileName.Text = fileName.Text.Replace(c, '_');

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16, 12, 16, 12) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(shell);

            var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 0, 0, 12) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.Controls.Add(new Label { Text = "Exporter votre modele", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, Margin = new Padding(0) }, 0, 0);
            header.Controls.Add(new Label { Text = title + "  |  Corps, materiaux et textures", ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 5, 0, 0) }, 0, 1);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var content = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5, Location = Point.Empty, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(header, 0, 0);
            scroll.Controls.Add(content); shell.Controls.Add(scroll, 0, 0);
            Action fitContent = () =>
            {
                // Contraindre la largeur avant le calcul de hauteur des textes.
                // Dock + AutoSize seul calcule une largeur preferee hors ecran.
                int width = Math.Max(100, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
                content.MinimumSize = new Size(width, 0); content.MaximumSize = new Size(width, 0); content.Width = width;
                foreach (Control card in content.Controls)
                    foreach (Control child in card.Controls)
                        if (child is Label) child.MaximumSize = new Size(Math.Max(40, width - card.Padding.Horizontal - 12), 0);
            };
            scroll.SizeChanged += (s, e) => fitContent();

            var formatsCard = Card("01  Formats", "Selectionnez un ou plusieurs formats.");
            var formats = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, Height = 70, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            formats.Controls.AddRange(new Control[] { glb, gltf, fbx });
            bool sizingFormats = false;
            formats.SizeChanged += (s, e) =>
            {
                if (sizingFormats) return;
                sizingFormats = true;
                try
                {
                    int ideal = TextRenderer.MeasureText("Modele avec fichiers associes", Font).Width + gltf.Padding.Horizontal + 16;
                    bool stacked = formats.ClientSize.Width < ideal * 3 + 30;
                    formats.FlowDirection = stacked ? FlowDirection.TopDown : FlowDirection.LeftToRight;
                    int width = Math.Max(60, stacked ? formats.ClientSize.Width - 10 : (formats.ClientSize.Width - 30) / 3);
                    int height = TextRenderer.MeasureText("Ag", Font).Height * 2 + glb.Padding.Vertical + 20;
                    foreach (var format in new[] { glb, gltf, fbx }) { format.Width = width; format.Height = height; }
                    formats.Height = stacked ? (height + 4) * 3 : height + 4;
                }
                finally { sizingFormats = false; }
            };
            formatsCard.Controls.Add(formats); content.Controls.Add(formatsCard, 0, 1);

            var destinationCard = Card("02  Destination", "Choisissez le dossier et le nom des fichiers exportes.");
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 6, Margin = new Padding(0, 12, 0, 0) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            for (int i = 0; i < 6; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var directoryLabel = FieldLabel("Dossier"); fields.Controls.Add(directoryLabel, 0, 0); fields.SetColumnSpan(directoryLabel, 2);
            fields.Controls.Add(directory, 0, 1);
            var browse = Button("Parcourir...", false); browse.Dock = DockStyle.Fill; browse.Height = 32;
            browse.Click += (s, e) => { using (var picker = new FolderBrowserDialog { SelectedPath = directory.Text }) if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath; };
            fields.Controls.Add(browse, 1, 1);
            var nameLabel = FieldLabel("Nom du fichier"); fields.Controls.Add(nameLabel, 0, 2); fields.SetColumnSpan(nameLabel, 2);
            fields.Controls.Add(fileName, 0, 3); fields.SetColumnSpan(fileName, 2);
            var scaleLabel = FieldLabel("Echelle"); fields.Controls.Add(scaleLabel, 0, 4); fields.SetColumnSpan(scaleLabel, 2);
            fields.Controls.Add(scale, 0, 5);
            destinationCard.Controls.Add(fields);
            destinationCard.Controls.Add(Note("Echelle 1 = dimensions d'origine. Axe vertical Y. GLB/glTF en metres, FBX en centimetres."));
            content.Controls.Add(destinationCard, 0, 2);

            var optimizationCard = Card("03  Optimisation", "Alleger le fichier en gardant le controle sur la geometrie.");
            var modes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
            foreach (var mode in new[] { automatic, adjustable, original }) { mode.Margin = new Padding(0, 0, 28, 0); modes.Controls.Add(mode); mode.CheckedChanged += (s, e) => UpdateMode(); }
            optimizationCard.Controls.Add(modes);
            optimizationCard.Controls.Add(detail);
            manualControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            manualControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            manualControls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            var keepLabel = FieldLabel("Triangles a conserver");
            manualControls.Controls.Add(keepLabel, 0, 0); manualControls.SetColumnSpan(keepLabel, 3);
            manualControls.Controls.Add(keep, 0, 1); manualControls.Controls.Add(keepNumber, 1, 1); manualControls.Controls.Add(FieldLabel("%"), 2, 1);
            optimizationCard.Controls.Add(manualControls);
            optimizationCard.Controls.Add(targetNote);
            content.Controls.Add(optimizationCard, 0, 3);
            keep.ValueChanged += (s, e) => { keepNumber.Value = keep.Value; UpdateMode(); };
            keepNumber.ValueChanged += (s, e) => { keep.Value = (int)keepNumber.Value; UpdateMode(); };

            var recap = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            recap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            recap.RowStyles.Add(new RowStyle(SizeType.AutoSize)); recap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            recap.Controls.Add(summary, 0, 0); recap.Controls.Add(Note("Le bilan affichera les triangles avant et apres."), 0, 1);
            content.Controls.Add(recap, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(0, 12, 0, 0) };
            var export = Button("Exporter", true); export.Width = 145;
            var cancel = Button("Annuler", false); cancel.Width = 115; cancel.DialogResult = DialogResult.Cancel;
            export.Click += Confirm; buttons.Controls.AddRange(new Control[] { export, cancel });
            shell.Controls.Add(buttons, 0, 1);
            foreach (var format in new[] { glb, gltf, fbx }) format.CheckedChanged += (s, e) => UpdateMode();
            AcceptButton = export; CancelButton = cancel; UpdateMode();
            Shown += (s, e) =>
            {
                int textHeight = TextRenderer.MeasureText("Ag", Font).Height;
                export.Height = cancel.Height = Math.Max(42, textHeight + 20);
                export.Width = Math.Max(110, TextRenderer.MeasureText(export.Text, Font).Width + 48);
                cancel.Width = Math.Max(100, TextRenderer.MeasureText(cancel.Text, Font).Width + 48);
                browse.Height = directory.PreferredHeight + 6;
                fields.ColumnStyles[1].Width = TextRenderer.MeasureText(browse.Text, Font).Width + 24;
                fitContent(); content.PerformLayout();
                var work = Screen.FromControl(this).WorkingArea;
                // Afficher tout le contenu si l'ecran le permet, sinon limiter
                // la fenetre a l'espace de travail et activer le defilement.
                MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width - 32), Math.Min(MinimumSize.Height, work.Height - 32));
                int desiredHeight = content.PreferredSize.Height + buttons.PreferredSize.Height + shell.Padding.Vertical + Height - ClientSize.Height + 16;
                Size = new Size(Math.Min(Width, work.Width - 32), Math.Min(desiredHeight, work.Height - 32));
                Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
                fitContent();
            };
        }

        private void UpdateMode()
        {
            manualControls.Enabled = adjustable.Checked;
            manualControls.Visible = adjustable.Checked;
            detail.Text = automatic.Checked ? "Retriangule les zones planes. Protege contours, trous, normales et UV. Les courbes restent intactes." :
                adjustable.Checked ? "Reduit le nombre de triangles. Protege les bords et les coutures ; les courbes peuvent changer." :
                "Conserve tous les triangles du modele. Les sommets identiques et les occurrences restent optimises.";
            targetNote.Text = adjustable.Checked ? "Cible : conserver " + keep.Value + " % des triangles (reduction de " + (100 - keep.Value) + " %). La cible peut etre limitee par les bords et les coutures." :
                automatic.Checked ? "Reduction automatique conservatrice, sans taux impose. Le gain depend des surfaces planes du modele." : "Aucune decimation.";
            summary.Text = new[] { glb, gltf, fbx }.Count(c => c.Checked) + " format(s) selectionne(s)  |  " +
                (automatic.Checked ? "Automatique" : adjustable.Checked ? "Conserver " + keep.Value + " %" : "Maillage original");
        }

        private static TableLayoutPanel Card(string title, string subtitle)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 10, Padding = new Padding(14, 10, 14, 10), BackColor = Color.White, Margin = new Padding(0, 0, 0, 10) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 10; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 5) });
            panel.Controls.Add(Note(subtitle)); return panel;
        }
        private static Label FieldLabel(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
        private static Label Note(string text) => new Label { Text = text, AutoSize = true, ForeColor = Muted, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        private static Button Button(string text, bool primary)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, Height = 42, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(8, 0, 0, 0) };
            button.FlatAppearance.BorderSize = primary ? 0 : 1; button.FlatAppearance.BorderColor = Color.FromArgb(209, 217, 229); return button;
        }
        private static CheckBox Format(string name, string description)
        {
            var check = new CheckBox { Text = "✓ " + name + "\n" + description, Checked = true, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
                Size = new Size(246, 66), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 3, 8, 3), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 4) };
            check.FlatAppearance.CheckedBackColor = Color.FromArgb(231, 239, 255); check.FlatAppearance.BorderColor = Color.FromArgb(187, 207, 243);
            check.CheckedChanged += (s, e) => { check.Text = (check.Checked ? "✓ " : "") + name + "\n" + description; check.ForeColor = check.Checked ? Accent : Muted; check.FlatAppearance.BorderColor = check.Checked ? Accent : Color.FromArgb(209, 217, 229); };
            check.ForeColor = Accent; return check;
        }

        private void Confirm(object sender, EventArgs e)
        {
            try
            {
                if (!glb.Checked && !gltf.Checked && !fbx.Checked) throw new ArgumentException("Selectionnez au moins un format.");
                string name = fileName.Text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name == "." || name == "..")
                    throw new ArgumentException("Indiquez un nom de fichier valide, sans extension.");
                if (string.IsNullOrWhiteSpace(directory.Text)) throw new ArgumentException("Choisissez un dossier de sortie.");
                Options = new ExportOptions { FileName = name, OutputDirectory = Path.GetFullPath(directory.Text.Trim()), Scale = (float)scale.Value,
                    Simplification = automatic.Checked ? SimplificationMode.Automatic : adjustable.Checked ? SimplificationMode.Adjustable : SimplificationMode.None, KeepTrianglePercent = keep.Value };
                if (glb.Checked) Options.Formats.Add("glb"); if (gltf.Checked) Options.Formats.Add("gltf"); if (fbx.Checked) Options.Formats.Add("fbx");
                bool exists = Options.Formats.Any(f => File.Exists(Path.Combine(Options.OutputDirectory, name + "." + f))) ||
                    (gltf.Checked && File.Exists(Path.Combine(Options.OutputDirectory, name + ".bin")));
                if (exists && MessageBox.Show(this, "Des fichiers existent deja. Les remplacer ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
