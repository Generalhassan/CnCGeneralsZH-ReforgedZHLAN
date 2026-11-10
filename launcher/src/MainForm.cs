using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZHLauncher
{
    class MainForm : Form
    {
        const string CustomName = "\u0001custom";
        readonly Engine engine;
        readonly Dictionary<string, string> codes = new Dictionary<string, string>();
        bool busy, filling;

        Label title, subtitle, presetName, presetDesc, orderHeader, codeLabel, codeHint, status;
        ListBox orderView;
        PresetList presets;
        CheckedListBox customList;
        Panel customButtons;
        Button play, check, settingsButton, savePreset, up, down;
        ProgressBar bar;
        DateTime lastReport = DateTime.MinValue;

        public MainForm(Engine engine)
        {
            this.engine = engine;
            Text = "Zero Hour LAN Launcher";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.75f);
            StartPosition = FormStartPosition.CenterScreen;
                        AutoScaleMode = AutoScaleMode.None;
            ClientSize = Theme.Sz(980, 640);
            MinimumSize = Theme.Sz(820, 560);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SuspendLayout();
            BuildLayout();
            ResumeLayout(false);

            engine.Report = OnProgress;
            Shown += (s, e) => FirstStart();
            Refill();
        }

        // ------------------------------------------------------------------ layout

        void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(78), BackColor = Theme.Panel, Padding = Theme.P(22, 12, 22, 10) };
            title = Theme.Label("ZERO HOUR REFORGED", 18f, FontStyle.Bold, Theme.Gold);
            title.Location = Theme.Pt(20, 10);
            subtitle = Theme.Label("", 10f, FontStyle.Regular, Theme.Muted);
            subtitle.Location = Theme.Pt(23, 46);
            settingsButton = Theme.Button("Settings");
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Click += (s, e) => OpenSettings();
            var packButton = Theme.Button("Pack folder");
            packButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            packButton.Click += (s, e) => OpenFolder(engine.PackDir);
            var headerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = Theme.P(0, 10, 0, 0),
            };
            headerButtons.Controls.Add(packButton);
            headerButtons.Controls.Add(settingsButton);
            header.Controls.Add(headerButtons);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(46), BackColor = Theme.Panel, Padding = Theme.P(22, 8, 22, 8) };
            status = Theme.Label("", 9.75f, FontStyle.Regular, Theme.Muted);
            status.AutoSize = false;
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            bar = new ProgressBar { Dock = DockStyle.Right, Width = Theme.S(260), Style = ProgressBarStyle.Continuous, Visible = false, Maximum = 1000 };
            footer.Controls.Add(status);
            footer.Controls.Add(bar);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Theme.P(16, 16, 16, 16), BackColor = Theme.Back };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(330)));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // left: the presets
            var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = Theme.P(12, 12, 12, 12), Margin = Theme.P(0, 0, 12, 0) };
            var presetsHeader = Theme.Label("PRESETS", 9f, FontStyle.Bold, Theme.Muted);
            presetsHeader.Dock = DockStyle.Top;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Remove this preset from the pack", null, (s, e) => RemovePreset());
            presets = new PresetList { Dock = DockStyle.Fill, CardMenu = menu, Title = PresetTitle, Subtitle = PresetLine };
            presets.SelectionChanged += (s, e) => ShowSelected();
            presets.ItemActivated += (s, e) => { if (!busy) Run(true); };
            menu.Opening += (s, e) => { var p = presets.SelectedItem as PresetDef; e.Cancel = p == null || p.Name == CustomName || !engine.Pack.Presets.Contains(p); };
            var leftGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Theme.P(0, 0, 0, 0), Padding = Theme.P(0, 0, 0, 0), BackColor = Theme.Panel };
            leftGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            presetsHeader.Dock = DockStyle.None;
            presetsHeader.Margin = Theme.P(0, 0, 0, 6);
            leftGrid.Controls.Add(presetsHeader, 0, 0);
            leftGrid.Controls.Add(presets, 0, 1);
            left.Controls.Add(leftGrid);

            // right: what the selected preset loads, and Play
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = Theme.P(22, 18, 22, 18), ColumnCount = 1, Margin = Theme.P(0, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // name
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // description
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // LOAD ORDER
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // the mods
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // LAN code
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons

            presetName = Theme.Label("", 17f, FontStyle.Bold);
            presetDesc = Theme.Label("", 10f, FontStyle.Regular, Theme.Muted);
            presetDesc.MaximumSize = Theme.Sz(560, 0);
            orderHeader = Theme.Label("LOAD ORDER  (a later mod wins where two change the same file)", 9f, FontStyle.Bold, Theme.Muted);
            orderHeader.Margin = Theme.P(0, 14, 0, 6);

            var listHost = new Panel { Dock = DockStyle.Fill, Margin = Theme.P(0, 0, 0, 0), BackColor = Theme.Panel };
            orderView = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel2, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(34), SelectionMode = SelectionMode.None, IntegralHeight = false,
            };
            orderView.DrawItem += DrawOrderItem;
            customList = new CheckedListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel2, ForeColor = Theme.Text,
                CheckOnClick = true, IntegralHeight = false, Font = Theme.Ui(10.5f), Visible = false,
            };
            customList.ItemCheck += (s, e) => { if (!filling && IsHandleCreated) BeginInvoke(new Action(CustomChanged)); };
            customButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Theme.S(110), FlowDirection = FlowDirection.TopDown, Padding = Theme.P(10, 0, 0, 0), Visible = false, BackColor = Theme.Panel };
            up = Theme.Button("Move up"); up.Click += (s, e) => MoveCustom(-1);
            down = Theme.Button("Move down"); down.Click += (s, e) => MoveCustom(1);
            up.Margin = down.Margin = Theme.P(0, 0, 0, 8);
            customButtons.Controls.Add(up);
            customButtons.Controls.Add(down);
            listHost.Controls.Add(orderView);
            listHost.Controls.Add(customList);
            listHost.Controls.Add(customButtons);

            var codeRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = Theme.P(0, 14, 0, 10), BackColor = Theme.Panel };
            codeLabel = Theme.Label("", 13f, FontStyle.Bold);
            codeHint = Theme.Label("Everyone in the room should see the same code before you host.", 9f, FontStyle.Regular, Theme.Muted);
            codeRow.Controls.Add(codeLabel);
            codeRow.Controls.Add(codeHint);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Panel };
            play = Theme.Button("PLAY", true);
            play.Size = new Size(Theme.S(200), Theme.S(52));
            play.Margin = Theme.P(0, 0, 12, 0);
            play.Click += (s, e) => Run(true);
            check = Theme.Button("Check files");
            check.Margin = Theme.P(0, 12, 8, 0);
            check.Click += (s, e) => Run(false);
            savePreset = Theme.Button("Save as preset");
            savePreset.Margin = Theme.P(0, 12, 8, 0);
            savePreset.Click += (s, e) => SaveCustomAsPreset();
            buttons.Controls.Add(play);
            buttons.Controls.Add(check);
            buttons.Controls.Add(savePreset);

            right.Controls.Add(presetName, 0, 0);
            right.Controls.Add(presetDesc, 0, 1);
            right.Controls.Add(orderHeader, 0, 2);
            right.Controls.Add(listHost, 0, 3);
            right.Controls.Add(codeRow, 0, 4);
            right.Controls.Add(buttons, 0, 5);

            body.Controls.Add(left, 0, 0);
            body.Controls.Add(right, 1, 0);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }


        // ------------------------------------------------------------------ filling it

        void Refill()
        {
            var keep = presets.SelectedItem != null ? presets.SelectedItem.Name : engine.Settings.LastPreset;
            var addonCount = engine.PackAddons().Count;
            subtitle.Text = "Reforged " + engine.Pack.Version + "   ·   " + engine.Pack.Name + "   ·   " + engine.Mods.Count + " mods in the pack" +
                            (addonCount == 0 ? "" : "   ·   " + (engine.Settings.NoAddons ? "add-ons off" : addonCount == 1 ? "1 add-on" : addonCount + " add-ons"));
            presets.Items.Clear();
            presets.Items.AddRange(engine.Pack.Presets);
            if (!engine.Pack.Presets.Any(p => p.Mods.Count == 0))
                presets.Items.Add(new PresetDef { Name = "No mods", Description = "Zero Hour with Reforged " + engine.Pack.Version + " and nothing else." });
            presets.Items.Add(new PresetDef { Name = CustomName, Description = "Tick the mods you want and put them in order." });
            presets.Rebuild();
            presets.SelectedItem = presets.Items.FirstOrDefault(p => p.Name == keep) ?? presets.Items[0];

            filling = true;
            customList.Items.Clear();
            var order = engine.Settings.CustomMods.Select(n => engine.FindMod(n)).Where(m => m != null).ToList();
            foreach (var m in order) customList.Items.Add(m, true);
            foreach (var m in engine.Mods.Where(m => !order.Contains(m))) customList.Items.Add(m, false);
            filling = false;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (busy) return;
            if (!engine.BaseReady) SetStatus("First start: open Settings and copy your Zero Hour into the launcher.", Theme.Warn);
            else if (!File.Exists(engine.PackPath(engine.Pack.Build ?? ""))) SetStatus("The pack has no Reforged build zip in its build folder.", Theme.Warn);
            else if (!engine.GameReady) SetStatus("Ready. The first Play installs Reforged " + engine.Pack.Version + " on this PC.", Theme.Muted);
            else SetStatus("Ready.", Theme.Good);
        }

        void SetStatus(string text, Color color) { status.Text = text; status.ForeColor = color; }

        PresetDef Selected { get { return presets.SelectedItem; } }
        bool IsCustom { get { return Selected != null && Selected.Name == CustomName; } }

        List<string> SelectedModNames()
        {
            if (Selected == null) return new List<string>();
            if (IsCustom) return customList.CheckedItems.Cast<ModInfo>().Select(m => m.Name).ToList();
            return Selected.Mods;
        }

        string CodeKey(List<string> names) { return string.Join("|", names.Select(n => n.ToLowerInvariant())); }

        void ShowSelected()
        {
            var p = Selected;
            if (p == null) return;
            presetName.Text = IsCustom ? "Custom" : p.Name;
            presetDesc.Text = p.Description ?? "";
            presetDesc.Visible = !string.IsNullOrEmpty(presetDesc.Text);
            orderView.Visible = !IsCustom;
            customList.Visible = customButtons.Visible = IsCustom;
            savePreset.Visible = IsCustom;
            orderHeader.Text = IsCustom ? "TICK MODS, THEN ORDER THEM  (a later mod wins where two change the same file)"
                                        : "LOAD ORDER  (a later mod wins where two change the same file)";
            orderView.Items.Clear();
            if (!IsCustom)
            {
                if (p.Mods.Count == 0) orderView.Items.Add("No mods: plain Zero Hour with Reforged's changes.");
                foreach (var n in p.Mods) orderView.Items.Add(n);
            }
            ShowCode();
        }

        void ShowCode()
        {
            string code;
            if (codes.TryGetValue(CodeKey(SelectedModNames()), out code))
            {
                codeLabel.Text = "LAN code   " + code;
                codeLabel.ForeColor = Theme.Gold;
            }
            else
            {
                codeLabel.Text = "LAN code   ----";
                codeLabel.ForeColor = Theme.Muted;
            }
        }

        void CustomChanged()
        {
            engine.Settings.CustomMods = customList.CheckedItems.Cast<ModInfo>().Select(m => m.Name).ToList();
            try { engine.SaveSettings(); } catch { }
            presets.Repaint();
            ShowCode();
        }

        void MoveCustom(int dir)
        {
            int i = customList.SelectedIndex, j = i + dir;
            if (i < 0 || j < 0 || j >= customList.Items.Count) return;
            var item = customList.Items[i];
            bool on = customList.GetItemChecked(i);
            customList.Items.RemoveAt(i);
            customList.Items.Insert(j, item);
            customList.SetItemChecked(j, on);
            customList.SelectedIndex = j;
            CustomChanged();
        }

        // ------------------------------------------------------------------ drawing

        string PresetTitle(PresetDef p) { return p.Name == CustomName ? "Custom..." : p.Name; }

        string PresetLine(PresetDef p)
        {
            if (p.Name == CustomName)
                return engine.Settings.CustomMods.Count == 0 ? "pick mods yourself" : string.Join(" + ", engine.Settings.CustomMods.Select(Label));
            return p.Mods.Count == 0 ? "no mods" : string.Join(" + ", p.Mods.Select(Label));
        }

        string Label(string modName)
        {
            var m = engine.FindMod(modName);
            return m == null ? modName + " (missing!)" : m.Label;
        }

        void DrawOrderItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Panel2)) g.FillRectangle(bg, e.Bounds);
            var text = (string)orderView.Items[e.Index];
            var p = Selected;
            var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            using (var f = Theme.Ui(10.5f))
            using (var fb = Theme.Ui(10.5f, FontStyle.Bold))
            {
                if (p == null || p.Mods.Count == 0)
                {
                    TextRenderer.DrawText(g, text, f, new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, e.Bounds.Width - Theme.S(24), e.Bounds.Height), Theme.Muted, tf);
                    return;
                }
                var m = engine.FindMod(text);
                TextRenderer.DrawText(g, (e.Index + 1) + ".", fb, new Rectangle(e.Bounds.X + Theme.S(12), e.Bounds.Y, Theme.S(30), e.Bounds.Height), Theme.Gold, tf);
                var label = m == null ? text + "   - not in the pack's mods folder" : m.Label;
                var kind = m == null ? "" : (m.IsPatch ? "patch  -modpatch" : "mod  -mod");
                TextRenderer.DrawText(g, label, f, new Rectangle(e.Bounds.X + Theme.S(44), e.Bounds.Y, e.Bounds.Width - Theme.S(200), e.Bounds.Height), m == null ? Theme.Warn : Theme.Text, tf);
                TextRenderer.DrawText(g, kind, Theme.Ui(9f), new Rectangle(e.Bounds.Right - Theme.S(150), e.Bounds.Y, Theme.S(140), e.Bounds.Height), Theme.Muted, tf | TextFormatFlags.Right);
            }
        }

        // ------------------------------------------------------------------ work

        void OnProgress(Progress p)
        {
            var now = DateTime.UtcNow;
            if (p.Fraction >= 0 && p.Fraction < 1 && (now - lastReport).TotalMilliseconds < 120) return;
            lastReport = now;
            BeginInvoke(new Action(() =>
            {
                SetStatus(p.Text, Theme.Text);
                bar.Visible = p.Fraction >= 0;
                if (p.Fraction >= 0) bar.Value = Math.Max(0, Math.Min(1000, (int)(p.Fraction * 1000)));
            }));
        }

        void SetBusy(bool on)
        {
            busy = on;
            play.Enabled = check.Enabled = settingsButton.Enabled = savePreset.Enabled = presets.Enabled = customList.Enabled = up.Enabled = down.Enabled = !on;
            bar.Maximum = 1000;
            if (!on) bar.Visible = false;
            UseWaitCursor = on;
        }

        async void Run(bool start)
        {
            if (busy || Selected == null) return;
            List<string> missing;
            var names = SelectedModNames();
            var mods = engine.Resolve(names, out missing);
            if (missing.Count > 0)
            {
                MessageBox.Show(this, "These mods are not in the pack's mods folder:\n\n" + string.Join("\n", missing), "Missing mods", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!engine.BaseReady) { OpenSettings(); return; }

            engine.Settings.LastPreset = Selected.Name;
            try { engine.SaveSettings(); } catch { }
            SetBusy(true);
            try
            {
                var code = await Task.Run(() => engine.Prepare(mods));
                codes[CodeKey(names)] = code;
                ShowCode();
                presets.Repaint();
                if (start)
                {
                    engine.Start(mods);
                    SetStatus("Game started  -  LAN code " + code + ".", Theme.Good);
                }
                else SetStatus("All files in place  -  LAN code " + code + ".", Theme.Good);
            }
            catch (Exception ex)
            {
                engine.Log("error: " + ex);
                SetStatus(ex.Message, Theme.Warn);
                MessageBox.Show(this, ex.Message, "Zero Hour LAN Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); }
        }

        async void RunSetup(string zh, string generals)
        {
            SetBusy(true);
            try
            {
                await Task.Run(() => engine.SetupBase(zh, generals));
                SetStatus("Zero Hour copied. Press Play.", Theme.Good);
            }
            catch (Exception ex)
            {
                engine.Log("setup error: " + ex);
                SetStatus(ex.Message, Theme.Warn);
                MessageBox.Show(this, ex.Message, "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); UpdateStatus(); }
        }

        void FirstStart()
        {
            if (!engine.BaseReady) OpenSettings();
        }

        void OpenSettings()
        {
            using (var f = new SettingsForm(engine))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.CopyRequested)
                    RunSetup(engine.Settings.ZeroHourPath, engine.Settings.GeneralsPath);
                else Refill();
            }
        }

        void SaveCustomAsPreset()
        {
            var names = SelectedModNames();
            if (names.Count == 0) { MessageBox.Show(this, "Tick at least one mod first.", "Save as preset"); return; }
            var name = Prompt.Ask(this, "Save as preset", "Name for this preset (everyone with the pack will see it):",
                                  string.Join(" + ", names.Select(Label)));
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = engine.Pack.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Mods = names.ToList();
            else engine.Pack.Presets.Add(new PresetDef { Name = name.Trim(), Mods = names.ToList() });
            try
            {
                engine.SavePack();
                engine.Settings.LastPreset = name.Trim();
                Refill();
            }
            catch (Exception ex)
            {
                engine.ReloadPack();
                MessageBox.Show(this, "Could not write pack.json in the pack folder:\n" + ex.Message, "Save as preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void RemovePreset()
        {
            var p = Selected;
            if (p == null || !engine.Pack.Presets.Contains(p)) return;
            if (MessageBox.Show(this, "Remove \"" + p.Name + "\" from pack.json?", "Remove preset", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            engine.Pack.Presets.Remove(p);
            try { engine.SavePack(); }
            catch (Exception ex) { MessageBox.Show(this, "Could not write pack.json:\n" + ex.Message); engine.ReloadPack(); }
            Refill();
        }

        static void OpenFolder(string dir)
        {
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { }
        }
    }

    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string question, string initial)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.BackColor = Theme.Panel;
                f.ForeColor = Theme.Text;
                f.Font = Theme.Ui(9.75f);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false;
                f.AutoScaleMode = AutoScaleMode.None;
                f.ClientSize = Theme.Sz(440, 140);
                var q = Theme.Label(question);
                q.Location = Theme.Pt(16, 16);
                var box = Theme.TextBox();
                box.Text = initial;
                box.SetBounds(Theme.S(16), Theme.S(44), Theme.S(408), Theme.S(26));
                var ok = Theme.Button("Save", false);
                ok.DialogResult = DialogResult.OK;
                ok.Location = Theme.Pt(270, 92);
                var cancel = Theme.Button("Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = Theme.Pt(350, 92);
                f.Controls.AddRange(new Control[] { q, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
