using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZHLauncher
{
    class SettingsForm : Form
    {
        readonly Engine engine;
        TextBox zh, gen, extra;
        ComboBox mouse;
        CheckBox addons;
        Label zhState, genState, baseState;
        Button copy;
        public bool CopyRequested;
        public string MoveTarget;
        public bool MoveUseExisting;
        Label dataPath;

        public SettingsForm(Engine engine)
        {
            this.engine = engine;
            Text = "Settings";
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            // the dialog takes the size of what is in it, so nothing overlaps at 125 % or 150 % scaling
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var t = new TableLayoutPanel
            {
                ColumnCount = 3, Padding = Theme.P(20, 16, 20, 16), BackColor = Theme.Panel,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(0, 0),
                MinimumSize = Theme.Sz(760, 0),
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(150)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int row = 0;
            Action<Control, int, int> add = (c, col, span) => { t.Controls.Add(c, col, row); if (span > 1) t.SetColumnSpan(c, span); };

            var intro = Theme.Label("The launcher plays from its own copy of Zero Hour, so your Steam or EA install and the official Reforged launcher are left alone. " +
                                    "The copy has to be made once on every PC.", 9.75f, FontStyle.Regular, Theme.Muted);
            intro.MaximumSize = Theme.Sz(710, 0);
            intro.Margin = Theme.P(0, 0, 0, 14);
            add(intro, 0, 3); row++;

            add(Lbl("Zero Hour folder"), 0, 1);
            zh = Theme.TextBox(); zh.Anchor = AnchorStyles.Left | AnchorStyles.Right; zh.Text = engine.Settings.ZeroHourPath ?? "";
            zh.TextChanged += (s, e) => Check();
            add(zh, 1, 1);
            var zhButtons = Row(Theme.Button("Browse..."), Theme.Button("Find"));
            zhButtons.Controls[0].Click += (s, e) => Browse(zh, "The Zero Hour folder: the one with INIZH.big in it");
            zhButtons.Controls[1].Click += (s, e) => Find();
            add(zhButtons, 2, 1); row++;
            zhState = Theme.Label("", 9f); add(zhState, 1, 2); row++;

            add(Lbl("Generals folder"), 0, 1);
            gen = Theme.TextBox(); gen.Anchor = AnchorStyles.Left | AnchorStyles.Right; gen.Text = engine.Settings.GeneralsPath ?? "";
            gen.TextChanged += (s, e) => Check();
            add(gen, 1, 1);
            var genButtons = Row(Theme.Button("Browse..."));
            genButtons.Controls[0].Click += (s, e) => Browse(gen, "The original Generals folder: the one with Textures.big in it");
            add(genButtons, 2, 1); row++;
            genState = Theme.Label("", 9f); add(genState, 1, 2); row++;

            add(Lbl("Extra arguments"), 0, 1);
            extra = Theme.TextBox(); extra.Anchor = AnchorStyles.Left | AnchorStyles.Right; extra.Text = engine.Settings.ExtraArgs ?? "";
            add(extra, 1, 2); row++;
            var extraHint = Theme.Label("Just for this PC, e.g. -win for a window. The pack can add its own for everyone.", 9f, FontStyle.Regular, Theme.Muted);
            add(extraHint, 1, 2); row++;

            add(Lbl("Mouse"), 0, 1);
            mouse = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Width = Theme.S(260), Margin = Theme.P(3, 3, 3, 12) };
            mouse.Items.AddRange(new object[] { "Classic (the game's default)", "Reforged" });
            mouse.SelectedIndex = string.Equals(engine.Settings.Mouse, "reforged", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            add(mouse, 1, 2); row++;

            add(Lbl("Add-ons"), 0, 1);
            var packAddons = engine.PackAddons();
            addons = new CheckBox
            {
                Text = packAddons.Count == 0 ? "Use the pack's add-ons (the pack has none)" : "Use the pack's add-ons: " + string.Join(", ", packAddons),
                Checked = !engine.Settings.NoAddons, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Panel,
                Font = Theme.Ui(9.75f), Margin = Theme.P(3, 4, 3, 2),
            };
            add(addons, 1, 2); row++;
            var addonsHint = Theme.Label("HD textures and the like. Off saves disk and video memory; it does not change who you can play with.", 9f, FontStyle.Regular, Theme.Muted);
            addonsHint.Margin = Theme.P(3, 0, 3, 12);
            add(addonsHint, 1, 2); row++;

            add(Lbl("Game files"), 0, 1);
            var dataBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
            dataPath = Theme.Label(engine.DataRoot, 9.75f);
            dataPath.MaximumSize = Theme.Sz(440, 0);
            dataPath.Margin = Theme.P(3, 6, 3, 2);
            baseState = Theme.Label("", 9f);
            baseState.MaximumSize = Theme.Sz(440, 0);
            baseState.Margin = Theme.P(3, 0, 3, 4);
            var dataHint = Theme.Label("Where the launcher keeps its copy of Zero Hour, the game, the mods and the add-ons.", 9f, FontStyle.Regular, Theme.Muted);
            dataHint.MaximumSize = Theme.Sz(440, 0);
            dataHint.Margin = Theme.P(3, 0, 3, 8);
            dataBox.Controls.Add(dataPath);
            dataBox.Controls.Add(baseState);
            dataBox.Controls.Add(dataHint);
            add(dataBox, 1, 1);
            var dataButtons = Row(Theme.Button("Open"), Theme.Button("Change..."));
            dataButtons.Controls[0].Click += (s, e) => { try { Process.Start(new ProcessStartInfo(engine.DataRoot) { UseShellExecute = true }); } catch { } };
            dataButtons.Controls[1].Click += (s, e) => ChangeDataFolder();
            add(dataButtons, 2, 1); row++;

            var spacer = new Panel { Height = Theme.S(10), Width = Theme.S(10), BackColor = Theme.Panel, Margin = new Padding(0) };
            add(spacer, 0, 3); row++;

            copy = Theme.Button("Copy Zero Hour into the launcher", true);
            copy.Font = Theme.Ui(10.5f, FontStyle.Bold);
            copy.AutoSize = true;
            copy.Padding = Theme.P(14, 8, 14, 8);
            copy.Click += (s, e) => { if (Save(true)) { CopyRequested = true; DialogResult = DialogResult.OK; } };
            var save = Theme.Button("Save");
            save.Click += (s, e) => { if (Save(false)) DialogResult = DialogResult.OK; };
            var cancel = Theme.Button("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            var bottom = Row(copy, save, cancel);
            bottom.Anchor = AnchorStyles.Right;
            add(bottom, 0, 3); row++;

            t.RowCount = row;
            for (int i = 0; i < row; i++)
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(t);
            if (string.IsNullOrWhiteSpace(zh.Text)) Find(); else Check();
        }

        static Label Lbl(string text)
        {
            var l = Theme.Label(text, 9.75f, FontStyle.Bold);
            l.Margin = Theme.P(0, 6, 8, 0);
            return l;
        }

        static FlowLayoutPanel Row(params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Panel, Margin = Theme.P(8, 0, 0, 0) };
            foreach (var c in cs) f.Controls.Add(c);
            return f;
        }

        void Browse(TextBox box, string what)
        {
            using (var d = new FolderBrowserDialog { Description = what, ShowNewFolderButton = false })
            {
                if (Directory.Exists(box.Text)) d.SelectedPath = box.Text;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            }
        }

        void ChangeDataFolder()
        {
            string picked;
            using (var d = new FolderBrowserDialog
            {
                Description = "Where should the launcher keep Zero Hour, the game, the mods and the add-ons? Pick an empty folder or make a new one.",
                ShowNewFolderButton = true,
            })
            {
                if (Directory.Exists(engine.DataRoot)) d.SelectedPath = engine.DataRoot;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                picked = d.SelectedPath;
            }
            string message;
            var kind = engine.CheckMoveTarget(picked, out message);
            switch (kind)
            {
                case Engine.MoveKind.Same:
                case Engine.MoveKind.Refused:
                    MessageBox.Show(this, message, "Game files", MessageBoxButtons.OK, kind == Engine.MoveKind.Same ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    return;
                case Engine.MoveKind.UseExisting:
                    if (MessageBox.Show(this, message, "Game files", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    break;
                default:
                    if (MessageBox.Show(this, "Move the launcher's files to\n" + picked + "?\n\n" + message + "\n\nThe launcher starts again when it is done.",
                                        "Game files", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    break;
            }
            if (!Save(false)) return;
            MoveTarget = picked;
            MoveUseExisting = kind == Engine.MoveKind.UseExisting;
            DialogResult = DialogResult.OK;
        }

        void Find()
        {
            var found = Engine.FindZeroHour();
            if (found != null) zh.Text = found;
            if (string.IsNullOrWhiteSpace(gen.Text) || !Engine.HoldsGenerals(gen.Text))
            {
                var g = Engine.FindGenerals(zh.Text);
                if (g != null) gen.Text = g;
            }
            Check();
            if (found == null) zhState.Text = "Not found by itself. Press Browse and pick the folder with INIZH.big in it.";
        }

        void Check()
        {
            var z = zh.Text.Trim();
            if (!Engine.HoldsZeroHour(z)) { State(zhState, "No INIZH.big in this folder.", Theme.Warn); }
            else if (Engine.HasReforgedInside(z))
                State(zhState, "Reforged from the official launcher is installed in this folder. Press Uninstall there (or Verify files in Steam) " +
                               "before copying, then you can install it again.", Theme.Warn);
            else State(zhState, "Zero Hour found, original files.", Theme.Good);

            var g = gen.Text.Trim();
            if (string.IsNullOrEmpty(g) && Engine.HoldsZeroHour(z)) { var f = Engine.FindGenerals(z); if (f != null) { gen.Text = f; return; } }
            if (Engine.HoldsGenerals(g) || File.Exists(Path.Combine(g, "Textures.big"))) State(genState, "Generals found.", Theme.Good);
            else State(genState, "Zero Hour needs the original Generals too (a folder with Textures.big).", Theme.Warn);

            if (engine.BaseReady)
                State(baseState, "Zero Hour is copied" + (engine.State.BaseFrom != null ? " from " + engine.State.BaseFrom : "") +
                                 ". Copy again only if you changed the game." + (engine.GameReady ? " Reforged " + engine.Pack.Version + " is installed." : ""), Theme.Good);
            else State(baseState, "Nothing copied yet.", Theme.Muted);
        }

        static void State(Label l, string text, Color c)
        {
            l.Text = text; l.ForeColor = c; l.MaximumSize = Theme.Sz(440, 0);
        }

        bool Save(bool forCopy)
        {
            var z = zh.Text.Trim(); var g = gen.Text.Trim();
            if (forCopy)
            {
                if (!Engine.HoldsZeroHour(z)) { MessageBox.Show(this, "Pick the Zero Hour folder first: the one with INIZH.big in it.", "Settings"); return false; }
                if (!File.Exists(Path.Combine(g, "Textures.big"))) { MessageBox.Show(this, "Pick the original Generals folder too: the one with Textures.big in it.", "Settings"); return false; }
                if (Engine.HasReforgedInside(z) &&
                    MessageBox.Show(this, "This Zero Hour folder has Reforged from the official launcher in it. Its newer files would end up under your v3.6.0 " +
                                          "and could differ from your friends' copies.\n\nUninstall it in the official launcher first (recommended).\n\nCopy anyway?",
                                    "Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return false;
            }
            engine.Settings.ZeroHourPath = z;
            engine.Settings.GeneralsPath = g;
            engine.Settings.ExtraArgs = extra.Text.Trim();
            engine.Settings.Mouse = mouse.SelectedIndex == 1 ? "reforged" : "classic";
            engine.Settings.NoAddons = !addons.Checked;
            try { engine.SaveSettings(); } catch (Exception ex) { MessageBox.Show(this, ex.Message); return false; }
            return true;
        }
    }
}
