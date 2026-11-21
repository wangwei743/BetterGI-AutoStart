using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>保存前给用户看一眼"到底改了什么"，确认后才写盘。</summary>
    internal sealed class ConfigDiffDialog : Form
    {
        internal ConfigDiffDialog(List<string> lines)
        {
            Theme.Load();
            SuspendLayout();

            Text = "确认保存";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Theme.Card;
            ForeColor = Theme.TextPrimary;
            ClientSize = new Size(Ui.Px(780), Ui.Px(520));

            var title = new Label
            {
                AutoSize = false,
                Text = "即将改动 " + lines.Count + " 项（确认后写入，旧文件会留一份 .bak）",
                ForeColor = Theme.TextPrimary,
                Font = Ui.Font(10.5f, FontStyle.Bold),
                BackColor = Theme.Card
            };
            title.SetBounds(Ui.Px(20), Ui.Px(16), Ui.Px(740), Ui.Px(26));

            var area = new FlatTextArea(string.Join("\r\n", lines)) { ReadOnlyBox = true };
            area.SetBounds(Ui.Px(20), Ui.Px(52), Ui.Px(740), Ui.Px(386));

            var hint = new Label
            {
                AutoSize = false,
                Text = "格式：配置项：  旧值   →   新值",
                ForeColor = Theme.TextTertiary,
                Font = Ui.Font(8.5f),
                BackColor = Theme.Card
            };
            hint.SetBounds(Ui.Px(20), Ui.Px(444), Ui.Px(740), Ui.Px(20));

            var ok = new FlatButton("保存", FlatButton.Kind.Accent) { Height = Ui.Px(32) };
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new FlatButton("取消", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            ok.SetBounds(ClientSize.Width - Ui.Px(20 + 96), Ui.Px(470), Ui.Px(96), Ui.Px(32));
            cancel.SetBounds(ClientSize.Width - Ui.Px(20 + 96 + 8 + 96), Ui.Px(470), Ui.Px(96), Ui.Px(32));

            Controls.AddRange(new Control[] { title, area, hint, cancel, ok });
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }
    }

    /// <summary>自检结果：上面是几条结论徽章，下面是完整自检文本。</summary>
    internal sealed class ConfigResultDialog : Form
    {
        internal ConfigResultDialog(string title, string subtitle,
            IList<KeyValuePair<string, bool?>> facts, string rawText, bool allOk,
            Action onExport = null)
        {
            Theme.Load();
            SuspendLayout();

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Theme.Card;
            ForeColor = Theme.TextPrimary;
            ClientSize = new Size(Ui.Px(840), Ui.Px(640));

            var head = new Label
            {
                AutoSize = false,
                Text = subtitle,
                ForeColor = allOk ? Theme.Success : Theme.Warning,
                Font = Ui.Font(10.5f, FontStyle.Bold),
                BackColor = Theme.Card
            };
            head.SetBounds(Ui.Px(20), Ui.Px(14), Ui.Px(800), Ui.Px(26));
            Controls.Add(head);

            int y = Ui.Px(50);
            foreach (var f in facts)
            {
                string mark = f.Value == null ? "·" : (f.Value.Value ? "✔" : "✘");
                var badge = new InfoBadge(mark + " " + f.Key,
                    f.Value == null ? InfoBadge.Tone.Neutral : (f.Value.Value ? InfoBadge.Tone.Success : InfoBadge.Tone.Danger));
                badge.SetBounds(Ui.Px(20), y, badge.Width, badge.Height);
                Controls.Add(badge);
                y += Ui.Px(34);
            }

            int areaTop = y + Ui.Px(8);
            int areaBottom = ClientSize.Height - Ui.Px(20 + 32 + 14);
            var area = new FlatTextArea(rawText ?? "") { ReadOnlyBox = true };
            area.SetBounds(Ui.Px(20), areaTop, Ui.Px(800), Math.Max(Ui.Px(80), areaBottom - areaTop));
            Controls.Add(area);

            var close = new FlatButton("关闭", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
            close.Click += (s, e) => Close();
            close.SetBounds(ClientSize.Width - Ui.Px(20 + 96), ClientSize.Height - Ui.Px(20 + 32), Ui.Px(96), Ui.Px(32));
            Controls.Add(close);

            // 出问题时要反馈，一键打包比让用户自己找日志靠谱
            if (onExport != null)
            {
                var export = new FlatButton("导出诊断包…", FlatButton.Kind.Standard) { Height = Ui.Px(32) };
                export.Click += (s, e) =>
                {
                    try { onExport(); }
                    catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "诊断包", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                export.SetBounds(ClientSize.Width - Ui.Px(20 + 96 + 12 + 132), ClientSize.Height - Ui.Px(20 + 32), Ui.Px(132), Ui.Px(32));
                Controls.Add(export);
            }

            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }
    }

    /// <summary>试跑前问一句：只跑启动器，还是一直跑到游戏就绪。</summary>
    internal sealed class ConfigPilotDialog : Form
    {
        /// <summary>1 = 只跑启动器；2 = 跑到游戏就绪；0 = 取消。</summary>
        internal int Mode { get; private set; }

        private readonly FlatButton _launcher;
        private readonly FlatButton _gameReady;
        private readonly FlatButton _cancel;
        private readonly Label _hint;

        internal ConfigPilotDialog()
        {
            Theme.Load();
            SuspendLayout();

            Text = "试跑";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Font(9.5f);
            BackColor = Theme.Card;
            ForeColor = Theme.TextPrimary;
            ClientSize = new Size(Ui.Px(560), Ui.Px(300));

            var title = new Label
            {
                AutoSize = false,
                Text = "试跑到哪一步就停？",
                ForeColor = Theme.TextPrimary,
                Font = Ui.Font(11f, FontStyle.Bold),
                BackColor = Theme.Card
            };
            title.SetBounds(Ui.Px(20), Ui.Px(18), Ui.Px(520), Ui.Px(26));

            _launcher = new FlatButton("只跑启动器（--launcher-only）", FlatButton.Kind.Standard);
            _gameReady = new FlatButton("跑到游戏就绪（--stop-before-bettergi）", FlatButton.Kind.Standard);
            _cancel = new FlatButton("取消", FlatButton.Kind.Subtle);

            _hint = new Label
            {
                AutoSize = false,
                ForeColor = Theme.TextSecondary,
                Font = Ui.Font(9f),
                BackColor = Theme.Card,
                Text = "两者都不会启动 BetterGI 一条龙、都不会关机。"
            };

            _launcher.Click += (s, e) => { Mode = 1; DialogResult = DialogResult.OK; Close(); };
            _gameReady.Click += (s, e) => { Mode = 2; DialogResult = DialogResult.OK; Close(); };
            _cancel.Click += (s, e) => { Mode = 0; DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, _launcher, _gameReady, _hint, _cancel });
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
            LayoutSelf();
        }

        private void LayoutSelf()
        {
            int w = ClientSize.Width;
            _launcher.SetBounds(Ui.Px(20), Ui.Px(58), w - Ui.Px(40), Ui.Px(46));
            _gameReady.SetBounds(Ui.Px(20), Ui.Px(118), w - Ui.Px(40), Ui.Px(46));
            _hint.SetBounds(Ui.Px(20), Ui.Px(176), w - Ui.Px(40), Ui.Px(40));
            _cancel.SetBounds(w - Ui.Px(20 + 96), Ui.Px(228), Ui.Px(96), Ui.Px(32));
        }
    }
}