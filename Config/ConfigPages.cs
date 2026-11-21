using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>
    /// 配置窗口的「时间与超时 / 截图与日志 / 关键词表 / 高级（坐标）」四页。
    /// 这几页字段多，统一用"卡片 + 两列行"的排版：一行 = 一个配置项（标签 + 输入控件）。
    /// </summary>
    internal sealed partial class ConfigShellForm
    {
        private sealed class RowSpec
        {
            internal FlatCard Card;
            internal Control Label;
            internal Control Sub;
            internal Control Field;
            internal int Top;
            internal int RowH;
        }

        private readonly List<RowSpec> _rows = new List<RowSpec>();
        private readonly Dictionary<string, FlatTextBox> _fields = new Dictionary<string, FlatTextBox>();
        private readonly Dictionary<string, ToggleSwitch> _switches = new Dictionary<string, ToggleSwitch>();
        private readonly Dictionary<string, FlatTextArea> _kw = new Dictionary<string, FlatTextArea>();
        private Label _lblConflict;

        private const int SpecRowH = 62;

        /// <summary>内容区可用宽度（滚动条出现时让出 17px）。</summary>
        private int ContentW
        {
            get
            {
                if (_scroll == null) return Math.Max(Ui.Px(200), _pageHost.Width);
                int w = _scroll.ClientSize.Width - Ui.Px(10);
                if (_scroll.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
                return Math.Max(Ui.Px(200), w);
            }
        }

        private FlatCard SpecCard(int y, int rows)
        {
            var card = new FlatCard { BackColor = Color.Transparent };
            card.SetBounds(0, y, ContentW, Ui.Px(16) + rows * Ui.Px(SpecRowH));
            (_scroll ?? _pageHost).Controls.Add(card);
            return card;
        }

        // ─────────── 行构造：数值 / 文本 / 开关 ───────────

        private void AddNumRow(FlatCard card, string key, string label, string hint, int row, int col, int cols, int rowHeight)
        {
            AddNumRow(card, key, label, hint, row, col, cols, rowHeight, 96);
        }

        private void AddNumRow(FlatCard card, string key, string label, string hint, int row, int col, int cols, int rowHeight, int fieldW)
        {
            var box = new FlatTextBox(ReadNum(key).ToString(CultureInfo.InvariantCulture));
            box.TextChanged += (s, e) => { MarkDirty(); };
            _fields[key] = box;
            AddRowCore(card, label, hint, box, row, col, cols, rowHeight, fieldW);
        }

        private void AddDoubleRow(FlatCard card, string key, string label, string hint, int row, int col, int cols, int rowHeight)
        {
            var v = ReadDouble(key);
            var box = new FlatTextBox(v.ToString("0.###", CultureInfo.InvariantCulture));
            box.TextChanged += (s, e) => { MarkDirty(); };
            _fields[key] = box;
            AddRowCore(card, label, hint, box, row, col, cols, rowHeight, 96);
        }

        private void AddTextRow(FlatCard card, string key, string label, string hint, int row, int col, int cols, int rowHeight, int fieldW = 150)
        {
            var box = new FlatTextBox(ReadText(key));
            box.TextChanged += (s, e) => { MarkDirty(); };
            _fields[key] = box;
            AddRowCore(card, label, hint, box, row, col, cols, rowHeight, fieldW);
        }

        private void AddSwitchRow(FlatCard card, string key, string label, string hint, int row, int col, int cols, int rowHeight)
        {
            var tg = new ToggleSwitch(ReadBool(key));
            tg.CheckedChanged += (s, e) => { MarkDirty(); };
            _switches[key] = tg;
            AddRowCore(card, label, hint, tg, row, col, cols, rowHeight, 84);
        }

        private void AddRowCore(FlatCard card, string label, string hint, Control field, int row, int col, int cols, int rowHeight, int fieldW)
        {
            var lbl = MkLabel(label, 10.5f, Theme.TextPrimary);
            Control sub = string.IsNullOrEmpty(hint) ? null : MkLabel(hint, 8.5f, Theme.TextTertiary);
            card.Controls.Add(lbl);
            if (sub != null) card.Controls.Add(sub);
            card.Controls.Add(field);
            _rows.Add(new RowSpec
            {
                Card = card,
                Label = lbl,
                Sub = sub,
                Field = field,
                Top = Ui.Px(16) + row * Ui.Px(rowHeight),
                RowH = Ui.Px(rowHeight),
                // 列号交给布局阶段算
            });
            field.Tag = string.Format(CultureInfo.InvariantCulture, "col{0}/{1}/{2}", col, cols, fieldW);
        }

        // ─────────── 三页的字段定义 ───────────

        private void BuildTimeoutPage()
        {
            var card = SpecCard(Ui.Px(0), 7);
            var items = new[]
            {
                Tuple.Create("PollSeconds", "轮询间隔（秒）", "主循环多久检查一次界面"),
                Tuple.Create("LauncherStartTimeout", "启动器启动超时（秒）", "点了开始菜单后等窗口出现"),
                Tuple.Create("LauncherStateTimeout", "启动器状态超时（秒）", "等开始/更新按钮出现"),
                Tuple.Create("UpdateTimeout", "更新超时（秒）", "大版本更新最长等多久，默认 30 分钟"),
                Tuple.Create("LauncherSelfUpdateWaitSeconds", "启动器自更新等待（秒）", "点了立即更新后等它重启"),
                Tuple.Create("GameProcessTimeout", "游戏进程超时（秒）", "点开始游戏后等 YuanShen.exe"),
                Tuple.Create("GameWindowTimeout", "游戏窗口超时（秒）", "进程起了但窗口没出来"),
                Tuple.Create("AgreementTimeout", "协议弹窗超时（秒）", "等协议/公告窗口出现"),
                Tuple.Create("GameEntryTimeout", "点击进入超时（秒）", "标题界面点「点击进入」"),
                Tuple.Create("GameSettleSeconds", "进游戏后稳定等待（秒）", "等 HUD 出现再复核 UID"),
                Tuple.Create("BetterGiVerifySeconds", "启动 BetterGI 后验证（秒）", "确认它真的起来了"),
                Tuple.Create("WatchdogShutdownDelay", "看门狗关机延时（秒）", "一条龙结束后多久关机"),
                Tuple.Create("FallbackShutdownDelay", "兜底关机延时（秒）", "启动失败时的一小时兜底")
            };
            for (int i = 0; i < items.Length; i++)
            {
                int col = i < 7 ? 0 : 1;
                int row = i < 7 ? i : i - 7;
                AddNumRow(card, items[i].Item1, items[i].Item2, items[i].Item3, row, col, 2, SpecRowH);
            }
        }

        private void BuildShotPage()
        {
            int y = 0;

            // 截图目录：单独一张卡（两行高度，给输入框留够位置）
            var card1 = SpecCard(y, 2);
            y += card1.Height + Ui.Px(12);
            var dirBox = new FlatTextBox(_cfg.ShotDirectory ?? "");
            dirBox.TextChanged += (s, e) => { MarkDirty(); };
            _fields["ShotDirectory"] = dirBox;
            var lbl = MkLabel("截图目录", 10.5f, Theme.TextPrimary);
            var sub = MkLabel("留空表示 exe 同目录下的 shots\\", 8.5f, Theme.TextTertiary);
            var btn = new FlatButton("浏览…", FlatButton.Kind.Standard);
            btn.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择截图存放目录（留空用 exe 同目录的 shots）";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        dirBox.Text = dlg.SelectedPath;
                }
            };
            card1.Controls.Add(lbl); card1.Controls.Add(sub);
            card1.Controls.Add(dirBox); card1.Controls.Add(btn);
            _rows.Add(new RowSpec { Card = card1, Label = lbl, Sub = sub, Field = dirBox, Top = Ui.Px(16), RowH = Ui.Px(SpecRowH) });
            dirBox.Tag = "dirBox";
            btn.Tag = "dirBtn";

            // 保留策略
            var card2 = SpecCard(y, 2);
            y += card2.Height + Ui.Px(12);
            AddNumRow(card2, "ShotRetentionDays", "截图保留天数", "超过就自动删除（默认 30 天）", 0, 0, 2, SpecRowH);
            AddNumRow(card2, "ShotMaxTotalMB", "截图容量上限（MB）", "超了从最旧的删（默认 500）", 0, 1, 2, SpecRowH);
            AddSwitchRow(card2, "ShotKeepOnSuccess", "成功时也保留截图", "默认只留失败现场，成功就清掉", 1, 0, 2, SpecRowH);

            // 快捷操作
            var card3 = SpecCard(y, 2);
            var actLabel = MkLabel("快捷操作", 10.5f, Theme.TextPrimary);
            var hint = MkLabel("日志在 exe 同目录的 logs\\，截图在 shots\\（或上面填的目录）", 8.5f, Theme.TextTertiary);
            var b1 = new FlatButton("打开日志文件夹", FlatButton.Kind.Standard);
            var b2 = new FlatButton("打开截图文件夹", FlatButton.Kind.Standard);
            var b3 = new FlatButton("按策略清理截图", FlatButton.Kind.Standard);
            b1.Click += (s, e) => OpenFolder(System.IO.Path.Combine(AppContext.BaseDirectory, "logs"));
            b2.Click += (s, e) => OpenFolder(ResolveShotDir());
            b3.Click += (s, e) => CleanupShots();
            card3.Controls.Add(actLabel); card3.Controls.Add(hint);
            card3.Controls.Add(b1); card3.Controls.Add(b2); card3.Controls.Add(b3);
            _rows.Add(new RowSpec { Card = card3, Label = actLabel, Sub = hint, Field = b1, Top = Ui.Px(16), RowH = Ui.Px(SpecRowH) });
            b1.Tag = "act1"; b2.Tag = "act2"; b3.Tag = "act3";
        }
        private void BuildAdvancedPage()
        {
            var card = SpecCard(Ui.Px(0), 7);
            AddDoubleRow(card, "GenshinTabRatioX", "原神页签 X（相对）", "1920x1080 下约 0.026", 0, 0, 2, SpecRowH);
            AddDoubleRow(card, "GenshinTabRatioY", "原神页签 Y（相对）", "1920x1080 下约 0.836", 0, 1, 2, SpecRowH);
            AddDoubleRow(card, "AgreementRatioX", "协议按钮 X（相对）", "只在找不到模板时兜底用", 1, 0, 2, SpecRowH);
            AddDoubleRow(card, "AgreementRatioY", "协议按钮 Y（相对）", "默认 0.82（偏下）", 1, 1, 2, SpecRowH);
            AddSwitchRow(card, "AgreementRatioFallback", "协议按比例盲点（兜底）", "关闭＝只认模板，找不到就不点", 2, 0, 2, SpecRowH);
            AddSwitchRow(card, "AutoClickGameEntry", "自动点「点击进入」", "标题界面自动进去", 2, 1, 2, SpecRowH);
            AddNumRow(card, "MaxRelativeClicks", "最多盲点次数", "协议兜底最多点几下（默认 3）", 3, 0, 2, SpecRowH);
            AddSwitchRow(card, "NewVersionCooldown", "新版冷却", "大版本更新后当天不跑", 3, 1, 2, SpecRowH);
            AddSwitchRow(card, "CooldownOnlyMajor", "仅大版本冷却", "小版本更新不触发冷却", 4, 0, 2, SpecRowH);
            AddTextRow(card, "BetterGiArgs", "BetterGI 参数", "默认 startOneDragon", 4, 1, 2, SpecRowH);
            AddTextRow(card, "GameConfigIni", "config.ini", "原神目录里的配置文件，用来读版本号", 5, 0, 2, SpecRowH, 240);
            AddTextRow(card, "LauncherProcessName", "启动器进程名", "不带 .exe", 5, 1, 2, SpecRowH, 150);
            AddTextRow(card, "GameProcessName", "游戏进程名", "不带 .exe", 6, 0, 2, SpecRowH);
        }

        // ─────────── 关键词页 ───────────

        private static readonly string[][] KwGroups =
        {
            new[] { "KwStart", "开始按钮", "出现这些词判定为「可以开始了」" },
            new[] { "KwUpdate", "更新按钮", "出现这些词判定为「要更新/要下载」" },
            new[] { "KwIgnore", "更新里的忽略词", "含这些词的不算「要更新」（预下载、检查更新…）" },
            new[] { "KwDanger", "危险词（永不点击）", "任何情况下都不点这些（取消、退出、删除…）" },
            new[] { "KwAgree", "同意/接受", "协议弹窗里可以点的确认词" },
            new[] { "KwEntry", "进游戏提示", "标题界面的「点击进入」类文字" },
            new[] { "KwBusy", "进行中（继续等）", "出现这些说明还在下载/安装，只能等" },
            new[] { "KwPause", "暂停", "更新卡住时可以尝试的词" },
            new[] { "KwOtherGame", "别的游戏（别乱点）", "出现这些说明不在原神页面" },
            new[] { "KwError", "错误提示", "出现这些说明这条链路失败了" }
        };

        private void BuildKeywordPage()
        {
            _lblConflict = MkLabel("", 10f, Theme.Warning);
            _lblConflict.SetBounds(0, 0, ContentW, Ui.Px(46));
            (_scroll ?? _pageHost).Controls.Add(_lblConflict);

            int y = Ui.Px(52);
            foreach (var g in KwGroups)
            {
                var card = new FlatCard { BackColor = Color.Transparent };
                card.SetBounds(0, y, ContentW, Ui.Px(164));
                (_scroll ?? _pageHost).Controls.Add(card);

                var title = MkLabel(g[1], 10.5f, Theme.TextPrimary, true);
                var hint = MkLabel(g[2], 8.5f, Theme.TextTertiary);
                var area = new FlatTextArea(string.Join("\r\n", ReadKeywords(g[0])));
                area.TextChanged += (s, e) => { MarkDirty(); CheckKeywordConflicts(); };
                _kw[g[0]] = area;

                card.Controls.Add(title);
                card.Controls.Add(hint);
                card.Controls.Add(area);
                title.Tag = "t"; hint.Tag = "h"; area.Tag = "a";
                title.Name = g[0];

                y += Ui.Px(164 + 10);
            }

            CheckKeywordConflicts();
        }

        private void LayoutKeywordPage()
        {
            if (_scroll == null) return;
            int w = ContentW;
            if (_lblConflict != null) _lblConflict.SetBounds(0, 0, w, Ui.Px(46));

            int y = Ui.Px(52);
            foreach (var card in _scroll.Controls.CastSnapshot().OfType<FlatCard>())
            {
                card.SetBounds(0, y, w, Ui.Px(164));
                foreach (var c in card.Controls.CastSnapshot())
                {
                    var tag = c.Tag as string;
                    if (tag == "t") c.SetBounds(Ui.Px(16), Ui.Px(10), w - Ui.Px(32), Ui.Px(22));
                    else if (tag == "h") c.SetBounds(Ui.Px(16), Ui.Px(32), w - Ui.Px(32), Ui.Px(18));
                    else if (tag == "a") c.SetBounds(Ui.Px(16), Ui.Px(54), w - Ui.Px(34), Ui.Px(98));
                }
                y += Ui.Px(174);
            }
        }

        private void CheckKeywordConflicts()
        {
            if (_lblConflict == null) return;

            var start = KwSet("KwStart");
            var update = KwSet("KwUpdate");
            var ignore = KwSet("KwIgnore");
            var agree = KwSet("KwAgree");
            var entry = KwSet("KwEntry");
            var danger = KwSet("KwDanger");

            var msgs = new List<string>();

            // 危险词优先级最高：只要某个点击词里含危险词，那个点击词就永远点不到
            foreach (var name in new[] { "开始按钮", "更新按钮", "同意", "进入" })
            {
                var set = name == "开始按钮" ? start : name == "更新按钮" ? update : name == "同意" ? agree : entry;
                var hit = SubstringClash(danger, set);
                if (hit.Count > 0) msgs.Add("危险词吞了" + name + "：" + BriefList(hit));
            }

            // 忽略词在匹配前生效：忽略词是某个点击词的子串，那个词就永远匹配不到
            foreach (var name in new[] { "开始按钮", "更新按钮" })
            {
                var set = name == "开始按钮" ? start : update;
                var hit = SubstringClash(ignore, set);
                if (hit.Count > 0) msgs.Add("忽略词吞了" + name + "：" + BriefList(hit));
            }

            // 同一个词既是开始又是更新 → 状态判不出来
            var both = start.Where(w => update.Any(u => u.Contains(w) || w.Contains(u))).ToList();
            if (both.Count > 0) msgs.Add("同一个词既是开始又是更新：" + BriefList(both));

            _lblConflict.Text = msgs.Count == 0
                ? "✔ 关键词表没有冲突"
                : "⚠ " + string.Join("；", msgs);
            _lblConflict.ForeColor = msgs.Count == 0 ? Theme.Success : Theme.Warning;
        }

        private static string BriefList(List<string> items)
        {
            if (items == null || items.Count == 0) return "";
            if (items.Count <= 3) return string.Join("、", items);
            return string.Join("、", items.Take(3)) + " 等 " + items.Count + " 项";
        }

        /// <summary>返回 sub 里被 outer 的某个词包含住的那些词（外层词优先级更高，会把它们吞掉）。</summary>
        private static List<string> SubstringClash(HashSet<string> outer, HashSet<string> sub)
        {
            var hit = new List<string>();
            foreach (var s in sub)
            {
                if (string.IsNullOrEmpty(s)) continue;
                foreach (var o in outer)
                {
                    if (string.IsNullOrEmpty(o) || o.Length < 2) continue;
                    if (s.Contains(o) && s != o) { hit.Add(s + "（含 " + o + "）"); break; }
                    if (s == o) { hit.Add(s); break; }
                }
            }
            return hit;
        }
        private HashSet<string> KwSet(string key)
        {
            var area = _kw.ContainsKey(key) ? _kw[key] : null;
            var raw = area != null ? area.Text : string.Join("\r\n", ReadKeywords(key));
            return new HashSet<string>(SplitKeywords(raw), StringComparer.Ordinal);
        }

        private static string[] SplitKeywords(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new string[0];
            return raw.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        // ─────────── 布局 ───────────

        private void LayoutSpecPage()
        {
            if (_scroll == null) return;
            var cards = _scroll.Controls.CastSnapshot().OfType<FlatCard>().ToList();
            int w = ContentW;

            // 卡片宽度跟窗口走（高度在构造时已按行数算好，这里只改宽）
            foreach (var card in cards) card.SetBounds(0, card.Top, w, card.Height);

            foreach (var r in _rows)
            {
                if (r.Card == null) continue;
                int cw = r.Card.Width;
                int col = 0, cols = 1, fieldWlogical = 96;
                var tag = r.Field.Tag as string;
                if (!string.IsNullOrEmpty(tag) && tag.StartsWith("col"))
                {
                    var parts = tag.Substring(3).Split('/');
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out col);
                    if (parts.Length > 1) int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cols);
                    if (parts.Length > 2) int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out fieldWlogical);
                }
                int colW = (cw - Ui.Px(32)) / Math.Max(1, cols);
                int baseX = Ui.Px(16) + col * colW;
                int fieldW = Ui.Px(fieldWlogical);
                int labelW = colW - fieldW - Ui.Px(16);

                r.Label.SetBounds(baseX, r.Top + (r.Sub == null ? Ui.Px(14) : Ui.Px(6)), labelW, Ui.Px(22));
                if (r.Sub != null) r.Sub.SetBounds(baseX, r.Top + Ui.Px(28), labelW, Ui.Px(18));
                r.Field.SetBounds(baseX + labelW + Ui.Px(8), r.Top + Ui.Px(10), fieldW, Ui.Px(30));
            }

            // 截图页的目录行与快捷操作行单独摆
            LayoutShotExtras();
        }

        private void LayoutShotExtras()
        {
            if (_scroll == null) return;
            foreach (var card in _scroll.Controls.CastSnapshot().OfType<FlatCard>())
            {
                FlatTextBox dirBox = null; FlatButton dirBtn = null;
                FlatButton a1 = null, a2 = null, a3 = null;
                foreach (var c in card.Controls.CastSnapshot())
                {
                    var tag = c.Tag as string;
                    if (tag == "dirBox") dirBox = c as FlatTextBox;
                    else if (tag == "dirBtn") dirBtn = c as FlatButton;
                    else if (tag == "act1") a1 = c as FlatButton;
                    else if (tag == "act2") a2 = c as FlatButton;
                    else if (tag == "act3") a3 = c as FlatButton;
                }
                if (dirBox != null)
                {
                    int w = card.Width;
                    dirBox.SetBounds(Ui.Px(16), Ui.Px(54), w - Ui.Px(16 + 100 + 20), Ui.Px(32));
                    if (dirBtn != null) dirBtn.SetBounds(w - Ui.Px(16 + 92), Ui.Px(54), Ui.Px(92), Ui.Px(32));
                }
                if (a1 != null)
                {
                    int w = card.Width;
                    int y = Ui.Px(56);
                    int bw = Ui.Px(140);
                    a1.SetBounds(Ui.Px(16), y, bw, Ui.Px(32));
                    if (a2 != null) a2.SetBounds(Ui.Px(16) + bw + Ui.Px(10), y, bw, Ui.Px(32));
                    if (a3 != null) a3.SetBounds(Ui.Px(16) + (bw + Ui.Px(10)) * 2, y, bw + Ui.Px(30), Ui.Px(32));
                }
            }
        }

        // ─────────── 读写 ───────────

        private void CollectSpecFields()
        {
            foreach (var kv in _fields)
            {
                var p = typeof(AppConfig).GetProperty(kv.Key);
                if (p == null) continue;
                var text = (kv.Value.Text ?? "").Trim();

                if (p.PropertyType == typeof(string)) { p.SetValue(_cfg, text); continue; }
                if (p.PropertyType == typeof(int))
                {
                    int v;
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                        p.SetValue(_cfg, v);
                    continue;
                }
                if (p.PropertyType == typeof(double))
                {
                    double d;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        p.SetValue(_cfg, d);
                    continue;
                }
            }

            foreach (var kv in _switches)
            {
                var p = typeof(AppConfig).GetProperty(kv.Key);
                if (p != null && p.PropertyType == typeof(bool)) p.SetValue(_cfg, kv.Value.Checked);
            }

            foreach (var kv in _kw)
            {
                var p = typeof(AppConfig).GetProperty(kv.Key);
                if (p != null && p.PropertyType == typeof(string[])) p.SetValue(_cfg, SplitKeywords(kv.Value.Text));
            }
        }

        private string ReadText(string key)
        {
            var p = typeof(AppConfig).GetProperty(key);
            var v = p == null ? null : p.GetValue(_cfg) as string;
            return v ?? "";
        }

        private int ReadNum(string key)
        {
            var p = typeof(AppConfig).GetProperty(key);
            if (p == null) return 0;
            var v = p.GetValue(_cfg);
            return v is int ? (int)v : 0;
        }

        private double ReadDouble(string key)
        {
            var p = typeof(AppConfig).GetProperty(key);
            if (p == null) return 0;
            var v = p.GetValue(_cfg);
            return v is double ? (double)v : 0d;
        }

        private bool ReadBool(string key)
        {
            var p = typeof(AppConfig).GetProperty(key);
            if (p == null) return false;
            var v = p.GetValue(_cfg);
            return v is bool && (bool)v;
        }

        private string[] ReadKeywords(string key)
        {
            var p = typeof(AppConfig).GetProperty(key);
            var v = p == null ? null : p.GetValue(_cfg) as string[];
            return v ?? new string[0];
        }

        // ─────────── 快捷操作 ───────────

        private string ResolveShotDir()
        {
            var dir = (_fields.ContainsKey("ShotDirectory") ? _fields["ShotDirectory"].Text : _cfg.ShotDirectory) ?? "";
            dir = dir.Trim();
            if (dir.Length == 0) dir = System.IO.Path.Combine(AppContext.BaseDirectory, "shots");
            return dir;
        }

        private void OpenFolder(string dir)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
                UpdateStatus("已打开 " + dir, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打不开这个目录：" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CleanupShots()
        {
            var dir = ResolveShotDir();
            if (!System.IO.Directory.Exists(dir))
            {
                MessageBox.Show(this, "截图目录还不存在：\n" + dir, "没有可清理的内容",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int keepDays = Math.Max(1, ParseClamp(_fields.ContainsKey("ShotRetentionDays") ? _fields["ShotRetentionDays"].Text : _cfg.ShotRetentionDays.ToString(), _cfg.ShotRetentionDays, 1, 3650));
            int maxMb = Math.Max(50, ParseClamp(_fields.ContainsKey("ShotMaxTotalMB") ? _fields["ShotMaxTotalMB"].Text : _cfg.ShotMaxTotalMB.ToString(), _cfg.ShotMaxTotalMB, 50, 100000));

            try
            {
                var di = new System.IO.DirectoryInfo(dir);
                var all = di.GetDirectories().OrderByDescending(d => d.CreationTimeUtc).ToList();
                int deleted = 0;
                long freed = 0;

                var cutoff = DateTime.UtcNow.AddDays(-keepDays);
                foreach (var d in all)
                {
                    if (d.CreationTimeUtc >= cutoff) continue;
                    long size = DirSize(d);
                    try { d.Delete(true); deleted++; freed += size; } catch { }
                }

                var left = di.GetDirectories().OrderBy(d => d.CreationTimeUtc).ToList();
                long total = left.Sum(DirSize);
                long limit = (long)maxMb * 1024 * 1024;
                foreach (var d in left)
                {
                    if (total <= limit) break;
                    long size = DirSize(d);
                    try { d.Delete(true); deleted++; freed += size; total -= size; } catch { }
                }

                UpdateStatus("已清理 " + deleted + " 个目录，释放 " + (freed / 1024 / 1024) + " MB", false);
                MessageBox.Show(this, "清理完成：删除 " + deleted + " 个，释放 " + (freed / 1024 / 1024) + " MB。",
                    "清理截图", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "清理失败：" + ex.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static long DirSize(System.IO.DirectoryInfo d)
        {
            try
            {
                long sum = 0;
                foreach (var f in d.GetFiles("*", System.IO.SearchOption.AllDirectories)) sum += f.Length;
                return sum;
            }
            catch { return 0; }
        }
    }

    /// <summary>多行的自绘文本框（关键词表用）。</summary>
    internal sealed class FlatTextArea : Control
    {
        private readonly TextBox _box;
        private bool _focus;

        internal FlatTextArea(string text)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            BackColor = Color.Transparent;

            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Font = Ui.Font(9f),
                BackColor = Theme.FieldBg,
                ForeColor = Theme.TextPrimary,
                Text = text ?? ""
            };
            _box.GotFocus += (s, e) => { _focus = true; Invalidate(); };
            _box.LostFocus += (s, e) => { _focus = false; Invalidate(); };
            _box.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            Controls.Add(_box);
        }

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value; }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool ReadOnlyBox
        {
            get { return _box.ReadOnly; }
            set { _box.ReadOnly = value; _box.TabStop = !value; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Ui.Px(8);
            _box.SetBounds(pad, pad, Math.Max(Ui.Px(20), Width - pad * 2), Math.Max(Ui.Px(20), Height - pad * 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Ui.BackdropOf(this, Theme.Card));

            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = Theme.RoundRect(r, Ui.Px(4)))
            {
                using (var b = new SolidBrush(Theme.FieldBg)) g.FillPath(b, path);
                var border = _focus ? Theme.FieldBorderFocus : Theme.FieldBorder;
                using (var p = new Pen(border, Math.Max(1f, Ui.Px(1)))) g.DrawPath(p, path);
                if (_focus)
                    using (var p = new Pen(Theme.Accent, Math.Max(2f, Ui.Px(2))))
                        g.DrawLine(p, Ui.Px(8), Height - Ui.Px(2), Width - Ui.Px(8), Height - Ui.Px(2));
            }
        }
    }
}
