using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SaabDidTool
{
    internal static class RoundedUi
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void RoundOnResize(Control control, int radius)
        {
            void Apply(object sender, EventArgs e)
            {
                if (control.Width < 2 || control.Height < 2) return;
                using var path = RoundRect(new Rectangle(0, 0, control.Width, control.Height), radius);
                var old = control.Region;
                control.Region = new Region(path);
                old?.Dispose();
            }
            control.SizeChanged += Apply;
            Apply(control, EventArgs.Empty);
        }
    }

    // Keeps the CheckBox.Checked API so existing read/write logic stays unchanged.
    // Checked (green) means the diagnostic bulb check is DISABLED.
    internal sealed class RoundedToggleCheckBox : CheckBox
    {
        public RoundedToggleCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
            CheckAlign = ContentAlignment.MiddleRight;
            TextAlign = ContentAlignment.MiddleLeft;
            CheckedChanged += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var switchRect = new Rectangle(Width - 74, (Height - 26) / 2, 70, 26);
            Color on = Color.FromArgb(38, 178, 105);
            Color off = Color.FromArgb(205, 68, 75);
            using (var track = RoundedUi.RoundRect(switchRect, 13))
            using (var fill = new SolidBrush(Checked ? on : off))
                g.FillPath(fill, track);
            int knobX = Checked ? switchRect.Right - 23 : switchRect.Left + 3;
            using (var knob = new SolidBrush(Color.White))
                g.FillEllipse(knob, knobX, switchRect.Top + 3, 20, 20);
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(0, 0, Math.Max(0, Width - 82), Height),
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 1, Width - 1, Height - 2));
        }
    }
}
