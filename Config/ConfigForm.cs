using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>
    /// P1 骨架窗口：只证明"能开、能读、能存、不碰别的东西"。
    /// 刻意用系统标准控件 + 浅色外观：本机是 150% DPI + Windows 深色主题，
    /// 自绘深色主题下标准控件的文字颜色会被系统主题强制，出现"白底白字"。
    /// P2 再按用户选定的观感统一换皮肤。
    /// </summary>
    internal sealed class ConfigShellForm : Form
    {
        private readonly ConfigSnapshot _snap;
        private readonly TextBox _txtJson;

        private readonly Label _lblStatus;
        private int _demoUptime;
        private bool _dirty;

        internal ConfigShellForm(ConfigSnapshot snap)
        {
            _snap = snap;
            var cfg = snap.Config ?? new AppConfig();

            SuspendLayout();

            Text = "BetterGI-AutoStart · 配置（P1 骨架）";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 680);
            MinimumSize = new Size(720, 520);
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Icon = null;

            // ---- 顶部：配置文件路径 ----
            var lblHead = new Label
            {
                Dock = DockStyle.Top,
                Height = 76,
                Padding = new Padding(12, 10, 12, 4),
                Text = "配置文件：" + Environment.NewLine + snap.Path
                       + (snap.Existed ? "" : Environment.NewLine + "（文件不存在，保存时按默认值生成）")
            };

            // ---- 快速验证 ----
            var boxQuick = new GroupBox
            {
                Text = "快速验证：改一个字段，保存后看预览里的 diff",
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(12, 6, 12, 6)
            };

            _demoUptime = cfg.MinUptimeMinutes;

            var btnDemo = new Button
            {
                Text = "试改一个值并保存（+1）",
                Location = new Point(14, 28),
                Size = new Size(230, 32)
            };
            btnDemo.Click += (s, e) => DemoChangeAndSave();

            var lblHint = new Label
            {
                Text = "改的是 MinUptimeMinutes；预览里应该只有这一行变，并留一份 .bak 备份",
                AutoSize = true,
                Location = new Point(262, 36),
                ForeColor = Color.DimGray
            };

            boxQuick.Controls.Add(btnDemo);
            boxQuick.Controls.Add(lblHint);

            // ---- 配置预览 ----
            var boxJson = new GroupBox
            {
                Text = "当前配置内容（只读预览；保存后自动刷新）",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 6, 10, 10)
            };

            _txtJson = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(20, 20, 20),
                TabStop = false
            };
            boxJson.Controls.Add(_txtJson);

            // ---- 底部状态与按钮 ----
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 10, 12, 10) };

            _lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Left,
                Width = 620,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var btnClose = new Button { Text = "关闭", Dock = DockStyle.Right, Width = 90 };
            btnClose.Click += (s, e) => Close();

            var btnFolder = new Button { Text = "打开配置文件夹", Dock = DockStyle.Right, Width = 130 };
            btnFolder.Click += (s, e) => OpenFolder();


            panelBottom.Controls.Add(_lblStatus);
            panelBottom.Controls.Add(btnClose);
            panelBottom.Controls.Add(btnFolder);


            Controls.Add(boxJson);
            Controls.Add(boxQuick);
            Controls.Add(lblHead);
            Controls.Add(panelBottom);

            ResumeLayout(false);

            Load += (s, e) =>
            {
                _txtJson.Text = _snap.Existed && !string.IsNullOrEmpty(_snap.OriginalText)
                    ? _snap.OriginalText
                    : ConfigStore.Serialize(_snap.Root);
                _txtJson.Select(0, 0);
                if (!string.IsNullOrEmpty(_snap.ParseError))
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "⚠ " + _snap.ParseError + "（保存会以默认值覆盖，注意备份）";
                }
                else
                {
                    UpdateStatus();
                }
            };
        }

        private void UpdateStatus()
        {
            _lblStatus.ForeColor = _dirty ? Color.DarkOrange : Color.DimGray;
            _lblStatus.Text = _dirty ? "● 有未保存的修改" : "已加载，无修改";
        }

        private void OpenFolder()
        {
            try
            {
                var dir = Path.GetDirectoryName(_snap.Path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开文件夹失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>P1 演示：把 MinUptimeMinutes 加 1 后保存，刷新预览以证明"只有这一行变"</summary>
        private void DemoChangeAndSave()
        {
            var cfg = _snap.Config ?? new AppConfig();
            _demoUptime = _demoUptime >= 240 ? 0 : _demoUptime + 1;
            cfg.MinUptimeMinutes = _demoUptime;

            var r = ConfigStore.Save(_snap, cfg);
            if (r.Ok)
            {
                _dirty = false;
                _txtJson.Text = _snap.OriginalText;
                _txtJson.Select(0, 0);
                _lblStatus.ForeColor = Color.SeaGreen;
                _lblStatus.Text = "✔ 已保存"
                    + (string.IsNullOrEmpty(r.BackupPath) ? "" : "（备份：" + Path.GetFileName(r.BackupPath) + "）");
            }
            else
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "✘ " + r.Message;
                MessageBox.Show(this, r.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class ConfigUi
    {
        // 只在 --config 入口把进程设成 per-monitor v2，让 WinForms 按实际 DPI 渲染文字。
        // 不带 --config 的主流程在调用本类之前就已返回，行为不受影响。
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        /// <summary>--config 入口：初始化 WinForms 后显示配置窗口，返回进程退出码</summary>
        internal static int Run()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var snap = ConfigStore.Load(ConfigStore.DefaultPath);
            using (var form = new ConfigShellForm(snap))
            {
                Application.Run(form);
            }
            return 0;
        }
    }
}
