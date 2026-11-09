using System.Drawing;
using System.Windows.Forms;

namespace ZHLauncher
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(14, 18, 27);
        public static readonly Color Panel = Color.FromArgb(22, 28, 41);
        public static readonly Color Panel2 = Color.FromArgb(31, 39, 56);
        public static readonly Color Line = Color.FromArgb(44, 54, 76);
        public static readonly Color Text = Color.FromArgb(230, 233, 239);
        public static readonly Color Muted = Color.FromArgb(139, 147, 167);
        public static readonly Color Gold = Color.FromArgb(212, 165, 38);
        public static readonly Color GoldHot = Color.FromArgb(234, 188, 60);
        public static readonly Color Good = Color.FromArgb(92, 184, 122);
        public static readonly Color Warn = Color.FromArgb(232, 150, 64);

        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static Button Button(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Gold : Panel2,
                ForeColor = primary ? Color.FromArgb(17, 17, 17) : Text,
                Font = primary ? Ui(13f, FontStyle.Bold) : Ui(9.75f),
                AutoSize = !primary,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = primary ? GoldHot : Line;
            b.FlatAppearance.MouseDownBackColor = primary ? Gold : Panel;
            return b;
        }

        public static Label Label(string text, float size = 9.75f, FontStyle style = FontStyle.Regular, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Ui(size, style),
                ForeColor = color ?? Text,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4),
            };
        }

        public static TextBox TextBox()
        {
            return new TextBox { BackColor = Panel2, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = Ui(9.75f) };
        }
    }
}
