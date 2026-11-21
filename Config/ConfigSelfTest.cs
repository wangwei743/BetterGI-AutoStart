using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    internal sealed partial class ConfigShellForm
    {
        private System.Windows.Forms.Timer _selfTestTimer;
        private string _selfTestJson;
        private Stopwatch _selfTestWatch;

        /// <summary>
        /// 跑一次 --self-test：提权子进程 + JSON 文件交接（提权后父进程拿不到 stdout）。
        /// </summary>
        private void RunSelfTest()
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "BetterGIAutoStart.exe");
            if (!File.Exists(exe)) exe = Application.ExecutablePath;

            _selfTestJson = Path.Combine(AppContext.BaseDirectory, "ConfigUi.selftest.json");
            try { if (File.Exists(_selfTestJson)) File.Delete(_selfTestJson); } catch { }

            try
            {

                Process.Start(new ProcessStartInfo(exe, "--self-test --self-test-json \"" + _selfTestJson + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动自检失败：" + ex.Message, "自检", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateStatus("正在跑自检……（会在后台提权开一个进程，约 5–20 秒）", false);
            _btnSelfTest.Enabled = false;

            _selfTestWatch = Stopwatch.StartNew();
            if (_selfTestTimer == null)
            {
                _selfTestTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _selfTestTimer.Tick += (s, e) => PollSelfTest();
            }
            _selfTestTimer.Start();
        }

        private void PollSelfTest()
        {
            bool done = _selfTestJson != null && File.Exists(_selfTestJson);
            bool timeout = _selfTestWatch != null && _selfTestWatch.Elapsed.TotalSeconds > 120;
            if (!done && !timeout) return;

            _selfTestTimer.Stop();
            _btnSelfTest.Enabled = true;

            if (!done)
            {
                UpdateStatus("✘ 自检超时：没有拿到结果文件", false, true);
                MessageBox.Show(this,
                    "等了 120 秒还没拿到自检结果。\n\n" +
                    "可能是自检进程被拦住了（或 UAC 弹窗没有确认）。\n" +
                    "可以手动在命令行跑：\n    BetterGIAutoStart.exe --self-test",
                    "自检超时", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string raw = "";
            var facts = new List<KeyValuePair<string, bool?>>();
            bool allOk = true;
            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(_selfTestJson)))
                {
                    var root = doc.RootElement;
                    raw = root.TryGetProperty("text", out var t) ? t.GetString() : "";

                    bool b(string name)
                    {
                        bool v = root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.True;
                        if (!v && name != "admin") allOk = false;
                        return v;
                    }

                    bool admin = b("admin");
                    bool launcher = b("launcher");
                    bool game = b("game");
                    bool bgi = b("bettergi");
                    bool ini = b("ini");
                    string ocr = root.TryGetProperty("ocr", out var o) ? (o.GetString() ?? "") : "";
                    string ver = root.TryGetProperty("gameVersion", out var gv) ? (gv.GetString() ?? "") : "";

                    facts.Add(new KeyValuePair<string, bool?>("启动器 launcher.exe", launcher));
                    facts.Add(new KeyValuePair<string, bool?>("原神 YuanShen.exe", game));
                    facts.Add(new KeyValuePair<string, bool?>("BetterGI.exe", bgi));
                    facts.Add(new KeyValuePair<string, bool?>("原神 config.ini", ini));
                    facts.Add(new KeyValuePair<string, bool?>("管理员权限", admin));
                    facts.Add(new KeyValuePair<string, bool?>("OCR " + (ocr.Length == 0 ? "不可用" : ocr), ocr.Length > 0));
                    facts.Add(new KeyValuePair<string, bool?>("游戏版本 " + (ver.Length == 0 ? "读不到" : ver), ver.Length > 0));
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("✘ 自检结果解析失败：" + ex.Message, false, true);
                return;
            }
            finally
            {
                try { File.Delete(_selfTestJson); } catch { }
            }

            UpdateStatus(allOk ? "✔ 自检通过" : "⚠ 自检有项目不通过（见弹窗）", false, !allOk);
            using (var dlg = new ConfigResultDialog("自检结果",
                       allOk ? "全部通过，可以直接挂计划任务了" : "有项目不通过，先按下面的日志改配置",
                       facts, raw, allOk, () => ExportDiagnostics(raw)))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>把日志、配置、环境、截图打成一个 zip，方便用户反馈。</summary>
        private void ExportDiagnostics(string selfTestRaw)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "保存诊断包";
                dlg.Filter = "Zip 压缩包 (*.zip)|*.zip";
                dlg.FileName = "BetterGIAutoStart_诊断_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                Cursor = Cursors.WaitCursor;
                try
                {
                    int n = ConfigDiagnostics.Export(dlg.FileName, selfTestRaw);
                    Cursor = Cursors.Default;
                    UpdateStatus("✔ 诊断包已导出（" + n + " 项）：" + Path.GetFileName(dlg.FileName), false);
                    MessageBox.Show(this,
                        "诊断包已保存：\r\n" + dlg.FileName + "\r\n\r\n" +
                        "里面是环境信息、自检输出、配置、最近日志和一张屏幕截图，\r\n" +
                        "没有任何账号信息。把它发到仓库 Issues 里就行：\r\n" +
                        "github.com/wangwei743/BetterGI-AutoStart/issues",
                        "诊断包", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    UpdateStatus("✘ 导出失败：" + ex.Message, false, true);
                }
            }
        }
    }
}
