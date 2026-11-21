using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>
    /// WinUI 风格的配置窗口（P2）：
    /// 左栏导航 + 卡片式设置项 + 底部操作条。外观全部自绘，不引入 Windows App SDK。
    /// </summary>
    internal sealed class ConfigShellForm : Form
    {
        private ConfigSnapshot _snap;
        private AppConfig _cfg;

        private NavRail _rail;
        private Panel _pageHost;
        private Label _lblTitle, _lblSubtitle, _lblStatus;
        private FlatButton _btnPilot, _btnImport, _btnExport, _btnSave;
        private int _page = -1;
        private bool _dirty;

        // 路径页控件
        private FlatTextBox[] _pathBoxes;
        private InfoBadge[] _pathBadges;

        // 自动化页控件
        private ToggleSwitch[] _tgAuto;
        private FlatTextBox[] _numAuto;

        private static readonly string[] NavItems =
        {
            "路径与程序", "自动化行为", "时间与超时", "截图与日志", "关键词表", "高级（坐标）"
        };

        private static readonly string[] PathNames = { "启动器", "原神本体", "BetterGI" };
        private static readonly string[] PathExes = { "launcher.exe", "YuanShen.exe", "BetterGI.exe" };

        internal ConfigShellForm(ConfigSnapshot snap)
        {
            _snap = snap;
            _cfg = snap.Config ?? new AppConfig();

            Theme.Load();

            SuspendLayout();
            Text = "BetterGI-AutoStart · 配置";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.Px(1000), Ui.Px(720));
            MinimumSize = new Size(Ui.Px(880), Ui.Px(600));
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(10);
            BackColor = Theme.Layer;
            ForeColor = Theme.TextPrimary;
            DoubleBuffered = true;
            Icon = null;
            ResumeLayout(false);

            // DPI 只能在窗口句柄创建后才知道，界面放到 OnLoad 里搭
            Load += (s, e) => { Ui.Init(DeviceDpi); Theme.ApplyTitleBar(Handle); BuildUi(); };

            FormClosing += (s, e) =>
            {
                if (!_dirty) return;
                var r = MessageBox.Show(this, "有未保存的修改，要保存吗？", "关闭配置",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) e.Cancel = true;
                else if (r == DialogResult.Yes) { DoSave(); if (_dirty) e.Cancel = true; }
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }

        // ───────────────────────── 搭界面 ─────────────────────────

        private void BuildUi()
        {
            SuspendLayout();
            BackColor = Theme.Layer;
            ClientSize = new Size(Ui.Px(1000), Ui.Px(720));

            // 左栏导航
            _rail = new NavRail(NavItems, 0);
            _rail.SelectedChanged += (s, e) => ShowPage(_rail.Selected);
            Controls.Add(_rail);

            // 底部操作条
            var bottom = new Panel { BackColor = Theme.Layer };
            bottom.Paint += (s, e) =>
            {
                using (var p = new Pen(Theme.Divider, Math.Max(1f, Ui.Px(1))))
                    e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0);
            };

            _lblStatus = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextTertiary,
                Font = Ui.Font(9),
                BackColor = Theme.Layer,
                Text = ""
            };

            _btnPilot = new FlatButton("试跑（停在游戏就绪）", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
            _btnPilot.Click += (s, e) => RunPilot();

            _btnImport = new FlatButton("导入配置", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
            _btnImport.Click += (s, e) => ImportConfig();

            _btnExport = new FlatButton("导出配置", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
            _btnExport.Click += (s, e) => ExportConfig();

            _btnSave = new FlatButton("保存", FlatButton.Kind.Accent) { Height = Ui.Px(32) };
            _btnSave.Click += (s, e) => DoSave();

            bottom.Controls.AddRange(new Control[] { _lblStatus, _btnPilot, _btnImport, _btnExport, _btnSave });
            bottom.Resize += (s, e) => LayoutBottom(bottom);
            Controls.Add(bottom);

            // 右侧内容区
            _pageHost = new Panel { BackColor = Theme.Layer };
            _lblTitle = new Label
            {
                AutoSize = false,
                Font = Ui.Font(20, FontStyle.Bold),
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.Layer
            };
            _lblSubtitle = new Label
            {
                AutoSize = false,
                Font = Ui.Font(9.5f),
                ForeColor = Theme.TextSecondary,
                BackColor = Theme.Layer
            };
            _pageHost.Controls.Add(_lblTitle);
            _pageHost.Controls.Add(_lblSubtitle);
            Controls.Add(_pageHost);

            Resize += (s, e) => LayoutShell();
            LayoutShell();

            ShowPage(0);
            UpdateStatus("已加载配置", false);

            ResumeLayout(true);
            LayoutShell();
        }

        private void LayoutShell()
        {
            if (_rail == null) return;
            int railW = Ui.Px(196);
            int bottomH = Ui.Px(58);
            int pad = Ui.Px(16);

            _rail.SetBounds(pad, pad + Ui.Px(8), railW, Math.Max(Ui.Px(120), ClientSize.Height - bottomH - pad * 2 - Ui.Px(8)));
            _pageHost.SetBounds(pad * 2 + railW, pad + Ui.Px(8),
                Math.Max(Ui.Px(200), ClientSize.Width - railW - pad * 3),
                Math.Max(Ui.Px(160), ClientSize.Height - bottomH - pad * 2 - Ui.Px(8)));

            var bottom = _btnSave?.Parent;
            if (bottom != null)
                bottom.SetBounds(0, ClientSize.Height - bottomH, ClientSize.Width, bottomH);

            _lblTitle.SetBounds(0, 0, _pageHost.Width, Ui.Px(34));
            _lblSubtitle.SetBounds(0, Ui.Px(36), _pageHost.Width, Ui.Px(22));

            LayoutBottom(bottom);
            LayoutCurrentPage();
        }

        private void LayoutBottom(Control bottom)
        {
            if (bottom == null || _btnSave == null) return;
            int pad = Ui.Px(16);
            int h = bottom.Height;
            int y = (h - Ui.Px(32)) / 2;
            int x = bottom.Width - pad;

            _btnSave.Size = new Size(Ui.Px(96), Ui.Px(32));
            x -= _btnSave.Width; _btnSave.Location = new Point(x, y); x -= Ui.Px(8);
            _btnExport.Size = new Size(Ui.Px(96), Ui.Px(32));
            x -= _btnExport.Width; _btnExport.Location = new Point(x, y); x -= Ui.Px(4);
            _btnImport.Size = new Size(Ui.Px(96), Ui.Px(32));
            x -= _btnImport.Width; _btnImport.Location = new Point(x, y); x -= Ui.Px(12);
            _btnPilot.Size = new Size(Ui.Px(176), Ui.Px(32));
            x -= _btnPilot.Width; _btnPilot.Location = new Point(x, y); x -= Ui.Px(12);

            _lblStatus.SetBounds(pad, 0, Math.Max(Ui.Px(80), x - pad), h);
        }

        // ───────────────────────── 页面 ─────────────────────────

        private void ShowPage(int index)
        {
            if (_page == index && index == 0 && _pathBoxes != null) return;
            _page = index;

            foreach (Control c in _pageHost.Controls.CastSnapshot())
                if (c != _lblTitle && c != _lblSubtitle) c.Dispose();
            _pageHost.Controls.Clear();
            _pageHost.Controls.Add(_lblTitle);
            _pageHost.Controls.Add(_lblSubtitle);

            _pathBoxes = null; _pathBadges = null; _tgAuto = null; _numAuto = null;

            switch (index)
            {
                case 0:
                    _lblTitle.Text = "路径与程序";
                    _lblSubtitle.Text = "告诉程序：启动器、原神本体、BetterGI 分别在哪。带 ✔ 的表示已找到。";
                    BuildPathPage();
                    break;
                case 1:
                    _lblTitle.Text = "自动化行为";
                    _lblSubtitle.Text = "这些开关决定程序做到哪一步：是否看门狗、失败是否兜底关机、是否自动点进游戏。";
                    BuildAutoPage();
                    break;
                default:
                    _lblTitle.Text = NavItems[index];
                    _lblSubtitle.Text = "这一页在下一阶段做（P3），先保留占位。";
                    BuildPlaceholder(index);
                    break;
            }
            LayoutCurrentPage();
        }

        private void LayoutCurrentPage()
        {
            if (_pageHost == null) return;
            _lblTitle.SetBounds(0, 0, _pageHost.Width, Ui.Px(34));
            _lblSubtitle.SetBounds(0, Ui.Px(36), _pageHost.Width, Ui.Px(22));

            if (_page == 0) LayoutPathPage();
            else if (_page == 1) LayoutAutoPage();
        }

        private FlatCard AddCard(Label title, int y, int h)
        {
            var card = new FlatCard { BackColor = Color.Transparent };
            card.SetBounds(0, y, _pageHost.Width, h);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            if (title != null) card.Controls.Add(title);
            _pageHost.Controls.Add(card);
            return card;
        }

        private static Label MkLabel(string text, float pt, Color color, bool bold = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = Ui.Font(pt, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        private const int RowH = 88;

        private void BuildPathPage()
        {
            var card = AddCard(null, Ui.Px(66), Ui.Px(60) + RowH * 3);
            _pathBoxes = new FlatTextBox[3];
            _pathBadges = new InfoBadge[3];

            string[] values = { _cfg.LauncherPath, _cfg.GameExePath, _cfg.BetterGiPath };

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var lbl = MkLabel(PathNames[i], 11, Theme.TextPrimary, true);
                var sub = MkLabel(PathExes[i] + "（点右侧可浏览选择）", 9, Theme.TextTertiary);
                var box = new FlatTextBox(values[i] ?? "");
                box.TextChanged += (s, e) => { _dirty = true; ValidatePaths(); };
                var badge = new InfoBadge("检查中", InfoBadge.Tone.Neutral);
                var btn = new FlatButton("浏览…", FlatButton.Kind.Standard);
                btn.Click += (s, e) => BrowsePath(idx);
                btn.Height = Ui.Px(30);

                card.Controls.Add(lbl); card.Controls.Add(sub);
                card.Controls.Add(box); card.Controls.Add(badge); card.Controls.Add(btn);

                lbl.Tag = "lbl" + i; sub.Tag = "sub" + i;
                box.Tag = "box" + i;
                _pathBoxes[i] = box;
                _pathBadges[i] = badge;
            }

            // 说明卡
            var hint = AddCard(null, Ui.Px(66) + Ui.Px(60) + RowH * 3 + Ui.Px(12), Ui.Px(104));
            hint.DrawBorder = false;
            var hintTitle = MkLabel("三个路径怎么找", 10.5f, Theme.TextPrimary, true);
            hintTitle.Tag = "hintTitle";
            var hintBody = MkLabel(
                "启动器：「游戏安装路径」在启动器右上角设置里看；本体：那个目录下的 YuanShen.exe；" +
                "BetterGI：你解压 BetterGI 的目录里的 BetterGI.exe。路径里有反斜杠，配置里要写两个 \\\\。",
                9.5f, Theme.TextSecondary);
            hintBody.Tag = "hintBody";
            hint.Controls.Add(hintTitle);
            hint.Controls.Add(hintBody);

            ValidatePaths();
        }

        private void LayoutPathPage()
        {
            if (_pathBoxes == null) return;
            int w = _pageHost.Width;
            int cardW = w;
            int top = Ui.Px(66) + Ui.Px(8);

            var cards = _pageHost.Controls.CastSnapshot();
            FlatCard pathCard = null, hintCard = null;
            foreach (var c in cards)
            {
                var fc = c as FlatCard;
                if (fc == null) continue;
                if (fc.Controls.Count > 0 && fc.Controls[0].Text == PathNames[0]) pathCard = fc;
                else if (fc.Controls.Count > 0 && fc.Controls[0].Text == "三个路径怎么找") hintCard = fc;
            }
            if (pathCard != null) { pathCard.SetBounds(0, Ui.Px(66), cardW, Ui.Px(52) + RowH * 3); }

            for (int i = 0; i < 3; i++)
            {
                if (pathCard == null || _pathBoxes[i] == null) break;
                int y = Ui.Px(20) + i * RowH;
                var ctrls = pathCard.Controls.CastSnapshot();
                Label lbl = null, sub = null; InfoBadge badge = null; FlatButton btn = null;
                foreach (var c in ctrls)
                {
                    if (c.Tag as string == "lbl" + i) lbl = c as Label;
                    else if (c.Tag as string == "sub" + i) sub = c as Label;
                    else if (c is InfoBadge) { /* 顺序取第 i 个 */ }
                    else if (c is FlatButton) { }
                }
                // 按加入顺序取徽章/按钮更稳：路径卡里每行的控件顺序是 lbl,sub,box,badge,btn
                if (badge == null || btn == null)
                {
                    int seen = 0;
                    foreach (var c in ctrls)
                    {
                        if (c is InfoBadge ib) { if (seen == i) badge = ib; seen++; }
                    }
                    seen = 0;
                    foreach (var c in ctrls)
                    {
                        if (c is FlatButton fb) { if (seen == i) btn = fb; seen++; }
                    }
                }

                if (lbl != null) lbl.SetBounds(Ui.Px(20), y + Ui.Px(6), Ui.Px(170), Ui.Px(22));
                if (sub != null) sub.SetBounds(Ui.Px(20), y + Ui.Px(30), Ui.Px(180), Ui.Px(20));
                if (btn != null) btn.SetBounds(pathCard.Width - Ui.Px(108), y + Ui.Px(14), Ui.Px(88), Ui.Px(30));
                if (_pathBadges[i] != null)
                    _pathBadges[i].SetBounds(pathCard.Width - Ui.Px(120) - _pathBadges[i].Width - Ui.Px(8), y + Ui.Px(17),
                        _pathBadges[i].Width, _pathBadges[i].Height);
                int boxX = Ui.Px(206);
                int boxW = Math.Max(Ui.Px(180), pathCard.Width - boxX - Ui.Px(210) - (_pathBadges[i]?.Width ?? 0));
                if (_pathBoxes[i] != null) _pathBoxes[i].SetBounds(boxX, y + Ui.Px(14), boxW, Ui.Px(32));
            }

            if (hintCard != null)
            {
                hintCard.SetBounds(0, Ui.Px(66) + Ui.Px(52) + RowH * 3 + Ui.Px(12), cardW, Ui.Px(104));
                foreach (var c in hintCard.Controls.CastSnapshot())
                {
                    if (c.Tag as string == "hintTitle") c.SetBounds(Ui.Px(20), Ui.Px(14), hintCard.Width - Ui.Px(40), Ui.Px(22));
                    else if (c.Tag as string == "hintBody") c.SetBounds(Ui.Px(20), Ui.Px(38), hintCard.Width - Ui.Px(40), Ui.Px(54));
                }
            }
        }

        private void BuildAutoPage()
        {
            var card = AddCard(null, Ui.Px(66), Ui.Px(28) + RowH * 5);
            _tgAuto = new ToggleSwitch[3];
            _numAuto = new FlatTextBox[2];

            var items = new[]
            {
                "看门狗：BetterGI 退出后自动关机",
                "启动失败时兜底关机（一小时兜底）",
                "允许直接启动原神（跳过启动器）"
            };
            bool[] values = { _cfg.WatchdogEnabled, _cfg.FallbackShutdown, _cfg.AllowDirectGameFallback };

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var lbl = MkLabel(items[i], 10.5f, Theme.TextPrimary);
                lbl.Tag = "al" + i;
                var tg = new ToggleSwitch(values[i]);
                tg.Tag = "tg" + i;
                tg.CheckedChanged += (s, e) => { _dirty = true; UpdateStatus("有未保存的修改", true); };
                _tgAuto[i] = tg;
                card.Controls.Add(lbl);
                card.Controls.Add(tg);
            }

            var numItems = new[]
            {
                Tuple.Create("开机后等待（分钟，0–240）", "MinUptimeMinutes", _cfg.MinUptimeMinutes),
                Tuple.Create("看门狗轮询间隔（秒，5–300）", "WatchdogPollSeconds", _cfg.WatchdogPollSeconds)
            };
            for (int i = 0; i < 2; i++)
            {
                var lbl = MkLabel(numItems[i].Item1, 10.5f, Theme.TextPrimary);
                lbl.Tag = "nl" + i;
                var box = new FlatTextBox(numItems[i].Item3.ToString(CultureInfo.InvariantCulture));
                box.Tag = "nb" + i;
                box.Width = Ui.Px(90);
                box.TextChanged += (s, e) => { _dirty = true; UpdateStatus("有未保存的修改", true); };
                _numAuto[i] = box;
                card.Controls.Add(lbl);
                card.Controls.Add(box);
            }
        }

        private void LayoutAutoPage()
        {
            FlatCard card = null;
            foreach (var c in _pageHost.Controls.CastSnapshot())
            {
                var fc = c as FlatCard;
                if (fc != null && fc.Controls.Count > 0) { card = fc; break; }
            }
            if (card == null) return;

            card.SetBounds(0, Ui.Px(66), _pageHost.Width, Ui.Px(28) + RowH * 5);

            foreach (var c in card.Controls.CastSnapshot())
            {
                var tag = c.Tag as string;
                if (tag == null || tag.Length < 3) continue;
                int idx;
                if (!int.TryParse(tag.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)) continue;

                int labelW = Math.Max(Ui.Px(140), card.Width - Ui.Px(190));
                if (tag.StartsWith("al") || tag.StartsWith("nl"))
                {
                    int row = tag.StartsWith("al") ? idx : 3 + idx;
                    c.SetBounds(Ui.Px(20), Ui.Px(24) + row * RowH, labelW, Ui.Px(40));
                }
                else if (tag.StartsWith("tg"))
                {
                    c.SetBounds(card.Width - Ui.Px(122), Ui.Px(28) + idx * RowH, Ui.Px(96), Ui.Px(28));
                }
                else if (tag.StartsWith("nb"))
                {
                    c.SetBounds(card.Width - Ui.Px(136), Ui.Px(28) + (3 + idx) * RowH, Ui.Px(110), Ui.Px(32));
                }
            }
        }

        private void BuildPlaceholder(int index)
        {
            var card = AddCard(null, Ui.Px(66), Ui.Px(150));
            var lbl = MkLabel("P3 会做：" + NavItems[index], 11, Theme.TextPrimary, true);
            lbl.SetBounds(Ui.Px(20), Ui.Px(18), _pageHost.Width - Ui.Px(40), Ui.Px(24));
            var sub = MkLabel("先跑通读写、路径校验、试跑与导入导出；这一页的字段随后补上。", 9.5f, Theme.TextSecondary);
            sub.SetBounds(Ui.Px(20), Ui.Px(48), _pageHost.Width - Ui.Px(40), Ui.Px(22));
            card.Controls.Add(lbl);
            card.Controls.Add(sub);
        }

        // ───────────────────────── 动作 ─────────────────────────

        private void BrowsePath(int idx)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 " + PathExes[idx];
                dlg.Filter = "可执行文件|*.exe|所有文件|*.*";
                var cur = _pathBoxes[idx].Text.Trim();
                if (cur.Length > 0)
                {
                    try { dlg.InitialDirectory = Path.GetDirectoryName(cur); } catch { }
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBoxes[idx].Text = dlg.FileName;
                    ValidatePaths();
                }
            }
        }

        private void ValidatePaths(bool markDirty = true)
        {
            if (_pathBoxes == null || _pathBadges == null) return;
            string[] vals = { _pathBoxes[0].Text, _pathBoxes[1].Text, _pathBoxes[2].Text };
            for (int i = 0; i < 3; i++)
            {
                var v = (vals[i] ?? "").Trim();
                bool ok = v.Length > 0 && File.Exists(v);
                _pathBadges[i].SetState(ok ? "✔ 已找到" : (v.Length == 0 ? "未填写" : "✘ 找不到"),
                    ok ? InfoBadge.Tone.Success : InfoBadge.Tone.Danger);
            }
            if (_page == 0) LayoutPathPage();
            if (markDirty) UpdateStatus("有未保存的修改", true);
        }

        private void CollectToConfig()
        {
            if (_pathBoxes != null)
            {
                _cfg.LauncherPath = _pathBoxes[0].Text.Trim();
                _cfg.GameExePath = _pathBoxes[1].Text.Trim();
                _cfg.BetterGiPath = _pathBoxes[2].Text.Trim();
            }
            if (_tgAuto != null && _tgAuto.Length == 3)
            {
                _cfg.WatchdogEnabled = _tgAuto[0].Checked;
                _cfg.FallbackShutdown = _tgAuto[1].Checked;
                _cfg.AllowDirectGameFallback = _tgAuto[2].Checked;
            }
            if (_numAuto != null && _numAuto.Length == 2)
            {
                _cfg.MinUptimeMinutes = ParseClamp(_numAuto[0].Text, _cfg.MinUptimeMinutes, 0, 240);
                _cfg.WatchdogPollSeconds = ParseClamp(_numAuto[1].Text, _cfg.WatchdogPollSeconds, 5, 300);
            }
        }

        private static int ParseClamp(string text, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        private void DoSave()
        {
            CollectToConfig();
            var r = ConfigStore.Save(_snap, _cfg);
            if (r.Ok)
            {
                _dirty = false;
                UpdateStatus("✔ 已保存" + (string.IsNullOrEmpty(r.BackupPath) ? "" : "（备份 " + Path.GetFileName(r.BackupPath) + "）"), false);
            }
            else
            {
                UpdateStatus("✘ " + r.Message, false, true);
                MessageBox.Show(this, r.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RunPilot()
        {
            if (MessageBox.Show(this,
                    "试跑会用真实环境跑一遍：启动启动器 → 点开始游戏 → 等进游戏就绪，然后停下。\n\n" +
                    "· 不会启动 BetterGI 一条龙\n· 不会关机\n· 大约需要 3–10 分钟，期间请不要动鼠标\n\n要继续吗？",
                    "确认试跑", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            try
            {
                CollectToConfig();
                var r = ConfigStore.Save(_snap, _cfg);
                if (!r.Ok)
                {
                    MessageBox.Show(this, "先保存配置失败，没开始试跑：" + r.Message, "试跑取消",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var exe = Path.Combine(AppContext.BaseDirectory, "BetterGIAutoStart.exe");
                if (!File.Exists(exe)) exe = Application.ExecutablePath;

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--stop-before-bettergi",
                    UseShellExecute = true,
                    Verb = "runas"          // 主程序要管理员权限（OCR/前台窗口），这里直接提权启动
                });
                UpdateStatus("已启动试跑（会另开一个控制台窗口，跑到「游戏已就绪」就停）", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动试跑失败：" + ex.Message, "试跑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportConfig()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件|*.json|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var imported = ConfigStore.Load(dlg.FileName);
                if (!string.IsNullOrEmpty(imported.ParseError))
                {
                    MessageBox.Show(this, "这个文件读不了：" + imported.ParseError, "导入失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 只换值，不换写入目标：保存仍然写回 exe 旁边那份配置
                _cfg = imported.Config ?? new AppConfig();
                _dirty = true;
                var keep = _snap;
                _snap = new ConfigSnapshot
                {
                    Path = keep.Path,
                    Existed = keep.Existed,
                    OriginalText = keep.OriginalText,
                    Root = keep.Root,
                    Config = _cfg
                };
                ShowPage(0);
                _rail.Selected = 0;
                UpdateStatus("已导入 " + Path.GetFileName(dlg.FileName) + "（还没写盘，点保存才生效）", true);
            }
        }

        private void ExportConfig()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出配置";
                dlg.Filter = "配置文件|*.json|所有文件|*.*";
                dlg.FileName = "BetterGIAutoStart.config.export.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    CollectToConfig();
                    var root = ConfigStore.BuildMerged(_snap, _cfg);
                    var text = ConfigStore.Serialize(root);
                    File.WriteAllText(dlg.FileName, text, new System.Text.UTF8Encoding(true));
                    UpdateStatus("已导出到 " + dlg.FileName, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateStatus(string text, bool warn)
        {
            UpdateStatus(text, warn, false);
        }

        private void UpdateStatus(string text, bool warn, bool error)
        {
            if (_lblStatus == null) return;
            _lblStatus.Text = text;
            _lblStatus.ForeColor = error ? Theme.Danger : (warn ? Theme.Warning : Theme.TextTertiary);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Layer);
        }
    }

    internal static class ControlExtension
    {
        /// <summary>遍历子控件时先拍快照，避免边遍历边 Dispose 导致的集合修改异常。</summary>
        internal static System.Collections.Generic.List<Control> CastSnapshot(this Control.ControlCollection coll)
        {
            var list = new System.Collections.Generic.List<Control>(coll.Count);
            foreach (Control c in coll) list.Add(c);
            return list;
        }
    }

    internal static class ConfigUi
    {
        // 只在 --config 入口把进程设成 per-monitor v2，让 WinForms 按实际 DPI 渲染文字。
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        internal static int Run()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }

            // 界面出异常要留证据：写一份 crash 日志到 exe 旁边，而不是只弹一个"Microsoft .NET"框
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var snap = ConfigStore.Load(ConfigStore.DefaultPath);
                using (var form = new ConfigShellForm(snap))
                {
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                return 1;
            }
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "ConfigUi.error.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                sb.AppendLine(ex == null ? "(null)" : ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));

                MessageBox.Show(ex == null ? "未知错误" : ex.GetType().Name + "：" + ex.Message +
                                "\n\n详细信息已写入：\n" + path,
                    "配置界面出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
