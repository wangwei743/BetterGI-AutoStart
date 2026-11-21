using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using Microsoft.Win32;

namespace BetterGIAutoStart
{
    /// <summary>
    /// WinUI 风格的样式令牌：颜色跟随系统明暗、字体优先 Segoe UI Variable、尺寸按 DPI 缩放。
    /// 只做"像 WinUI"，不引入 Windows App SDK，保持零新依赖。
    /// </summary>
    internal static class Theme
    {
        internal static bool IsDark { get; private set; }

        // 背景与容器
        internal static Color Layer;          // 窗口底
        internal static Color Card;           // 卡片
        internal static Color CardHover;
        internal static Color Stroke;         // 1px 描边
        internal static Color Divider;

        // 文本
        internal static Color TextPrimary;
        internal static Color TextSecondary;
        internal static Color TextTertiary;

        // 强调色（WinUI Accent）
        internal static Color Accent;
        internal static Color AccentHover;
        internal static Color AccentText;
        internal static Color AccentSoftBg;   // 导航选中底

        // 输入控件
        internal static Color FieldBg;
        internal static Color FieldBorder;
        internal static Color FieldBorderFocus;

        // 语义色
        internal static Color Success;
        internal static Color SuccessBg;
        internal static Color Warning;
        internal static Color Danger;
        internal static Color DangerBg;

        private static string _fontFamily;

        internal static void Load()
        {
            IsDark = DetectDark();

            if (IsDark)
            {
                Layer = Color.FromArgb(32, 32, 32);
                Card = Color.FromArgb(44, 44, 44);
                CardHover = Color.FromArgb(52, 52, 52);
                Stroke = Color.FromArgb(66, 66, 66);
                Divider = Color.FromArgb(58, 58, 58);
                TextPrimary = Color.FromArgb(255, 255, 255);
                TextSecondary = Color.FromArgb(196, 196, 196);
                TextTertiary = Color.FromArgb(150, 150, 150);
                Accent = Color.FromArgb(96, 205, 255);
                AccentHover = Color.FromArgb(130, 216, 255);
                AccentText = Color.FromArgb(20, 36, 46);
                AccentSoftBg = Color.FromArgb(46, 62, 72);
                FieldBg = Color.FromArgb(37, 37, 38);
                FieldBorder = Color.FromArgb(78, 78, 78);
                FieldBorderFocus = Accent;
                Success = Color.FromArgb(108, 203, 95);
                SuccessBg = Color.FromArgb(38, 60, 36);
                Warning = Color.FromArgb(252, 225, 0);
                Danger = Color.FromArgb(255, 153, 164);
                DangerBg = Color.FromArgb(64, 38, 42);
            }
            else
            {
                Layer = Color.FromArgb(243, 243, 243);
                Card = Color.FromArgb(255, 255, 255);
                CardHover = Color.FromArgb(249, 249, 249);
                Stroke = Color.FromArgb(229, 229, 229);
                Divider = Color.FromArgb(235, 235, 235);
                TextPrimary = Color.FromArgb(26, 26, 26);
                TextSecondary = Color.FromArgb(93, 93, 93);
                TextTertiary = Color.FromArgb(117, 117, 117);
                Accent = Color.FromArgb(0, 95, 184);
                AccentHover = Color.FromArgb(25, 110, 200);
                AccentText = Color.White;
                AccentSoftBg = Color.FromArgb(233, 240, 250);
                FieldBg = Color.FromArgb(253, 253, 253);
                FieldBorder = Color.FromArgb(214, 214, 214);
                FieldBorderFocus = Accent;
                Success = Color.FromArgb(15, 123, 15);
                SuccessBg = Color.FromArgb(226, 244, 226);
                Warning = Color.FromArgb(157, 93, 0);
                Danger = Color.FromArgb(196, 43, 28);
                DangerBg = Color.FromArgb(253, 231, 233);
            }

            _fontFamily = PickFont();
        }

        /// <summary>WinUI 的正文 14px ≈ 10.5pt；这里统一用"逻辑点"，由调用方乘 DPI 缩放。</summary>
        internal static Font Font(float sizePt, FontStyle style = FontStyle.Regular)
        {
            try { return new Font(_fontFamily, sizePt, style, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", sizePt, style, GraphicsUnit.Point); }
        }

        private static string PickFont()
        {
            try
            {
                using (var f = new InstalledFontCollection())
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var fam in f.Families) names.Add(fam.Name);
                    foreach (var want in new[] { "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI", "Microsoft YaHei UI" })
                        if (names.Contains(want)) return want;
                }
            }
            catch { }
            return "Segoe UI";
        }

        private static bool DetectDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        var v = k.GetValue("AppsUseLightTheme");
                        if (v is int i) return i == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>圆角矩形路径（WinUI：卡片 8，控件 4）</summary>
        internal static System.Drawing.Drawing2D.GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            float d = radius * 2f;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }
    }
}
