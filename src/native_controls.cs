using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShutdownTimer {
    internal static class Theme {
        public static readonly Color Ink = Color.FromArgb(29, 53, 86), Blue = Color.FromArgb(40, 107, 232), Muted = Color.FromArgb(82, 104, 136);
        public static readonly Color Light = Color.FromArgb(240, 245, 255), Border = Color.FromArgb(218, 230, 247), Red = Color.FromArgb(194, 56, 62);
        static readonly FontFamily[] InstalledFamilies = FontFamily.Families;
        public static readonly FontFamily TextFamily = ChooseFamily(new [] { "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI", "微软雅黑 UI", "Noto Sans CJK SC", "Noto Sans CJK JP", "Segoe UI" });
        public static readonly FontFamily NumberFamily = TextFamily;
        static FontFamily ChooseFamily(string[] choices) {
            foreach (string wanted in choices)
                foreach (FontFamily family in InstalledFamilies) {
                    // Family.Name uses the system language; match the English and Chinese names as well.
                    if (String.Equals(family.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(family.GetName(0x0409), wanted, StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(family.GetName(0x0804), wanted, StringComparison.OrdinalIgnoreCase)) return family;
                }
            return FontFamily.GenericSansSerif;
        }
        public static GraphicsPath Round(RectangleF r, float radius) {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }
    internal sealed class PixelLabel : Control {
        public ContentAlignment Align = ContentAlignment.MiddleCenter;
        internal TextBox BaselinePeer;
        public PixelLabel() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            flags |= Align == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            Rectangle textBounds = ClientRectangle;
            if (BaselinePeer != null) {
                float peerAscent = BaselinePeer.Font.Size * BaselinePeer.Font.FontFamily.GetCellAscent(BaselinePeer.Font.Style) / BaselinePeer.Font.FontFamily.GetEmHeight(BaselinePeer.Font.Style);
                float ascent = Font.Size * Font.FontFamily.GetCellAscent(Font.Style) / Font.FontFamily.GetEmHeight(Font.Style);
                float baseline = BaselinePeer.Top + (BaselinePeer.ClientSize.Height - BaselinePeer.Font.Height) / 2F + peerAscent;
                textBounds.Y = (int)Math.Round(baseline - ascent - Top);
                textBounds.Height = Font.Height;
                flags &= ~TextFormatFlags.VerticalCenter;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, flags);
        }
    }
    internal sealed class RoundPanel : Panel {
        public Color BorderColor = Theme.Border;
        public float ScaleFactor = 1;
        public RoundPanel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e) {
            using (var background = new SolidBrush(Parent == null ? Color.White : Parent.BackColor)) e.Graphics.FillRectangle(background, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 12 * ScaleFactor))
            using (var b = new SolidBrush(BackColor)) using (var pen = new Pen(BorderColor, Math.Max(1, ScaleFactor))) { e.Graphics.FillPath(b, p); e.Graphics.DrawPath(pen, p); }
        }
    }
    internal sealed class ClockLogo : Control {
        internal ClockLogo() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) {
            float size = Math.Min(Width, Height); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Theme.Round(new RectangleF(0, 0, size, size), size * .27F))
            using (var fill = new SolidBrush(Theme.Blue)) e.Graphics.FillPath(fill, shape);
            using (var pen = new Pen(Color.White, Math.Max(1, size * .06F))) {
                e.Graphics.DrawEllipse(pen, size * .2F, size * .2F, size * .6F, size * .6F);
                e.Graphics.DrawLine(pen, size * .5F, size * .33F, size * .5F, size * .5F);
                e.Graphics.DrawLine(pen, size * .5F, size * .5F, size * .66F, size * .5F);
            }
        }
    }
    internal enum ButtonTone { Preset, Primary, Tray, Cancel }
    internal sealed class RoundButton : Button {
        bool hover, pressed;
        ButtonTone tone;
        bool urgent;
        public float ScaleFactor = 1;
        public Color BorderColor = Theme.Border;
        public ButtonTone Tone {
            get { return tone; }
            set { tone = value; RefreshColors(); }
        }
        public bool Urgent {
            get { return urgent; }
            set { if (urgent != value) { urgent = value; RefreshColors(); } }
        }
        public RoundButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            RefreshColors();
        }
        static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        void Colors(bool over, bool down, out Color fill, out Color text, out Color border) {
            // These are the final conversation version's CSS palettes. Each
            // state chooses background AND text together, rather than darkening
            // every button's normal background while leaving blue text on it.
            switch (tone) {
                case ButtonTone.Primary:
                    fill = Rgb(down ? 0x1b51bc : over ? 0x205fd5 : 0x286be8);
                    text = Color.White; border = Rgb(over || down ? 0x205fd5 : 0x286be8); break;
                case ButtonTone.Tray:
                    fill = Rgb(over || down ? 0xe7f0ff : 0xf4f8ff);
                    text = Rgb(0x2465d6); border = Rgb(over || down ? 0x9dbdee : 0xc8daf8); break;
                case ButtonTone.Cancel:
                    fill = Rgb(down ? 0xffdedb : urgent ? over ? 0xffdedb : 0xffe9e7 : over ? 0xffe9e7 : 0xfff4f3);
                    text = Rgb(down ? 0x9c222a : urgent ? over ? 0x9c222a : 0xb32932 : over ? 0xad2931 : 0xc2383e);
                    border = Rgb(urgent ? over || down ? 0xd1656b : 0xdc7e83 : over || down ? 0xe7a6a7 : 0xf0c7c7); break;
                default:
                    fill = Rgb(down ? 0x205fd5 : over ? 0x286be8 : 0xf0f5ff);
                    text = over || down ? Color.White : Rgb(0x2465d6);
                    border = Rgb(over || down ? 0x286be8 : 0xdce7fa); break;
            }
        }
        void RefreshColors() {
            Color fill, text, border; Colors(false, false, out fill, out text, out border);
            BackColor = fill; ForeColor = text; BorderColor = border; Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaintBackground(PaintEventArgs e) {
            // OnPaint owns the complete surface. The native flat-button hover
            // background must not be layered over the custom text/background.
        }
        protected override void OnPaint(PaintEventArgs e) {
            using (var background = new SolidBrush(Parent == null ? Color.White : Parent.BackColor)) e.Graphics.FillRectangle(background, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, text, border; Colors(hover, pressed, out fill, out text, out border);
            if (!Enabled) { fill = Color.FromArgb(240, 242, 245); text = Theme.Muted; border = Theme.Border; }
            using (var p = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 9 * ScaleFactor))
            using (var b = new SolidBrush(fill)) using (var pen = new Pen(Focused && ShowFocusCues ? Theme.Blue : border, (Focused && ShowFocusCues ? 2 : 1) * ScaleFactor)) { e.Graphics.FillPath(b, p); e.Graphics.DrawPath(pen, p); }
            int inset = Math.Max(2, (int)Math.Round(3 * ScaleFactor));
            Rectangle textBounds = Rectangle.Inflate(ClientRectangle, -inset, -inset);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }
    internal class PixelForm : Form {
        protected float UiScale = 1;
        protected bool LayingOut;
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        protected virtual Size DesignClientSize { get { return new Size(294, 425); } }
        internal float LayoutScale { get { return UiScale; } }
        protected void ApplyDpiScale(float scale) {
            if (Single.IsNaN(scale) || Single.IsInfinity(scale) || scale <= 0) throw new ArgumentOutOfRangeException("scale");
            UiScale = scale;
            // Resolution and the work area affect location only. The complete
            // layout always uses one fixed design and the monitor DPI factor.
            ClientSize = new Size(S(DesignClientSize.Width), S(DesignClientSize.Height));
            LayoutPixels();
        }
        protected void Place(Control c, int x, int y, int w, int h, float pixels, bool bold, bool number = false) {
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            FontFamily family = number ? Theme.NumberFamily : Theme.TextFamily;
            int size = Math.Max(9, S(pixels)); string key = family.Name + ":" + size + ":" + bold;
            Font font;
            if (!fonts.TryGetValue(key, out font)) { font = new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); fonts.Add(key, font); }
            c.Font = font;
            var button = c as RoundButton; if (button != null) button.ScaleFactor = UiScale;
            var panel = c as RoundPanel; if (panel != null) panel.ScaleFactor = UiScale;
        }
        protected int S(float value) { return (int)Math.Round(value * UiScale); }
        protected virtual void LayoutPixels() {}
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x02E0) {
                float dpi = (int)(m.WParam.ToInt64() & 0xffff) / 96F;
                var rect = (Native.Rect)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.Rect));
                ApplyDpiScale(dpi); Location = new Point(rect.Left, rect.Top); m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) foreach (Font font in fonts.Values) font.Dispose(); }
    }
}
