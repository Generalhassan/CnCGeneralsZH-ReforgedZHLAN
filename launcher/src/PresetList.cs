using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZHLauncher
{
    // The presets down the left side: one card each, click to pick, double-click to play.
    class PresetList : FlowLayoutPanel
    {
        public readonly List<PresetDef> Items = new List<PresetDef>();
        public Func<PresetDef, string> Title = p => p.Name;
        public Func<PresetDef, string> Subtitle = p => "";
        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;
        public ContextMenuStrip CardMenu;
        PresetDef selected;

        public PresetList()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
            BackColor = Theme.Panel;
            Padding = Theme.P(0, 0, 0, 0);
            Margin = Theme.P(0, 0, 0, 0);
            Resize += (s, e) => FitWidths();
        }

        public PresetDef SelectedItem
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                foreach (Control c in Controls) c.Invalidate();
                var card = Controls.Cast<Card>().FirstOrDefault(c => c.Preset == value);
                if (card != null) ScrollControlIntoView(card);
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public void Rebuild()
        {
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            foreach (var p in Items) Controls.Add(new Card(this, p));
            FitWidths();
            ResumeLayout(true);
        }

        public void Repaint() { foreach (Control c in Controls) c.Invalidate(); }

        void FitWidths()
        {
            int w = ClientSize.Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - Theme.S(2);
            foreach (Control c in Controls) c.Width = Math.Max(100, w);
        }

        class Card : Control
        {
            readonly PresetList owner;
            public readonly PresetDef Preset;
            bool hot;

            public Card(PresetList owner, PresetDef p)
            {
                this.owner = owner;
                Preset = p;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Height = Theme.S(58);
                Margin = Theme.P(0, 0, 0, 6);
                Cursor = Cursors.Hand;
                ContextMenuStrip = owner.CardMenu;
                MouseEnter += (s, e) => { hot = true; Invalidate(); };
                MouseLeave += (s, e) => { hot = false; Invalidate(); };
                MouseDown += (s, e) => { if (owner.Enabled) owner.SelectedItem = Preset; };
                DoubleClick += (s, e) => { if (owner.Enabled && owner.ItemActivated != null) owner.ItemActivated(owner, EventArgs.Empty); };
            }


            protected override void OnPaint(PaintEventArgs e)
            {
                bool sel = owner.SelectedItem == Preset;
                var g = e.Graphics;
                var r = ClientRectangle;
                using (var bg = new SolidBrush(sel ? Theme.Line : hot ? Color.FromArgb(37, 46, 66) : Theme.Panel2)) g.FillRectangle(bg, r);
                if (sel) using (var accent = new SolidBrush(Theme.Gold)) g.FillRectangle(accent, 0, 0, Theme.S(4), r.Height);
                float s = Theme.Dpi;
                int pad = (int)(14 * s);
                var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                using (var f1 = Theme.Ui(11f, FontStyle.Bold))
                using (var f2 = Theme.Ui(9f))
                {
                    TextRenderer.DrawText(g, owner.Title(Preset), f1, new Rectangle(pad, (int)(7 * s), r.Width - pad * 2, (int)(22 * s)), Theme.Text, tf);
                    TextRenderer.DrawText(g, owner.Subtitle(Preset), f2, new Rectangle(pad, (int)(30 * s), r.Width - pad * 2, (int)(20 * s)), Theme.Muted, tf);
                }
            }
        }
    }
}
