using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>DPI 缩放：所有尺寸都写成 96 DPI 下的"逻辑像素"，由这里乘系数。</summary>
    internal static class Ui
    {
        internal static float S = 1f;

        internal static void Init(int deviceDpi)
        {
            S = Math.Max(0.75f, Math.Min(3f, deviceDpi / 96f));
        }

        internal static int Px(double logical) => (int)Math.Round(logical * S);

        internal static Size Sz(double w, double h) => new Size(Px(w), Px(h));

        internal static Font Font(double pt, FontStyle style = FontStyle.Regular) => Theme.Font((float)pt, style);

        /// <summary>
        /// 自绘控件清屏用：父控件背景可能是 Color.Transparent（我们自己的卡片），
        /// 直接 Clear(Transparent) 会得到黑色，这里回退到指定默认色。
        /// </summary>
        internal static Color BackdropOf(Control child, Color fallback)
        {
            var parent = child.Parent;
            if (parent == null) return fallback;
            var c = parent.BackColor;
            if (c == Color.Transparent) return fallback;
            return c;
        }
    }

    /// <summary>圆角卡片：WinUI 的 SettingsCard 外观（1px 描边 + 8px 圆角 + 卡片底色）。</summary>
    internal sealed class FlatCard : Panel
    {
        internal bool DrawBorder = true;

        internal FlatCard()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.BackdropOf(this, Theme.Layer));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Theme.RoundRect(r, Ui.Px(8)))
            {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                if (DrawBorder)
                    using (var pen = new Pen(Theme.Stroke, Math.Max(1f, Ui.Px(1)))) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>WinUI 风格按钮：Accent（填充强调色）/ Standard（卡片底 + 描边）/ Subtle（无底）。</summary>
    internal sealed class FlatButton : Button
    {
        internal enum Kind { Accent, Standard, Subtle }

        internal Kind Style = Kind.Standard;
        private bool _hover, _down;

        internal FlatButton(string text, Kind kind = Kind.Standard)
        {
            Text = text;
            Style = kind;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Font = Ui.Font(10);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color fill, border, text;
            switch (Style)
            {
                case Kind.Accent:
                    fill = _down ? Theme.Accent : (_hover ? Theme.AccentHover : Theme.Accent);
                    border = fill; text = Theme.AccentText; break;
                case Kind.Subtle:
                    fill = _hover ? Theme.CardHover : Color.Transparent;
                    border = Color.Transparent; text = Theme.TextPrimary; break;
                default:
                    fill = _down ? Theme.Divider : (_hover ? Theme.CardHover : Theme.Card);
                    border = Theme.FieldBorder; text = Theme.TextPrimary; break;
            }

            using (var path = Theme.RoundRect(r, Ui.Px(4)))
            {
                if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.A > 0) using (var p = new Pen(border, Math.Max(1f, Ui.Px(1)))) g.DrawPath(p, path);
            }

            if (Focused)
                using (var p = new Pen(Color.FromArgb(120, Theme.Accent), Math.Max(1f, Ui.Px(1))))
                    g.DrawRectangle(p, Ui.Px(2), Ui.Px(2), Width - Ui.Px(5), Height - Ui.Px(5));

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>WinUI 输入框：圆角底 + 描边，聚焦时描边换成强调色。内部用原生 TextBox 承载输入。</summary>
    internal sealed class FlatTextBox : Control
    {
        private readonly TextBox _box;
        private bool _focus;

        internal FlatTextBox(string text = "")
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;

            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Ui.Font(10),
                // 显式给两色：Windows 深色主题下若不指定，编辑框文字可能被主题刷成浅色而落在浅色底上
                BackColor = Theme.FieldBg,
                ForeColor = Theme.TextPrimary
            };
            _box.GotFocus += (s, e) => { _focus = true; Invalidate(); };
            _box.LostFocus += (s, e) => { _focus = false; Invalidate(); };
            _box.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            _box.Text = text;
            Controls.Add(_box);
        }

        internal TextBox Inner => _box;

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool ReadOnlyBox
        {
            get { return _box.ReadOnly; }
            set
            {
                _box.ReadOnly = value;
                _box.TabStop = !value;
                _box.BackColor = value ? Theme.Layer : Theme.FieldBg;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int padX = Ui.Px(10), padY = (Height - _box.PreferredHeight) / 2;
            _box.SetBounds(padX, Math.Max(Ui.Px(3), padY), Math.Max(Ui.Px(10), Width - padX * 2), _box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.BackdropOf(this, Theme.Card));

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Theme.RoundRect(r, Ui.Px(4)))
            {
                using (var b = new SolidBrush(_box.ReadOnly ? Theme.Layer : Theme.FieldBg)) g.FillPath(b, path);
                using (var p = new Pen(_focus ? Theme.FieldBorderFocus : Theme.FieldBorder, _focus ? Math.Max(1.5f, Ui.Px(1.5f)) : Math.Max(1f, Ui.Px(1))))
                    g.DrawPath(p, path);
            }
            // 底部强调线（WinUI 的 TextBox 聚焦样式）
            if (_focus && !_box.ReadOnly)
            {
                using (var p = new Pen(Theme.Accent, Math.Max(2f, Ui.Px(2))))
                    g.DrawLine(p, Ui.Px(8), Height - Ui.Px(2), Width - Ui.Px(8), Height - Ui.Px(2));
            }
        }

        protected override void OnClick(EventArgs e)
        {
            _box.Focus();
            base.OnClick(e);
        }
    }

    /// <summary>WinUI ToggleSwitch：胶囊轨道 + 圆形滑块。</summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool _on;
        private bool _hover;

        internal ToggleSwitch(bool on)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            _on = on;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = Ui.Sz(96, 24);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Checked
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); OnCheckedChanged(EventArgs.Empty); } }
        }

        internal event EventHandler CheckedChanged;
        private void OnCheckedChanged(EventArgs e) { var h = CheckedChanged; if (h != null) h(this, e); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Checked = !Checked; base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.BackdropOf(this, Theme.Card));

            // 轨道
            int trackW = Ui.Px(40), trackH = Ui.Px(20);
            var track = new Rectangle(Px(0), (Height - trackH) / 2, trackW, trackH);
            using (var path = Theme.RoundRect(track, trackH / 2f))
            {
                var bg = _on ? Theme.Accent : (_hover ? Theme.FieldBorder : Color.FromArgb(200, Theme.FieldBorder));
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (!_on)
                    using (var p = new Pen(Theme.FieldBorder, Math.Max(1f, Ui.Px(1)))) g.DrawPath(p, path);
            }
            // 滑块
            int knob = Ui.Px(12);
            int kx = _on ? track.Right - knob - Ui.Px(4) : track.Left + Ui.Px(4);
            int ky = track.Top + (track.Height - knob) / 2;
            using (var b = new SolidBrush(_on ? Theme.AccentText : Theme.TextSecondary))
                g.FillEllipse(b, kx, ky, knob, knob);

            // 开/关文字
            var text = _on ? "开" : "关";
            TextRenderer.DrawText(g, text, Ui.Font(9), new Rectangle(track.Right + Ui.Px(10), 0, Width - track.Right - Ui.Px(10), Height),
                _on ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused)
                using (var p = new Pen(Color.FromArgb(160, Theme.Accent), Math.Max(1f, Ui.Px(1))))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        private static int Px(double v) { return Ui.Px(v); }
    }

    /// <summary>WinUI NavigationView 的左栏：选中项圆角高亮 + 左侧强调条。</summary>
    internal sealed class NavRail : Control
    {
        private readonly string[] _items;
        private int _selected;
        private int _hover = -1;

        internal NavRail(string[] items, int selected = 0)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            _items = items;
            _selected = selected;
            BackColor = Color.Transparent;
            Font = Ui.Font(10);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal int Selected
        {
            get { return _selected; }
            set { if (value >= 0 && value < _items.Length) { _selected = value; Invalidate(); } }
        }

        internal event EventHandler SelectedChanged;

        private int ItemHeight { get { return Ui.Px(38); } }
        private int ItemTop(int i) { return Ui.Px(6) + i * ItemHeight; }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Length; i++)
                if (p.Y >= ItemTop(i) && p.Y < ItemTop(i) + ItemHeight) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i >= 0 && i != _selected)
            {
                _selected = i;
                Invalidate();
                var h = SelectedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.BackdropOf(this, Theme.Layer));

            for (int i = 0; i < _items.Length; i++)
            {
                var r = new RectangleF(Ui.Px(8), ItemTop(i), Width - Ui.Px(16), ItemHeight - Ui.Px(2));
                bool sel = i == _selected;
                if (sel || i == _hover)
                {
                    using (var path = Theme.RoundRect(r, Ui.Px(5)))
                    using (var b = new SolidBrush(sel ? Theme.AccentSoftBg : Theme.CardHover))
                        g.FillPath(b, path);
                }
                if (sel)
                {
                    using (var b = new SolidBrush(Theme.Accent))
                    using (var path = Theme.RoundRect(new RectangleF(r.X + Ui.Px(0), r.Y + Ui.Px(8), Math.Max(2f, Ui.Px(3)), r.Height - Ui.Px(16)), Ui.Px(1.5f)))
                        g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, _items[i], Font,
                    new Rectangle((int)r.X + Ui.Px(14), (int)r.Y, (int)r.Width - Ui.Px(20), (int)r.Height),
                    sel ? Theme.TextPrimary : Theme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>小徽章：✔ 已找到 / ✘ 未找到 / 灰色说明。</summary>
    internal sealed class InfoBadge : Control
    {
        internal enum Tone { Success, Danger, Neutral }

        private Tone _tone;
        private string _text;

        internal InfoBadge(string text, Tone tone)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            _text = text;
            _tone = tone;
            BackColor = Color.Transparent;
            Font = Ui.Font(9);
            AutoSizeBadge();
        }

        internal void SetState(string text, Tone tone)
        {
            _text = text; _tone = tone; AutoSizeBadge(); Invalidate();
        }

        private void AutoSizeBadge()
        {
            Size f;
            using (var g = CreateGraphics())
                f = TextRenderer.MeasureText(g, _text, Font, new Size(1000, 100), TextFormatFlags.NoPadding);
            Width = f.Width + Ui.Px(18);
            Height = Ui.Px(22);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Ui.BackdropOf(this, Theme.Card));

            Color bg, fg;
            switch (_tone)
            {
                case Tone.Success: bg = Theme.SuccessBg; fg = Theme.Success; break;
                case Tone.Danger: bg = Theme.DangerBg; fg = Theme.Danger; break;
                default: bg = Theme.Layer; fg = Theme.TextSecondary; break;
            }
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Ui.Px(4)))
            using (var b = new SolidBrush(bg)) g.FillPath(b, path);

            TextRenderer.DrawText(g, _text, Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
