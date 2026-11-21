using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using WinStorageFile = Windows.Storage.StorageFile;

namespace BetterGIAutoStart
{
    #region 数据模型

    internal sealed class WinInfo
    {
        public IntPtr Handle;
        public int Pid;
        public string Proc = "";
        public string Title = "";
        public string Class = "";
        public string ExePath = "";
        public int Width;
        public int Height;
    }

    internal sealed class Shot
    {
        public string Path = "";
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    internal sealed class OcrLine
    {
        public string Text = "";
        public double X;
        public double Y;
        public double W;
        public double H;
        public double Area { get { return W * H; } }
    }

    internal enum LauncherKind { Start, Update, Timeout, OtherGame }

    internal sealed class LauncherState
    {
        public LauncherKind Kind = LauncherKind.Timeout;
        public OcrLine Line;
        public string Note = "";
    }


    internal sealed class MatchResult
    {
        public double Score;
        public int X, Y, W, H;
        public double Scale;
    }

    /// <summary>
    /// 模板匹配（BetterGI 同款思路）：只按图像找位置，不用任何绝对坐标。
    /// 模板存放在 exe 目录下的 templates\，按参考客户区尺寸做多尺度匹配。
    /// </summary>
    internal static class TemplateEngine
    {
        public static string Dir = "";
        public static int RefW = 1920;
        public static int RefH = 1080;

        public static double[] ScalesFor(int clientWidth)
        {
            double r = clientWidth / (double)Math.Max(1, RefW);
            var list = new List<double>();
            foreach (var k in new[] { 0.85, 0.9, 0.95, 1.0, 1.05, 1.1, 1.15 })
            {
                double s = r * k;
                if (s > 0.2 && s < 4) list.Add(s);
            }
            return list.ToArray();
        }

        public static MatchResult Find(string imagePath, string name, double threshold, double[] scales)
        {
            try
            {
                if (!File.Exists(imagePath)) return null;

                int imgW = 0, imgH = 0;
                try
                {
                    using (var bmp = new Bitmap(imagePath)) { imgW = bmp.Width; imgH = bmp.Height; }
                }
                catch { return null; }

                // 不同分辨率下启动器布局不一样，优先用同尺寸采到的模板
                var sizedPath = Path.Combine(Dir, name + "_" + imgW + "x" + imgH + ".png");
                var basePath = Path.Combine(Dir, name + ".png");
                bool useSized = File.Exists(sizedPath);
                var tplPath = useSized ? sizedPath : basePath;
                if (!File.Exists(tplPath)) return null;

                if (useSized)
                {
                    scales = new[] { 1.0, 0.97, 1.03 };
                }
                else
                {
                    // 通用模板可能在不同分辨率下采集，做宽范围缩放搜索
                    scales = new[] { 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.4, 1.6, 1.8, 2.0 };
                }

                using (var src = new OpenCvSharp.Mat(imagePath, OpenCvSharp.ImreadModes.Grayscale))
                using (var tpl0 = new OpenCvSharp.Mat(tplPath, OpenCvSharp.ImreadModes.Grayscale))
                {
                    if (src.Empty() || tpl0.Empty()) return null;

                    MatchResult best = null;
                    foreach (var s in scales)
                    {
                        int tw = (int)Math.Round(tpl0.Width * s);
                        int th = (int)Math.Round(tpl0.Height * s);
                        if (tw < 8 || th < 8 || tw >= src.Width || th >= src.Height) continue;

                        using (var tpl = new OpenCvSharp.Mat())
                        using (var res = new OpenCvSharp.Mat())
                        {
                            OpenCvSharp.Cv2.Resize(tpl0, tpl, new OpenCvSharp.Size(tw, th), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                            OpenCvSharp.Cv2.MatchTemplate(src, tpl, res, OpenCvSharp.TemplateMatchModes.CCoeffNormed);
                            OpenCvSharp.Cv2.MinMaxLoc(res, out double minVal, out double maxVal, out OpenCvSharp.Point minLoc, out OpenCvSharp.Point maxLoc);
                            if (best == null || maxVal > best.Score)
                            {
                                best = new MatchResult { Score = maxVal, X = maxLoc.X, Y = maxLoc.Y, W = tw, H = th, Scale = s };
                            }
                        }
                    }
                    if (best != null && best.Score < threshold)
                    {
                        Program.Log("模板 " + name + (useSized ? "(尺寸版)" : "") + " 最高分只有 " + best.Score.ToString("0.00"));
                    }
                    return best;
                }
            }
            catch (Exception ex)
            {
                Program.Log("模板匹配异常(" + name + "): " + ex.Message);
                return null;
            }
        }
    }

    #endregion

    #region Win32

    internal static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }

    #endregion

    internal static class Program
    {
        #region 字段

        // 版本号只在这里改；命名空间/程序集版本在 csproj 里同步
        private static readonly string Version =
            typeof(Program).Assembly.GetName().Version.ToString(3);
        private static AppConfig Cfg;
        private static string LogDir = "";
        private static string LogFile = "";
        private static string ShotDir = "";
        private static string RunShotDir = "";
        private static string RunKind = "run";
        private static OcrEngine Ocr;
        private static bool DryRun;
        private static bool StopBeforeBetterGi;
        private static bool ForceRun;
        private static bool SkipUptime;
        private static bool SkippedToday;
        private static bool BetterGiStarted;
        private static bool FallbackScheduled;
        private static bool WatchdogFired;

        #endregion

        #region 入口

        [STAThread] // 配置界面要用文件对话框，必须是 STA 线程；对控制台流程无影响
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            Cfg = LoadConfig();

            // 配置界面：在 InitPaths/提权/OCR 之前返回，不建目录、不写日志、不提权
            if (HasArg(args, "--config")) return ConfigUi.Run();

            // 诊断包：只读（日志尾部、配置、环境、模板清单、截图），给用户反馈问题时用
            string diagPath = ArgValue(args, "--diagnostics");
            if (!string.IsNullOrEmpty(diagPath))
            {
                try
                {
                    int n = ConfigDiagnostics.Export(diagPath, null);
                    Console.WriteLine("诊断包已写出：" + diagPath + "（" + n + " 项）");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("导出诊断包失败：" + ex.Message);
                    return 1;
                }
            }

            DryRun = HasArg(args, "--dry-run");
            bool selfTest = HasArg(args, "--self-test");
            string selfTestJson = ArgValue(args, "--self-test-json"); // 自检结果写文件（给配置界面读）
            bool calibrate = HasArg(args, "--calibrate");
            string testWindow = ArgValue(args, "--test-window");
            StopBeforeBetterGi = HasArg(args, "--stop-before-bettergi");
            ForceRun = HasArg(args, "--force");
            SkipUptime = HasArg(args, "--skip-uptime");
            RunKind = (selfTest || calibrate || !string.IsNullOrEmpty(testWindow) || DryRun) ? "manual" : "run";

            InitPaths();

            // 除演练和纯离线模板匹配外，一律提权运行：
            // WinRT OCR 在受限令牌下会拿不到文件访问权限（实测），提权后正常
            var matchArg = ArgValue(args, "--match-file");
            if (!IsAdmin() && !DryRun && string.IsNullOrEmpty(matchArg))
            {
                Log("非管理员权限，尝试提权重启……");
                if (RelaunchElevated(args)) return 0;
                Log("提权失败");
                return 2;
            }

            InitOcr();

            var matchFile = ArgValue(args, "--match-file");
            if (!string.IsNullOrEmpty(matchFile))
            {
                var name = ArgValue(args, "--match-name");
                return MatchFileAndExit(matchFile, name);
            }

            if (HasArg(args, "--launcher-only"))
            {
                Log("=== 仅启动器测试模式 ===");
                KillLauncherFamily();
                Thread.Sleep(1500);
                var lwTest = StartLauncherAndWait();
                if (lwTest != null) Log("结果: 启动器窗口成功出现 " + lwTest.Width + "x" + lwTest.Height);
                else Log("结果: 启动器窗口未出现（进程状态: " + DescribeLauncherProcesses() + "）");
                return lwTest != null ? 0 : 2;
            }

            var clickTpl = ArgValue(args, "--click-template");
            if (!string.IsNullOrEmpty(clickTpl))
            {
                return ClickTemplateAndExit(clickTpl);
            }

            var clickClient = ArgValue(args, "--click-client");
            if (!string.IsNullOrEmpty(clickClient))
            {
                return ClickClientAndExit(clickClient);
            }

            if (selfTest)
            {
                SelfTest();
                TryFinalizeRunShots();
                if (!string.IsNullOrEmpty(selfTestJson)) WriteSelfTestJson(selfTestJson);
                return 0;
            }
            if (!string.IsNullOrEmpty(testWindow))
            {
                TestWindow(testWindow);
                TryFinalizeRunShots();
                return 0;
            }
            if (calibrate)
            {
                Calibrate();
                TryFinalizeRunShots();
                return 0;
            }

            Log("==================================================");
            Log("BetterGIAutoStart v" + Version + "  .NET " + Environment.Version);
            Log("配置: " + ConfigPath());
            Log("管理员: " + IsAdmin() + "  DryRun: " + DryRun);



            ClearStaleShutdownMarker();

            int code = 2;
            try
            {
                RunFlow();
                code = (BetterGiStarted || DryRun) ? 0 : 2;
            }
            catch (Exception ex)
            {
                Log("错误: " + ex.Message);
                Log(ex.StackTrace);
            }
            finally
            {
                if (BetterGiStarted)
                {
                    Log("BetterGI 已启动，本程序退出，不执行任何关机");
                    code = 0;
                }
                else if (DryRun)
                {
                    Log("[演练] 不执行关机");
                }
                else if (SkipTodayDone())
                {
                    code = 0;
                }
                else if (StopBeforeBetterGi)
                {
                    Log("已按要求停在 BetterGI 之前，视为正常结束，不关机");
                    code = 0;
                }
                else
                {
                    ScheduleFallbackShutdown("未启动 BetterGI");
                    code = 2;
                }
                TryFinalizeRunShots();
                Log("==================================================");
            }
            return code;
        }

        #endregion

        #region 配置 / 日志

        private static string ConfigPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "BetterGIAutoStart.config.json");
        }

        private static AppConfig LoadConfig()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "BetterGIAutoStart.config.json");
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("读取配置失败，使用默认值: " + ex.Message);
            }

            var def = new AppConfig();
            try
            {
                var opt = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(path, JsonSerializer.Serialize(def, opt), new UTF8Encoding(true));
            }
            catch { }
            return def;
        }

        private static void InitPaths()
        {
            // 截图默认放 exe 同目录的 shots\，方便排查；目录不可写时退回 %TEMP%
            ShotDir = string.IsNullOrWhiteSpace(Cfg.ShotDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "shots")
                : Cfg.ShotDirectory;
            try
            {
                Directory.CreateDirectory(ShotDir);
            }
            catch
            {
                ShotDir = Path.Combine(Path.GetTempPath(), "bettergi_autostart_shots");
                try { Directory.CreateDirectory(ShotDir); } catch { }
            }

            LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
            try
            {
                Directory.CreateDirectory(LogDir);
                LogFile = Path.Combine(LogDir, "BetterGIAutoStart_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch
            {
                LogDir = Path.Combine(Path.GetTempPath(), "bettergi_autostart_logs");
                try { Directory.CreateDirectory(LogDir); } catch { }
                LogFile = Path.Combine(LogDir, "BetterGIAutoStart_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }

            CleanupShots();

            TemplateEngine.Dir = Path.Combine(AppContext.BaseDirectory, "templates");
            try { Directory.CreateDirectory(TemplateEngine.Dir); } catch { }

            RunShotDir = Path.Combine(ShotDir, (RunKind == "manual" ? "manual_" : "") + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try { Directory.CreateDirectory(RunShotDir); } catch { RunShotDir = ShotDir; }
        }

        /// <summary>
        /// 截图清理：默认保留 30 天，且每个月只清理一次（留足排故障的历史）；
        /// 另外任何时候目录超过容量上限，都会从最旧的开始删，防止意外堆满。
        /// </summary>
        private static void CleanupShots()
        {
            try
            {
                if (string.IsNullOrEmpty(ShotDir) || !Directory.Exists(ShotDir)) return;

                int keepDays = Cfg == null ? 30 : Math.Max(1, Cfg.ShotRetentionDays);
                int maxMB = Cfg == null ? 500 : Math.Max(10, Cfg.ShotMaxTotalMB);
                string marker = Path.Combine(ShotDir, ".last_cleanup");
                string monthKey = DateTime.Now.ToString("yyyy-MM");

                int deleted = 0;
                long freed = 0;

                bool monthChanged = true;
                try
                {
                    if (File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), monthKey, StringComparison.Ordinal))
                    {
                        monthChanged = false;
                    }
                }
                catch { }

                // 每月一次的按天清理
                if (monthChanged)
                {
                    var cutoff = DateTime.Now.AddDays(-keepDays);
                    foreach (var d in new DirectoryInfo(ShotDir).GetDirectories())
                    {
                        try
                        {
                            if (d.LastWriteTime < cutoff)
                            {
                                d.Delete(true);
                                deleted++;
                            }
                        }
                        catch { }
                    }
                    foreach (var f in new DirectoryInfo(ShotDir).GetFiles())
                    {
                        try
                        {
                            if (f.Name == ".last_cleanup") continue;
                            if (f.LastWriteTime < cutoff)
                            {
                                long len = f.Length;
                                f.Delete();
                                deleted++;
                                freed += len;
                            }
                        }
                        catch { }
                    }
                    try { File.WriteAllText(marker, monthKey, new UTF8Encoding(false)); } catch { }
                    if (deleted > 0)
                    {
                        Log("截图月度清理: 删除 " + deleted + " 个超过 " + keepDays + " 天的文件，释放 " + Math.Round(freed / 1024.0 / 1024.0, 1) + " MB");
                    }
                }

                // 容量兜底：超上限就从最旧的删
                var rest = new DirectoryInfo(ShotDir).GetFiles()
                    .Where(f => f.Name != ".last_cleanup")
                    .OrderBy(f => f.LastWriteTime).ToList();
                long total = 0;
                foreach (var f in rest) total += f.Length;
                long limit = (long)maxMB * 1024L * 1024L;
                int overDeleted = 0;
                long overFreed = 0;
                foreach (var f in rest)
                {
                    if (total <= limit) break;
                    try
                    {
                        long len = f.Length;
                        f.Delete();
                        overDeleted++;
                        overFreed += len;
                        total -= len;
                    }
                    catch { }
                }
                if (overDeleted > 0)
                {
                    Log("截图容量超限(" + maxMB + "MB): 删除 " + overDeleted + " 个最旧文件，释放 " + Math.Round(overFreed / 1024.0 / 1024.0, 1) + " MB");
                }
            }
            catch { }
        }
        internal static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
            try { Console.WriteLine(line); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(LogFile))
                    File.AppendAllText(LogFile, line + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }

        private static void LogBlank()
        {
            try { Console.WriteLine(); } catch { }
        }

        #endregion

        #region OCR 初始化

        private static void InitOcr()
        {
            try
            {
                OcrEngine engine = null;
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                {
                    if (lang.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        engine = OcrEngine.TryCreateFromLanguage(lang);
                        if (engine != null) break;
                    }
                }
                if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();
                Ocr = engine;
                if (Ocr != null)
                    Log("OCR 就绪: " + Ocr.RecognizerLanguage.LanguageTag);
                else
                    Log("OCR 不可用：系统没有可用的 OCR 语言包");
            }
            catch (Exception ex)
            {
                Log("OCR 初始化失败: " + ex.Message);
            }
        }

        private static T Await<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            var task = op.AsTask();
            if (!task.Wait(20000)) throw new TimeoutException("WinRT 操作超时");
            return task.Result;
        }

        #endregion

        #region 窗口

        private static List<WinInfo> EnumTopWindows(bool visibleOnly = true)
        {
            var list = new List<WinInfo>();
            try
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    try
                    {
                        if (visibleOnly && !Native.IsWindowVisible(h)) return true;
                        var sb = new StringBuilder(512);
                        Native.GetWindowText(h, sb, sb.Capacity);
                        string title = sb.ToString();
                        var cn = new StringBuilder(256);
                        Native.GetClassName(h, cn, cn.Capacity);
                        uint pid;
                        Native.GetWindowThreadProcessId(h, out pid);

                        var info = new WinInfo();
                        info.Handle = h;
                        info.Pid = (int)pid;
                        info.Title = title;
                        info.Class = cn.ToString();
                        try
                        {
                            var p = Process.GetProcessById((int)pid);
                            info.Proc = p.ProcessName;
                            try { info.ExePath = p.MainModule.FileName; } catch { }
                        }
                        catch { }

                        Native.RECT rc;
                        if (Native.GetClientRect(h, out rc))
                        {
                            info.Width = rc.Right - rc.Left;
                            info.Height = rc.Bottom - rc.Top;
                        }
                        if (info.Width >= 200 && info.Height >= 150) list.Add(info);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        private static WinInfo FindWindowByProc(string procName, int minWidth, int minHeight)
        {
            foreach (var w in EnumTopWindows())
            {
                if (!string.Equals(w.Proc, procName, StringComparison.OrdinalIgnoreCase)) continue;
                if (w.Width < minWidth || w.Height < minHeight) continue;
                return w;
            }
            return null;
        }

        private static WinInfo FindLauncherWindow()
        {
            string launcherDir = "";
            try { launcherDir = Path.GetDirectoryName(Cfg.LauncherPath); } catch { }

            foreach (var w in EnumTopWindows())
            {
                if (w.Width < 300 || w.Height < 200) continue;
                if (string.Equals(w.Proc, Cfg.GameProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                bool hit = false;
                if (!string.IsNullOrEmpty(Cfg.LauncherProcessName) && string.Equals(w.Proc, Cfg.LauncherProcessName, StringComparison.OrdinalIgnoreCase)) hit = true;
                if (!hit && (string.Equals(w.Proc, "HYP", StringComparison.OrdinalIgnoreCase) || string.Equals(w.Proc, "HoYoPlay", StringComparison.OrdinalIgnoreCase))) hit = true;
                if (!hit && !string.IsNullOrEmpty(launcherDir) && !string.IsNullOrEmpty(w.ExePath) &&
                    w.ExePath.StartsWith(launcherDir, StringComparison.OrdinalIgnoreCase)) hit = true;
                if (!hit && (w.Title.Contains("米哈游启动器") || w.Title.Contains("HoYoPlay"))) hit = true;

                if (hit) return w;
            }
            return null;
        }

        private static WinInfo FindGameWindow()
        {
            foreach (var w in EnumTopWindows())
            {
                if (!string.Equals(w.Proc, Cfg.GameProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                if (w.Width < 400 || w.Height < 300) continue;
                return w;
            }
            return null;
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                var arr = Process.GetProcessesByName(name);
                return arr.Length > 0;
            }
            catch { return false; }
        }

        private static WinInfo WaitWindow(Func<WinInfo> finder, int timeoutSeconds, Action<int> tick)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            int n = 0;
            while (DateTime.Now < deadline)
            {
                var w = finder();
                if (w != null) return w;
                n++;
                if (tick != null && n % 10 == 0) tick(n);
                Thread.Sleep(2000);
            }
            return null;
        }

        #endregion

        #region 截图 / OCR

        private static bool EnsureForeground(IntPtr h)
        {
            try
            {
                if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
                Native.ShowWindow(h, 5);
                Native.BringWindowToTop(h);
                Native.SetForegroundWindow(h);
                Thread.Sleep(300);
                if (Native.GetForegroundWindow() == h) return true;

                Native.SetForegroundWindow(h);
                Thread.Sleep(300);
                if (Native.GetForegroundWindow() == h) return true;

                // SetForegroundWindow 被前台锁挡掉时，用最小化+还原强制抢焦点（实测有效）
                Log("窗口抢不到前台，最小化/还原强制切换");
                Native.ShowWindow(h, 6);   // SW_MINIMIZE
                Thread.Sleep(400);
                Native.ShowWindow(h, 9);   // SW_RESTORE
                Thread.Sleep(600);
                Native.SetForegroundWindow(h);
                Thread.Sleep(300);
                return Native.GetForegroundWindow() == h;
            }
            catch { return false; }
        }

        private static Shot Capture(IntPtr h, string name)
        {
            try
            {
                Native.RECT rc;
                if (!Native.GetClientRect(h, out rc)) { Log("截图失败: GetClientRect 返回 false"); return null; }
                int w = rc.Right - rc.Left;
                int hgt = rc.Bottom - rc.Top;
                if (w <= 10 || hgt <= 10) { Log("截图失败: 客户区过小 " + w + "x" + hgt); return null; }

                var origin = new Native.POINT();
                origin.X = 0;
                origin.Y = 0;
                if (!Native.ClientToScreen(h, ref origin)) { Log("截图失败: ClientToScreen 返回 false"); return null; }

                Bitmap bmp = null;
                try
                {
                    bmp = new Bitmap(w, hgt);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(w, hgt));
                    }
                }
                catch (Exception ex)
                {
                    Log("截图失败(CopyFromScreen): " + ex.GetType().Name + " " + ex.Message + " 区域 " + w + "x" + hgt + " @" + origin.X + "," + origin.Y);
                    if (bmp != null) { bmp.Dispose(); bmp = null; }
                }

                if (bmp == null || IsMostlyBlack(bmp))
                {
                    var alt = CaptureByPrintWindow(h, w, hgt);
                    if (alt != null)
                    {
                        if (bmp != null) bmp.Dispose();
                        bmp = alt;
                        Log("截图方式切换为 PrintWindow");
                    }
                    else if (bmp == null)
                    {
                        return null;
                    }
                }

                if (IsMostlyBlack(bmp))
                {
                    Log("警告：截到的画面接近全黑——可能是锁屏、显示器休眠、会话已断开或游戏独占全屏。虚显器场景请确认电源计划里“关闭显示器”不会让画面停止刷新。");
                }

                string path = Path.Combine(RunShotDir, name + ".png");
                if (!TrySaveBitmap(bmp, path))
                {
                    path = Path.Combine(RunShotDir, name + "_" + DateTime.Now.ToString("HHmmss_fff") + ".png");
                    if (!TrySaveBitmap(bmp, path))
                    {
                        bmp.Dispose();
                        return null;
                    }
                    CleanupOldShots(name);
                }
                bmp.Dispose();

                var shot = new Shot();
                shot.Path = path;
                shot.X = origin.X;
                shot.Y = origin.Y;
                shot.W = w;
                shot.H = hgt;
                return shot;
            }
            catch (Exception ex)
            {
                Log("截图失败: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        private static Bitmap CaptureByPrintWindow(IntPtr h, int w, int hgt)
        {
            try
            {
                var bmp = new Bitmap(w, hgt);
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    bool ok = false;
                    try { ok = Native.PrintWindow(h, hdc, 2); }
                    finally { g.ReleaseHdc(hdc); }
                    if (!ok)
                    {
                        bmp.Dispose();
                        return null;
                    }
                }
                return bmp;
            }
            catch (Exception ex)
            {
                Log("PrintWindow 截图失败: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        private static bool TrySaveBitmap(Bitmap bmp, string path)
        {
            try
            {
                bmp.Save(path, ImageFormat.Png);
                return true;
            }
            catch (Exception ex)
            {
                Log("保存截图失败(" + Path.GetFileName(path) + "): " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool IsMostlyBlack(Bitmap bmp)
        {
            try
            {
                int step = Math.Max(1, bmp.Width / 32);
                int dark = 0;
                int total = 0;
                for (int y = 0; y < bmp.Height; y += step)
                {
                    for (int x = 0; x < bmp.Width; x += step)
                    {
                        Color c = bmp.GetPixel(x, y);
                        total++;
                        if (c.R < 8 && c.G < 8 && c.B < 8) dark++;
                    }
                }
                return total > 0 && dark > total * 98 / 100;
            }
            catch { return false; }
        }

        private static void CleanupOldShots(string prefix)
        {
            try
            {
                foreach (var f in Directory.GetFiles(RunShotDir, prefix + "_*.png"))
                {
                    var fi = new FileInfo(f);
                    if ((DateTime.Now - fi.LastWriteTime).TotalMinutes > 10)
                    {
                        try { fi.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }
        private static Shot CaptureForOcr(IntPtr h, string name)
        {
            if (!EnsureForeground(h))
            {
                Log("窗口不在前台，跳过本次截图（避免截到别的窗口）: " + name);
                return null;
            }
            return Capture(h, name);
        }
        /// <summary>
        /// 在截图里按比例裁一块、放大 scale 倍再 OCR，坐标换算回整张客户区。
        /// 启动器主按钮是「白字压黄底」，整图 OCR 容易吞字（实测「开始游戏」读成「开始游」），
        /// 裁右下角放大后能完整读出来。
        /// </summary>
        private static List<OcrLine> OcrRegion(Shot shot, double rx0, double ry0, double rx1, double ry1, int scale, string name)
        {
            var result = new List<OcrLine>();
            if (shot == null || string.IsNullOrEmpty(shot.Path) || !File.Exists(shot.Path)) return result;
            try
            {
                using (var src = new Bitmap(shot.Path))
                {
                    int x0 = (int)(src.Width * rx0);
                    int y0 = (int)(src.Height * ry0);
                    int w = (int)(src.Width * (rx1 - rx0));
                    int h = (int)(src.Height * (ry1 - ry0));
                    if (x0 < 0) x0 = 0;
                    if (y0 < 0) y0 = 0;
                    if (x0 + w > src.Width) w = src.Width - x0;
                    if (y0 + h > src.Height) h = src.Height - y0;
                    if (w <= 8 || h <= 8) return result;

                    using (var crop = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(crop))
                        {
                            g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x0, y0, w, h), GraphicsUnit.Pixel);
                        }
                        int bw = w * scale;
                        int bh = h * scale;
                        using (var big = new Bitmap(bw, bh))
                        {
                            using (var g2 = Graphics.FromImage(big))
                            {
                                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g2.DrawImage(crop, 0, 0, bw, bh);
                            }
                            string path = Path.Combine(RunShotDir, name + ".png");
                            big.Save(path, ImageFormat.Png);

                            var fake = new Shot();
                            fake.Path = path;
                            fake.W = bw;
                            fake.H = bh;
                            var lines = OcrShot(fake);
                            foreach (var l in lines)
                            {
                                l.X = l.X / scale + x0;
                                l.Y = l.Y / scale + y0;
                                l.W = l.W / scale;
                                l.H = l.H / scale;
                                result.Add(l);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("区域 OCR 失败: " + ex.Message);
            }
            return result;
        }

        /// <summary>按模板找按钮；返回的 box 可直接用于点击（坐标来自匹配结果）</summary>
        private static OcrLine FindByTemplate(Shot shot, string[] names, double threshold, out string matchedName, out double score)
        {
            matchedName = null;
            score = 0;
            if (shot == null) return null;
            foreach (var n in names)
            {
                if (!File.Exists(Path.Combine(TemplateEngine.Dir, n + ".png"))) continue;
                var m = TemplateEngine.Find(shot.Path, n, threshold, null);
                if (m != null && m.Score >= threshold && m.Score > score)
                {
                    score = m.Score;
                    matchedName = n;
                    var line = new OcrLine();
                    line.Text = n;
                    line.X = m.X;
                    line.Y = m.Y;
                    line.W = m.W;
                    line.H = m.H;
                    return line;
                }
            }
            return null;
        }

        /// <summary>对一个已定位的按钮区域再 OCR，读它上面的真实文字（用来区分"立即更新"和"更新游戏"）</summary>
        private static string OcrButtonLabel(Shot shot, OcrLine line)
        {
            try
            {
                if (shot == null || line == null) return "";
                double rx0 = Math.Max(0, (line.X - 8) / shot.W);
                double ry0 = Math.Max(0, (line.Y - 6) / shot.H);
                double rx1 = Math.Min(1, (line.X + line.W + 8) / shot.W);
                double ry1 = Math.Min(1, (line.Y + line.H + 6) / shot.H);
                var parts = OcrRegion(shot, rx0, ry0, rx1, ry1, 2, "btn_label");
                var sb = new StringBuilder();
                foreach (var l in parts) sb.Append(l.Text);
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>先整图找主按钮；找不到再裁右下角放大找</summary>
        private static void FindLauncherButtons(List<OcrLine> lines, Shot shot, out OcrLine start, out OcrLine update)
        {
            start = FindLine(lines, Cfg.KwStart, Cfg.KwDanger, Cfg.KwIgnore);
            update = FindLine(lines, Cfg.KwUpdate, Cfg.KwDanger, Cfg.KwIgnore);
            if (start != null || update != null) return;

            var btnLines = OcrRegion(shot, 0.50, 0.60, 0.95, 1.0, 2, "launcher_btn");
            if (btnLines.Count == 0) return;

            start = FindLine(btnLines, Cfg.KwStart, Cfg.KwDanger, Cfg.KwIgnore);
            update = FindLine(btnLines, Cfg.KwUpdate, Cfg.KwDanger, Cfg.KwIgnore);
            if (start != null || update != null)
            {
                Log("整图没认出主按钮，右下角放大后识别成功");
            }
        }

        private static List<OcrLine> OcrShot(Shot shot)
        {
            var result = new List<OcrLine>();
            if (Ocr == null || shot == null) return result;
            try
            {
                return OcrFromPath(shot.Path);
            }
            catch (Exception ex)
            {
                // WinRT 的 StorageFile 对某些目录会拒绝访问（错误信息里提 StorageApplicationPermissions），
                // 复制到临时目录再读一次
                // 依次尝试：进程 TEMP → 程序目录内临时文件
                var candidates = new List<string>();
                try { candidates.Add(Path.Combine(Path.GetTempPath(), "autostart_ocr_" + Guid.NewGuid().ToString("N") + ".png")); } catch { }
                try
                {
                    var localDir = Path.Combine(AppContext.BaseDirectory, "_ocr_tmp");
                    Directory.CreateDirectory(localDir);
                    candidates.Add(Path.Combine(localDir, "ocr_" + Guid.NewGuid().ToString("N") + ".png"));
                }
                catch { }

                foreach (var tmp in candidates)
                {
                    try
                    {
                        File.Copy(shot.Path, tmp, true);
                        var retry = OcrFromPath(tmp);
                        try { File.Delete(tmp); } catch { }
                        if (retry.Count > 0) return retry;
                    }
                    catch (Exception ex2)
                    {
                        Log("OCR 复制到 " + Path.GetDirectoryName(tmp) + " 失败: " + ex2.Message);
                    }
                }

                Log("OCR 失败(原始): " + ex.ToString().Replace(Environment.NewLine, " | "));
                return result;
            }
        }

        private static List<OcrLine> OcrFromPath(string path)
        {
            var result = new List<OcrLine>();
            if (Ocr == null) return result;
            try
            {
                var file = Await(WinStorageFile.GetFileFromPathAsync(path));
                using (var stream = Await(file.OpenAsync(FileAccessMode.Read)))
                {
                    var decoder = Await(BitmapDecoder.CreateAsync(stream));
                    using (var sb = Await(decoder.GetSoftwareBitmapAsync()))
                    {
                        var ocr = Await(Ocr.RecognizeAsync(sb));
                        foreach (var line in ocr.Lines)
                        {
                            var text = new StringBuilder();
                            double minX = double.MaxValue, minY = double.MaxValue, maxX = 0, maxY = 0;
                            foreach (var word in line.Words)
                            {
                                text.Append(word.Text);
                                var r = word.BoundingRect;
                                if (r.X < minX) minX = r.X;
                                if (r.Y < minY) minY = r.Y;
                                if (r.X + r.Width > maxX) maxX = r.X + r.Width;
                                if (r.Y + r.Height > maxY) maxY = r.Y + r.Height;
                            }
                            if (text.Length == 0 || minX == double.MaxValue) continue;
                            var item = new OcrLine();
                            item.Text = text.ToString();
                            item.X = minX;
                            item.Y = minY;
                            item.W = maxX - minX;
                            item.H = maxY - minY;
                            result.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // 交给外层 OcrShot 处理：先复制到临时目录重试一次
                throw;
            }
            return result;
        }

        private static void DumpOcr(List<OcrLine> lines, string name)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var l in lines)
                    sb.AppendLine(((int)l.X) + " | " + ((int)l.Y) + " | " + ((int)l.W) + " | " + ((int)l.H) + " | " + l.Text);
                File.WriteAllText(Path.Combine(RunShotDir, name + "_ocr.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static bool ContainsAny(string text, string[] keys)
        {
            if (string.IsNullOrEmpty(text) || keys == null) return false;
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length;
            int m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = prev[j - 1] + cost;
                    if (prev[j] + 1 < v) v = prev[j] + 1;
                    if (cur[j - 1] + 1 < v) v = cur[j - 1] + 1;
                    cur[j] = v;
                }
                var tmp = prev;
                prev = cur;
                cur = tmp;
            }
            return prev[m];
        }

        private static bool FuzzyContains(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return false;
            if (key.Length <= 3) return text.IndexOf(key, StringComparison.Ordinal) >= 0;
            int maxDist = key.Length >= 6 ? 2 : 1;
            if (Levenshtein(text, key) <= maxDist) return true;
            for (int i = 0; i + key.Length <= text.Length; i++)
            {
                if (Levenshtein(text.Substring(i, key.Length), key) <= maxDist) return true;
            }
            return false;
        }

        private static bool LooksInGame(List<OcrLine> lines)
        {
            // 主界面 HUD 的特征文字：右下角 UID、血条旁 Lv.xx、左下角 Enter 按键提示。
            // 有任意一个就说明已经进游戏了，协议/标题界面都不用再处理。
            foreach (var l in lines)
            {
                var s = l.Text.Trim();
                if (s.Length == 0 || s.Length > 24) continue;
                if (s.IndexOf("UID", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (s.StartsWith("Lv.", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.StartsWith("Lv ", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "Enter", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static OcrLine FindLine(List<OcrLine> lines, string[] keywords, string[] danger, string[] exclude)
        {
            OcrLine best = null;
            int bestScore = -1;
            foreach (var line in lines)
            {
                string text = line.Text;
                if (string.IsNullOrEmpty(text)) continue;
                text = text.Replace(" ", "").Replace("\u3000", "");
                if (text.Length == 0 || text.Length > 24) continue;
                if (ContainsAny(text, danger)) continue;
                if (ContainsAny(text, exclude)) continue;

                foreach (var kw in keywords)
                {
                    if (string.IsNullOrEmpty(kw)) continue;
                    string key = kw.Replace(" ", "");
                    int score = -1;
                    if (text == key) score = 100;
                    else if (text.StartsWith(key, StringComparison.Ordinal)) score = 60;
                    else if (text.IndexOf(key, StringComparison.Ordinal) >= 0) score = 25;
                    else if (FuzzyContains(text, key)) score = 15;
                    if (score < 0) continue;

                    if (line.W >= 60 && line.H >= 20) score += 10;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = line;
                    }
                }
            }
            return best;
        }

        #endregion

        #region 点击

        /// <summary>把鼠标移到屏幕角落，避免 hover 改变按钮样式影响模板匹配</summary>
        private static void ParkMouse()
        {
            try
            {
                Native.SetCursorPos(2, 2);
                Thread.Sleep(120);
            }
            catch { }
        }

        private static bool ClickAt(int x, int y)
        {
            try
            {
                Native.SetCursorPos(x, y);
                Thread.Sleep(150);
                Native.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(80);
                Native.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(250);
                // 点完立刻挪开鼠标：停留在按钮上会触发 hover 样式，后续模板/截图都会失真
                ParkMouse();
                return true;
            }
            catch (Exception ex)
            {
                Log("点击失败: " + ex.Message);
                return false;
            }
        }

        private static int MatchFileAndExit(string file, string name)
        {
            try
            {
                var shot = new Shot();
                shot.Path = file;
                using (var bmp = new Bitmap(file)) { shot.W = bmp.Width; shot.H = bmp.Height; }

                var names = string.IsNullOrEmpty(name)
                    ? Directory.GetFiles(TemplateEngine.Dir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)).ToArray()
                    : new[] { name };

                foreach (var n in names)
                {
                    var m = TemplateEngine.Find(shot.Path, n, 0.30, TemplateEngine.ScalesFor(shot.W));
                    if (m == null) { Log("[" + n + "] 无结果"); continue; }
                    Log("[" + n + "] score=" + m.Score.ToString("0.000") + " 位置=(" + m.X + "," + m.Y + ") 尺寸=" + m.W + "x" + m.H + " scale=" + m.Scale.ToString("0.00") + " 中心=(" + (m.X + m.W / 2) + "," + (m.Y + m.H / 2) + ")");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log("匹配测试失败: " + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// 用模板确认是否在原神页面；不在就按模板找到左侧原神页签并点击。
        /// 全程不依赖任何绝对坐标——位置来自 matchTemplate 的结果。
        /// </summary>
        /// <summary>
        /// 读取启动器 CEF 日志里最后一次页面 URL 的 hash。
        /// hk4e_cn = 原神页面，hkrpg_cn = 星穹铁道。这是最可靠的“当前在哪个游戏页”判据。
        /// </summary>
        private static string ReadLauncherPageHash()
        {
            try
            {
                var dir = Path.GetDirectoryName(Cfg.LauncherPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

                var logs = Directory.GetFiles(dir, "debug.log", SearchOption.AllDirectories);
                if (logs.Length == 0)
                {
                    Log("页面 hash: 在 " + dir + " 下没找到 debug.log");
                    return null;
                }

                var file = logs.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                string last = null;
                // 启动器会独占打开这个日志，必须用 FileShare.ReadWrite 才能读到
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.IndexOf("hypfe/index.html", StringComparison.OrdinalIgnoreCase) >= 0) last = line;
                    }
                }
                if (last == null)
                {
                    Log("页面 hash: " + file + " 里没有 hypfe URL 行");
                    return null;
                }

                var m = Regex.Match(last, @"#/([A-Za-z0-9_]+)");
                if (!m.Success)
                {
                    Log("页面 hash: URL 行没解析出 #/xxx");
                    return null;
                }
                return m.Groups[1].Value.ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Log("页面 hash 读取失败: " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        private static bool SwitchToGenshinPage(IntPtr hwnd)
        {
            // 1) 老版本启动器会写页面 URL（hk4e_cn = 原神），能读到就直接确认
            var hash = ReadLauncherPageHash();
            if (!string.IsNullOrEmpty(hash))
            {
                if (hash.StartsWith("hk4e"))
                {
                    Log("确认在原神页面（启动器页面 hash=" + hash + "）");
                    return true;
                }
                Log("启动器当前页面 hash=" + hash + "（不是原神），准备切换");
            }

            // 2) 模板定位左侧“原神”页签，点一次。
            //    已经在原神页时点它是幂等的（无副作用）；不在原神页时它会切过去。
            for (int round = 1; round <= 2; round++)
            {
                var shot = CaptureForOcr(hwnd, "launcher_page");
                if (shot == null)
                {
                    Log("截图失败，跳过页面检查");
                    return false;
                }
                if (LooksBlank(shot.Path))
                {
                    ForceRepaint(hwnd);
                    continue;
                }

                var scales = TemplateEngine.ScalesFor(shot.W);
                var tab = TemplateEngine.Find(shot.Path, "launcher_tab_genshin", 0.80, scales);
                if (tab == null || tab.Score < 0.80)
                {
                    Log("模板没找到左侧原神页签（得分 " + (tab == null ? "无" : tab.Score.ToString("0.00")) + "），放弃切换");
                    return false;
                }

                int cx = tab.X + tab.W / 2;
                int cy = tab.Y + tab.H / 2;
                Log("模板定位到原神页签: 中心 (" + cx + "," + cy + ") 得分 " + tab.Score.ToString("0.00") + "，点击切换");
                if (!ClickAtClientPoint(hwnd, cx, cy))
                {
                    Log("页签点击失败");
                    return false;
                }
                Thread.Sleep(2500);

                // 点完再读一次 hash（老版本可用）
                hash = ReadLauncherPageHash();
                if (!string.IsNullOrEmpty(hash) && hash.StartsWith("hk4e"))
                {
                    Log("切换后确认在原神页面（hash=" + hash + "）");
                    return true;
                }
                break;
            }

            // 3) 新版启动器读不到 hash：以“点击了原神页签”为准。
            //    万一真的没切过去，后面按 YuanShen 进程找不到游戏窗口会安全失败，不会点错游戏。
            Log("已点击原神页签（新版启动器无法读页面 hash，按点击结果继续）");
            return true;
        }
        /// <summary>
        /// 把识别到的按钮区域裁下来存成模板（只在该模板还不存在时采一次）。
        /// 这样换分辨率/换显示器后，第一次跑就能把当前环境的按钮外观记录下来。
        /// </summary>
        private static void AutoCaptureButton(Shot shot, OcrLine line, string name)
        {
            try
            {
                if (shot == null || line == null) return;
                var path = Path.Combine(TemplateEngine.Dir, name + "_" + shot.W + "x" + shot.H + ".png");
                if (File.Exists(path)) return;

                int x = (int)Math.Max(0, line.X - 12);
                int y = (int)Math.Max(0, line.Y - 12);
                int w = (int)Math.Min(shot.W - x, line.W + 24);
                int h = (int)Math.Min(shot.H - y, line.H + 24);
                if (w < 24 || h < 14) return;

                using (var src = new Bitmap(shot.Path))
                using (var crop = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(crop))
                    {
                        g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                    }
                    crop.Save(path, ImageFormat.Png);
                }
                Log("已自动采集按钮模板: " + name + " " + w + "x" + h + " → templates\\" + name + ".png");
            }
            catch (Exception ex)
            {
                Log("采集按钮模板失败(" + name + "): " + ex.Message);
            }
        }

        /// <summary>判断截图是不是几乎全白（CEF 白屏）</summary>
        private static bool LooksBlank(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
                using (var bmp = new Bitmap(path))
                {
                    int w = bmp.Width, h = bmp.Height;
                    if (w < 10 || h < 10) return true;
                    int sx = Math.Max(1, w / 40);
                    int sy = Math.Max(1, h / 40);
                    long n = 0;
                    double sum = 0, sum2 = 0;
                    for (int y = 0; y < h; y += sy)
                    {
                        for (int x = 0; x < w; x += sx)
                        {
                            var c = bmp.GetPixel(x, y);
                            double l = (c.R + c.G + c.B) / 3.0;
                            sum += l;
                            sum2 += l * l;
                            n++;
                        }
                    }
                    if (n == 0) return true;
                    double mean = sum / n;
                    double variance = sum2 / n - mean * mean;
                    return mean > 235 && variance < 60;
                }
            }
            catch { return false; }
        }

        /// <summary>最小化再还原，逼 CEF 重新绘制（用户实测有效）</summary>
        private static void ForceRepaint(IntPtr hwnd)
        {
            try
            {
                Log("启动器画面疑似白屏，执行最小化/还原强制重绘");
                Native.ShowWindow(hwnd, 6);   // SW_MINIMIZE
                Thread.Sleep(450);
                Native.ShowWindow(hwnd, 9);   // SW_RESTORE
                Thread.Sleep(700);
                EnsureForeground(hwnd);
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                Log("强制重绘失败: " + ex.Message);
            }
        }

        /// <summary>优先用模板定位按钮并点击；模板不可用再退回 OCR 框中心</summary>
        private static bool ClickBest(IntPtr hwnd, OcrLine line, string tplName)
        {
            try
            {
                var tplPath = Path.Combine(TemplateEngine.Dir, tplName + ".png");
                if (File.Exists(tplPath))
                {
                    ParkMouse();
                    var shot = CaptureForOcr(hwnd, "tpl_" + tplName);
                    if (shot != null)
                    {
                        var m = TemplateEngine.Find(shot.Path, tplName, 0.85, TemplateEngine.ScalesFor(shot.W));
                        if (m != null && m.Score >= 0.85)
                        {
                            Log("模板定位按钮 " + tplName + " 得分 " + m.Score.ToString("0.00") + "，点击中心");
                            return ClickAtClientPoint(hwnd, m.X + m.W / 2, m.Y + m.H / 2);
                        }
                        Log("模板 " + tplName + " 匹配度不足，回退 OCR 框");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("模板点按失败，回退 OCR: " + ex.Message);
            }
            return ClickLine(hwnd, line);
        }

        /// <summary>按客户区坐标点击（坐标要先经过 ClientToScreen）</summary>
        private static bool ClickAtClientPoint(IntPtr hwnd, int cx, int cy)
        {
            if (DryRun)
            {
                Log("[演练] 本应点击客户区 (" + cx + "," + cy + ")");
                return false;
            }
            if (!EnsureForeground(hwnd))
            {
                Log("窗口切不到前台，取消点击");
                return false;
            }
            var p = new Native.POINT();
            p.X = cx;
            p.Y = cy;
            if (!Native.ClientToScreen(hwnd, ref p)) return false;
            Log("点击客户区 (" + cx + "," + cy + ") → 屏幕 (" + p.X + "," + p.Y + ")");
            return ClickAt(p.X, p.Y);
        }

        private static int ClickTemplateAndExit(string name)
        {
            try
            {
                var lw = FindLauncherWindow();
                if (lw == null) { Log("找不到启动器窗口"); return 2; }
                ParkMouse();
                var shot = CaptureForOcr(lw.Handle, "tpl_click");
                if (shot == null) { Log("截图失败"); return 2; }

                var m = TemplateEngine.Find(shot.Path, name, 0.80, TemplateEngine.ScalesFor(shot.W));
                if (m == null || m.Score < 0.80)
                {
                    Log("模板 " + name + " 未达到阈值（" + (m == null ? "无结果" : m.Score.ToString("0.00")) + "），不点击");
                    return 2;
                }
                int cx = m.X + m.W / 2;
                int cy = m.Y + m.H / 2;
                Log("模板 " + name + " 命中 得分 " + m.Score.ToString("0.00") + "，点击中心 (" + cx + "," + cy + ")");
                bool ok = ClickAtClientPoint(lw.Handle, cx, cy);
                Log(ok ? "已点击" : "点击失败");
                return ok ? 0 : 2;
            }
            catch (Exception ex)
            {
                Log("模板点击失败: " + ex.Message);
                return 2;
            }
        }

        /// <summary>调试用：在启动器客户区 (x,y) 精确点击一次</summary>
        private static int ClickClientAndExit(string spec)
        {
            try
            {
                var parts = spec.Split(',');
                int cx = int.Parse(parts[0].Trim());
                int cy = int.Parse(parts[1].Trim());

                var lw = FindLauncherWindow();
                if (lw == null) { Log("找不到启动器窗口"); return 2; }
                if (!EnsureForeground(lw.Handle)) { Log("启动器切不到前台，放弃点击"); return 2; }

                var p = new Native.POINT();
                p.X = cx;
                p.Y = cy;
                if (!Native.ClientToScreen(lw.Handle, ref p)) { Log("客户区坐标换算失败"); return 2; }

                Log("客户区点击 (" + cx + "," + cy + ") → 屏幕 (" + p.X + "," + p.Y + ")");
                bool ok = ClickAt(p.X, p.Y);
                Log(ok ? "点击完成" : "点击失败");
                TryFinalizeRunShots();
                return ok ? 0 : 2;
            }
            catch (Exception ex)
            {
                Log("参数或点击错误: " + ex.Message);
                return 2;
            }
        }

        private static bool ClickLine(IntPtr h, OcrLine line)
        {
            if (line == null) return false;
            if (DryRun)
            {
                Log("[演练] 本应点击“" + line.Text + "”");
                return false;
            }
            if (!EnsureForeground(h))
            {
                Log("窗口切不到前台，取消点击“" + line.Text + "”");
                return false;
            }
            var p = new Native.POINT();
            p.X = (int)(line.X + line.W / 2);
            p.Y = (int)(line.Y + line.H / 2);
            if (!Native.ClientToScreen(h, ref p)) return false;
            Log("点击“" + line.Text + "” 屏幕坐标 (" + p.X + "," + p.Y + ")");
            return ClickAt(p.X, p.Y);
        }

        private static bool ClickRatio(IntPtr h, double rx, double ry, string label)
        {
            if (DryRun)
            {
                Log("[演练] 本应按比例 (" + rx + "," + ry + ") 点击 " + label);
                return false;
            }
            if (!EnsureForeground(h))
            {
                Log("窗口切不到前台，取消坐标点击 " + label);
                return false;
            }
            Native.RECT rc;
            if (!Native.GetClientRect(h, out rc)) return false;
            int w = rc.Right - rc.Left;
            int hgt = rc.Bottom - rc.Top;
            if (w <= 10 || hgt <= 10) return false;
            var p = new Native.POINT();
            p.X = (int)(w * rx);
            p.Y = (int)(hgt * ry);
            if (!Native.ClientToScreen(h, ref p)) return false;
            Log("坐标回退点击 " + label + ": 客户区 " + w + "x" + hgt + "，屏幕 (" + p.X + "," + p.Y + ")");
            return ClickAt(p.X, p.Y);
        }

        #endregion

        #region 主流程

        private static void RunFlow()
        {
            Log("config.ini game_version = " + ReadGameVersion());

            if (!DryRun && !StopBeforeBetterGi && !ForceRun)
            {
                var doneMarker = Path.Combine(LogDir, "ran_" + DateTime.Now.ToString("yyyyMMdd") + ".flag");
                if (File.Exists(doneMarker))
                {
                    Log("今天已经成功运行过（" + Path.GetFileName(doneMarker) + "），本次跳过；强制重跑请加 --force");
                    SkippedToday = true;
                    return;
                }
            }

            WaitForSystemSettle();

            var preGame = FindGameWindow();
            if (preGame != null)
            {
                Log("检测到原神已在运行（" + preGame.Width + "x" + preGame.Height + "），跳过启动器，直接进入游戏处理");
                RunGamePhases();
                return;
            }

            Log("---------- 阶段一：启动启动器 ----------");
            var lw = FindLauncherWindow();
            if (lw == null) lw = ReviveLauncherWindow();
            if (lw != null)
            {
                Log("启动器已在运行: " + lw.Title + " (pid=" + lw.Pid + ")");
            }
            else
            {
                if (DryRun)
                {
                    Log("[演练] 启动器未运行；演练模式不会真的启动它，请先手动打开启动器再试");
                    return;
                }
                lw = StartLauncherAndWait();
            }

            if (lw == null)
            {
                Log("启动器窗口始终没有出现（进程状态: " + DescribeLauncherProcesses() + "）");
                if (!Cfg.AllowDirectGameFallback)
                {
                    throw new Exception("启动器窗口未出现，且未允许绕过启动器（AllowDirectGameFallback=false）");
                }
                Log("配置允许兜底：直接启动原神本体（本次将无法判断更新）");
                StartGameDirect();
                RunGamePhases();
                return;
            }

            Log("启动器窗口就绪: " + lw.Title + " (pid=" + lw.Pid + ", " + lw.Width + "x" + lw.Height + ")");

            // 必须先确认在原神页面，否则星穹铁道页上的「开始游戏」会被误认
            if (!SwitchToGenshinPage(lw.Handle))
            {
                if (DryRun)
                {
                    Log("[演练] 无法确认启动器在原神页面，演练到此结束");
                    return;
                }
                throw new Exception("无法确认启动器在原神页面（未找到原神页标识），为避免点错游戏已中止");
            }

            Log("---------- 阶段二：判断启动器状态 ----------");
            var state = WaitLauncherState(Cfg.LauncherStateTimeout);

            if (state.Kind == LauncherKind.OtherGame)
            {
                Log("启动器不在原神页面（" + (state.Line == null ? "" : state.Line.Text) + "），用模板定位原神页签");
                if (SwitchToGenshinPage(lw.Handle))
                {
                    Thread.Sleep(2500);
                    state = WaitLauncherState(60);
                }
            }

            if (state.Kind == LauncherKind.Timeout || state.Kind == LauncherKind.OtherGame)
            {
                Log("启动器状态仍不可用（" + state.Kind + "）: " + state.Note);
                if (!Cfg.AllowDirectGameFallback)
                {
                    throw new Exception("启动器无法进入原神页面，且未允许绕过启动器");
                }
                if (DryRun)
                {
                    Log("[演练] 不启动原神、不关机");
                    return;
                }
                Log("配置允许兜底：直接启动原神本体（本次将无法判断更新）");
                StartGameDirect();
                RunGamePhases();
                return;
            }

            if (DryRun)
            {
                Log("[演练] 识别到状态 " + state.Kind + "，按钮“" + (state.Line == null ? "?" : state.Line.Text) + "”，演练结束（不点击、不启动、不关机）");
                return;
            }

            if (state.Kind == LauncherKind.Update)
            {
                Log("检测到需要更新: “" + state.Line.Text + "”");
                if (!ClickBest(lw.Handle, state.Line, "auto_btn_update")) throw new Exception("点击更新按钮失败（或演练模式）");
                Log("---------- 阶段二-1：等待更新完成 ----------");
                WaitUpdateComplete(Cfg.UpdateTimeout);
                Log("更新完成，启动器已出现“开始游戏”");

                if (Cfg.NewVersionCooldown)
                {
                    Log("【新版本冷却期】已开启，不启动 BetterGI，直接兜底关机");
                    return;
                }
            }
            else
            {
                Log("启动器显示“开始游戏”，无需更新");
            }

            Log("---------- 阶段二-2：启动原神 ----------");
            // 注意：进程是全机器可见的，窗口是按会话隔离的。
            // 别的登录会话里开着原神时，这里必须按“本会话有没有游戏窗口”来判断，否则会误以为已启动。
            var gameWinNow = FindGameWindow();
            if (gameWinNow != null)
            {
                Log("原神已在运行（本会话可见窗口），跳过“开始游戏”");
            }
            else
            {
                var st2 = WaitLauncherState(60);
                if (st2.Kind != LauncherKind.Start) throw new Exception("未识别到“开始游戏”");
                if (!ClickBest(lw.Handle, st2.Line, "auto_btn_start")) throw new Exception("点击开始游戏失败（或演练模式）");
                if (!WaitGameProcess(Cfg.GameProcessTimeout)) throw new Exception("点击后未检测到原神进程");
            }

            RunGamePhases();
        }

        /// <summary>等游戏窗口 → 协议 → 标题界面 → 可操作 → BetterGI → 看门狗</summary>
        private static void RunGamePhases()
        {
            var gw = WaitWindow(FindGameWindow, Cfg.GameWindowTimeout, delegate (int n) { Log("等待原神窗口…… " + (n * 2) + "s"); });
            if (gw == null) throw new Exception("原神窗口未出现");
            Log("原神窗口就绪: " + gw.Title + " " + gw.Width + "x" + gw.Height);
            if (gw.Width != 1920 || gw.Height != 1080)
            {
                Log("提示：游戏客户区是 " + gw.Width + "x" + gw.Height + "，BetterGI 以 1920x1080 为基准（其他 16:9 多数也支持），建议按 1920x1080 窗口化");
            }

            HandleAgreement(gw);
            if (Cfg.AutoClickGameEntry) ClickGameEntry(gw);
            WaitGameReady(gw, Cfg.GameSettleSeconds);

            if (DryRun)
            {
                Log("[演练] 到此结束：不启动 BetterGI、不关机");
                return;
            }

            if (StopBeforeBetterGi)
            {
                Log("已按要求停在启动 BetterGI 之前（--stop-before-bettergi），不关机");
                return;
            }

            StartBetterGi();
            WriteTodayMarker();
            RunWatchdog();
        }

        /// <summary>开机后系统还在装更新/初始化时不急着跑，等一会儿</summary>
        private static void WaitForSystemSettle()
        {
            try
            {
                if (DryRun || ForceRun) return;
                int min = SkipUptime ? 0 : Cfg.MinUptimeMinutes;
                if (min <= 0) return;

                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var need = TimeSpan.FromMinutes(min);
                if (up >= need) return;

                var wait = need - up;
                if (wait > TimeSpan.FromMinutes(30)) wait = TimeSpan.FromMinutes(30);
                Log("系统启动才 " + (int)up.TotalMinutes + " 分钟（要求至少 " + min + " 分钟），后台可能还在装更新，等待 " + (int)Math.Ceiling(wait.TotalMinutes) + " 分钟");
                Thread.Sleep(wait);
                Log("等待结束，继续执行");
            }
            catch { }
        }

        /// <summary>把被关到托盘/隐藏的启动器窗口重新显示出来</summary>
        private static WinInfo ReviveLauncherWindow()
        {
            try
            {
                foreach (var w in EnumTopWindows(false))
                {
                    if (string.Equals(w.Proc, Cfg.GameProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                    bool family = string.Equals(w.Proc, Cfg.LauncherProcessName, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(w.Proc, "HYP", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(w.Proc, "HoYoPlay", StringComparison.OrdinalIgnoreCase);
                    if (!family) continue;
                    if (w.Width < 640 || w.Height < 480) continue;

                    Log("发现隐藏/托盘状态的启动器窗口（" + w.Proc + " " + w.Width + "x" + w.Height + "），尝试还原");
                    Native.ShowWindow(w.Handle, 9);
                    Native.ShowWindow(w.Handle, 5);
                    Native.SetForegroundWindow(w.Handle);
                    Thread.Sleep(1200);

                    var again = FindLauncherWindow();
                    if (again != null)
                    {
                        Log("启动器窗口已还原");
                        return again;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("还原启动器窗口失败: " + ex.Message);
            }
            return null;
        }

        /// <summary>直接启动原神本体（没有大版本更新时可用）</summary>
        private static void StartGameDirect()
        {
            if (!File.Exists(Cfg.GameExePath)) throw new Exception("找不到原神主程序: " + Cfg.GameExePath);
            var psi = new ProcessStartInfo(Cfg.GameExePath);
            psi.WorkingDirectory = Path.GetDirectoryName(Cfg.GameExePath);
            psi.UseShellExecute = true;
            Log("直接启动原神: " + Cfg.GameExePath);
            if (!DryRun) Process.Start(psi);
        }

        private static string DescribeLauncherProcesses()
        {
            var sb = new StringBuilder();
            foreach (var n in new[] { "launcher", "HYP", "HYPHelper", "HYUpdater" })
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(n))
                    {
                        if (sb.Length > 0) sb.Append("; ");
                        sb.Append(proc.ProcessName).Append("#").Append(proc.Id)
                           .Append("(session ").Append(proc.SessionId).Append(")");
                    }
                }
                catch { }
            }
            return sb.Length == 0 ? "（没有启动器相关进程）" : sb.ToString();
        }
        private static string ReadGameVersion()
        {
            try
            {
                if (!File.Exists(Cfg.GameConfigIni)) return "(无 config.ini)";
                foreach (var line in File.ReadAllLines(Cfg.GameConfigIni))
                {
                    string t = line.Trim();
                    if (t.StartsWith("game_version", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = t.IndexOf('=');
                        if (idx > 0) return t.Substring(idx + 1).Trim();
                    }
                }
            }
            catch { }
            return "(读取失败)";
        }

        private static bool WaitGameProcess(int timeoutSeconds)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline)
            {
                if (IsProcessRunning(Cfg.GameProcessName)) return true;
                Thread.Sleep(2000);
            }
            return false;
        }

        private static LauncherState WaitLauncherState(int timeoutSeconds)
        {
            var st = new LauncherState();
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            var lastLog = DateTime.Now.AddSeconds(-60);
            int selfUpdateClicks = 0;

            while (DateTime.Now < deadline)
            {
                var w = FindLauncherWindow();
                if (w == null) { Thread.Sleep(2000); continue; }

                EnsureForeground(w.Handle);
                ParkMouse();
                var shot = CaptureForOcr(w.Handle, "launcher");
                if (shot == null) { Thread.Sleep(2000); continue; }
                if (LooksBlank(shot.Path))
                {
                    ForceRepaint(w.Handle);
                    continue;
                }
                var lines = OcrShot(shot);
                if (lines.Count == 0) { Thread.Sleep(2000); continue; }

                var pause = FindLine(lines, Cfg.KwPause, null, null);
                if (pause != null)
                {
                    if ((DateTime.Now - lastLog).TotalSeconds >= 30)
                    {
                        Log("更新进行中（" + pause.Text + "），继续等待……");
                        lastLog = DateTime.Now;
                    }
                    Thread.Sleep(Cfg.PollSeconds * 1000);
                    continue;
                }

                // 主按钮：模板负责“定位”，OCR 只负责“读文字”（同一颗黄胶囊，形状无法区分开始/更新）
                OcrLine start = null;
                OcrLine update = null;

                string tplName;
                double tplScore;
                var btn = FindByTemplate(shot, new[] { "auto_btn_start", "launcher_btn_update" }, 0.85, out tplName, out tplScore);
                if (btn != null)
                {
                    string label = OcrButtonLabel(shot, btn);
                    btn.Text = string.IsNullOrEmpty(label) ? tplName : label;
                    Log("模板定位主按钮（" + tplName + " 得分 " + tplScore.ToString("0.00") + "），OCR 读出的文字: “" + btn.Text + "”");

                    if (label.IndexOf("开始游戏", StringComparison.Ordinal) >= 0)
                    {
                        start = btn;
                    }
                    else if (label.IndexOf("更新", StringComparison.Ordinal) >= 0)
                    {
                        update = btn;
                    }
                    else
                    {
                        // 文字没读出来，退回按模板身份判断
                        if (tplName.StartsWith("auto_btn_start", StringComparison.Ordinal)) start = btn;
                        else update = btn;
                    }
                }

                // 模板没定位到 → 整图 OCR 兜底
                if (start == null && update == null)
                {
                    FindLauncherButtons(lines, shot, out start, out update);
                }

                var busy = FindLine(lines, Cfg.KwBusy, null, null);

                var otherGame = FindLine(lines, Cfg.KwOtherGame, null, null);
                if (otherGame != null && start == null && update == null)
                {
                    Log("启动器不在原神页面（识别到“" + otherGame.Text + "”），判定为其它游戏或修复中，不做任何点击");
                    st.Kind = LauncherKind.OtherGame;
                    st.Line = otherGame;
                    return st;
                }

                if (busy != null && start == null)
                {
                    // 更新/下载进行中：哪怕按钮上写着“下载中”也不点，继续等
                    if ((DateTime.Now - lastLog).TotalSeconds >= 30)
                    {
                        Log("启动器正在更新/下载中（" + busy.Text + "），继续等待……");
                        lastLog = DateTime.Now;
                    }
                    Thread.Sleep(Cfg.PollSeconds * 1000);
                    continue;
                }

                if (update != null && start != null && start.Area > update.Area * 1.2)
                {
                    Log("同时看到“开始游戏”和“" + update.Text + "”，按按钮面积判定为无需更新");
                    st.Kind = LauncherKind.Start;
                    st.Line = start;
                    return st;
                }
                if (update != null)
                {
                    // 启动器自身更新提示（例「立即更新」）→ 点它并等重启，别当成原神更新
                    bool selfUpdate = update.Text.IndexOf("立即更新", StringComparison.Ordinal) >= 0
                                   || update.Text.IndexOf("更新启动器", StringComparison.Ordinal) >= 0
                                   || update.Text.IndexOf("启动器更新", StringComparison.Ordinal) >= 0;
                    if (selfUpdate && selfUpdateClicks < 2)
                    {
                        selfUpdateClicks++;
                        Log("检测到启动器自身更新提示: “" + update.Text + "”，点击并等待启动器重启（第 " + selfUpdateClicks + " 次）");
                        ClickBest(w.Handle, update, "auto_btn_launcher_update");
                        int waitSec = Math.Max(10, Cfg.LauncherSelfUpdateWaitSeconds);
                        Log("等待启动器更新/重启 " + waitSec + " 秒……");
                        Thread.Sleep(waitSec * 1000);
                        continue;
                    }

                    AutoCaptureButton(shot, update, "auto_btn_update");
                    Log("识别到原神更新按钮: “" + update.Text + "”");
                    st.Kind = LauncherKind.Update;
                    st.Line = update;
                    return st;
                }
                if (start != null)
                {
                    AutoCaptureButton(shot, start, "auto_btn_start");
                    Log("识别到开始游戏按钮: “" + start.Text + "”");
                    st.Kind = LauncherKind.Start;
                    st.Line = start;
                    return st;
                }

                if ((DateTime.Now - lastLog).TotalSeconds >= 30)
                {
                    Log("暂未识别到主按钮，当前 OCR: " + JoinTexts(lines, 12));
                    lastLog = DateTime.Now;
                }
                Thread.Sleep(Cfg.PollSeconds * 1000);
            }

            st.Note = "超时，最后 OCR: " + JoinTexts(OcrLast("launcher_timeout"), 15);
            return st;
        }

        private static WinInfo StartLauncherAndWait()
        {
            if (!File.Exists(Cfg.LauncherPath)) throw new Exception("找不到启动器: " + Cfg.LauncherPath);

            // 有启动器进程却没有窗口 = 上次启动卡死（常见于开机初期注册表初始化失败），先清掉再启动
            if (FindLauncherWindow() == null && LauncherFamilyAlive())
            {
                Log("发现残留的启动器进程但没有窗口（可能上次初始化失败），先清理: " + DescribeLauncherProcesses());
                KillLauncherFamily();
                Thread.Sleep(2000);
            }

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (attempt > 1)
                {
                    KillLauncherFamily();
                    Thread.Sleep(1500);
                }
                bool viaExplorer = attempt >= 2;   // 首选直接启动；失败再用 Explorer 兜底
                Log("启动启动器（第 " + attempt + " 次" + (viaExplorer ? "，改用 Explorer 方式" : "") + "）: " + Cfg.LauncherPath);
                if (!DryRun) StartLauncherProcess(viaExplorer);
                else Log("[演练] 跳过真实启动");

                var start = DateTime.Now;
                int lastLog = -1;
                int limit = attempt == 1 ? 45 : Cfg.LauncherStartTimeout;
                while ((DateTime.Now - start).TotalSeconds < limit)
                {
                    var w = FindLauncherWindow();
                    if (w != null) return w;

                    int elapsed = (int)(DateTime.Now - start).TotalSeconds;
                    if (elapsed >= 20 && elapsed / 20 != lastLog)
                    {
                        Log("等待启动器窗口…… " + elapsed + "s");
                        lastLog = elapsed / 20;
                    }

                    // 启动器被已有实例/更新程序接管时会秒退，此时没有窗口 = 这次白启动，重试一次
                    if (elapsed >= 20 && !LauncherFamilyAlive())
                    {
                        Log("启动器进程已退出且没有窗口");
                        break;
                    }
                    Thread.Sleep(2000);
                }
            }
            Log("两次启动都没有出现窗口，启动器进程状态: " + DescribeLauncherProcesses());
            return null;
        }

        /// <summary>结束所有启动器家族进程（本程序以管理员运行，实测可以结束 HYP）</summary>
        private static void KillLauncherFamily()
        {
            int killed = 0;
            foreach (var name in new[] { "HYP", "HYPHelper", "launcher", "HYUpdater" })
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                            killed++;
                        }
                        catch (Exception ex)
                        {
                            Log("结束 " + name + "#" + proc.Id + " 失败: " + ex.Message);
                        }
                    }
                }
                catch { }
            }
            if (killed > 0) Log("已清理 " + killed + " 个残留启动器进程");
        }

        private static void StartLauncherProcess(bool viaExplorer = false)
        {
            if (viaExplorer)
            {
                // 通过 Explorer 拉起 = 等价于用户在桌面双击，启动器以普通权限启动
                // （计划任务上下文里直接启动会让 HYP 初始化失败，这是实测出来的差异）
                var psiEx = new ProcessStartInfo("explorer.exe", "\"" + Cfg.LauncherPath + "\"");
                psiEx.UseShellExecute = true;
                Process.Start(psiEx);
                Log("已通过 Explorer 拉起启动器（模拟桌面双击）");
                return;
            }

            var psi = new ProcessStartInfo(Cfg.LauncherPath);
            psi.WorkingDirectory = Path.GetDirectoryName(Cfg.LauncherPath);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static bool LauncherFamilyAlive()
        {
            string[] names = { "launcher", "HYP", "HYPHelper", "HYUpdater" };
            foreach (var n in names)
            {
                try
                {
                    if (Process.GetProcessesByName(n).Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        private static void WaitUpdateComplete(int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var deadline = startTime.AddSeconds(timeoutSeconds);
            int stable = 0;
            var lastLog = startTime.AddSeconds(-60);

            while (DateTime.Now < deadline)
            {
                var w = FindLauncherWindow();
                if (w == null) { Thread.Sleep(3000); continue; }
                var shot = CaptureForOcr(w.Handle, "launcher");
                if (shot == null) { Thread.Sleep(3000); continue; }
                var lines = OcrShot(shot);

                var pause = FindLine(lines, Cfg.KwPause, null, null);
                var busy = FindLine(lines, Cfg.KwBusy, null, null);
                OcrLine start;
                OcrLine update;
                FindLauncherButtons(lines, shot, out start, out update);
                bool stillUpdating = (pause != null) || (busy != null);

                if ((DateTime.Now - lastLog).TotalSeconds >= 60)
                {
                    Log("更新等待中…… 已 " + (int)(DateTime.Now - startTime).TotalSeconds + "s" + (pause != null ? "，" + pause.Text : ""));
                    lastLog = DateTime.Now;
                }

                if (start != null && update == null && !stillUpdating)
                {
                    stable++;
                    if (stable >= 3) return;
                    Thread.Sleep(4000) ;
                    continue;
                }
                stable = 0;
                Thread.Sleep(Cfg.PollSeconds * 1000);
            }
            throw new Exception("等待更新超时（" + timeoutSeconds + "s）");
        }

        private static void HandleAgreement(WinInfo gw)
        {
            Log("---------- 阶段三：处理协议弹窗 ----------");
            var deadline = DateTime.Now.AddSeconds(Cfg.AgreementTimeout);
            var phaseStart = DateTime.Now;
            int ocrClicks = 0;
            int relClicks = 0;
            bool seen = false;
            int cleared = 0;
            List<OcrLine> lastLines = null;

            while (DateTime.Now < deadline)
            {
                var w = FindGameWindow();
                if (w == null) { Thread.Sleep(2000); continue; }

                var shot = CaptureForOcr(w.Handle, "game");
                var lines = shot != null ? OcrShot(shot) : new List<OcrLine>();
                lastLines = lines;

                if (lines.Count > 0 && LooksInGame(lines))
                {
                    Log("画面已是游戏主界面（检测到 UID/HUD），跳过协议处理");
                    DumpOcr(lines, "game");
                    return;
                }

                if (lines.Count > 0)
                {
                    var agree = FindByTemplate(shot, new[] { "auto_btn_agree" }, 0.85, out _, out _);
                    if (agree != null)
                    {
                        Log("模板识别到协议按钮（auto_btn_agree）");
                    }
                    else
                    {
                        agree = FindLine(lines, Cfg.KwAgree, Cfg.KwDanger, Cfg.KwIgnore);
                        if (agree != null) AutoCaptureButton(shot, agree, "auto_btn_agree");
                    }
                    if (agree != null)
                    {
                        seen = true;
                        cleared = 0;
                        ocrClicks++;
                        Log("识别到协议按钮“" + agree.Text + "”，点击");
                        ClickLine(w.Handle, agree);
                        Thread.Sleep(6000);
                        if (ocrClicks >= Cfg.AgreementVerifyRounds)
                        {
                            Log("协议按钮点击 " + ocrClicks + " 轮后仍在，放弃");
                            break;
                        }
                        continue;
                    }

                    var entry = FindByTemplate(shot, new[] { "auto_btn_entry" }, 0.85, out _, out _);
                if (entry != null)
                {
                    Log("模板识别到“点击进入”按钮");
                }
                else
                {
                    entry = FindLine(lines, Cfg.KwEntry, Cfg.KwDanger, null);
                    if (entry != null) AutoCaptureButton(shot, entry, "auto_btn_entry");
                }
                    if (entry != null)
                    {
                        Log("画面已到标题界面（" + entry.Text + "），协议处理完成");
                        return;
                    }

                    if (seen)
                    {
                        cleared++;
                        if (cleared >= 2)
                        {
                            Log("协议弹窗已消失，协议处理完成");
                            return;
                        }
                    }
                }

                if (Cfg.AgreementRatioFallback && !seen && ocrClicks == 0 && relClicks < Cfg.MaxRelativeClicks &&
                    (DateTime.Now - phaseStart).TotalSeconds >= Cfg.AgreementRelativeDelay * (relClicks + 1))
                {
                    relClicks++;
                    Log("未识别到协议控件，按校准比例 (" + Cfg.AgreementRatioX + "," + Cfg.AgreementRatioY + ") 做第 " + relClicks + " 次坐标回退");
                    ClickRatio(w.Handle, Cfg.AgreementRatioX, Cfg.AgreementRatioY, "协议按钮");
                    Thread.Sleep(8000);
                    continue;
                }

                if (!seen && ocrClicks == 0 && relClicks >= Cfg.MaxRelativeClicks
                    && (DateTime.Now - phaseStart).TotalSeconds > 75)
                {
                    Log("坐标回退已用尽仍未出现协议弹窗，判定本次无协议，继续下一步");
                    return;
                }

                Thread.Sleep(Cfg.PollSeconds * 1000);
            }
            Log("协议阶段结束（OCR 点击 " + ocrClicks + " 次，坐标回退 " + relClicks + " 次）");
            if (lastLines != null) { Log("结束时画面文字: " + JoinTexts(lastLines, 12)); DumpOcr(lastLines, "game"); }
        }

        private static void ClickGameEntry(WinInfo gw)
        {
            Log("---------- 阶段三-2：点击标题界面 ----------");
            var deadline = DateTime.Now.AddSeconds(Cfg.GameEntryTimeout);
            int clicks = 0;
            int quiet = 0;

            while (DateTime.Now < deadline)
            {
                var w = FindGameWindow();
                if (w == null) { Thread.Sleep(2000); continue; }
                var shot = CaptureForOcr(w.Handle, "game");
                var lines = shot != null ? OcrShot(shot) : new List<OcrLine>();
                if (lines.Count > 0 && LooksInGame(lines))
                {
                    Log("已在游戏主界面，无需点击“点击进入”");
                    return;
                }
                var entry = FindByTemplate(shot, new[] { "auto_btn_entry" }, 0.85, out _, out _);
                if (entry != null)
                {
                    Log("模板识别到“点击进入”按钮");
                }
                else
                {
                    entry = FindLine(lines, Cfg.KwEntry, Cfg.KwDanger, null);
                    if (entry != null) AutoCaptureButton(shot, entry, "auto_btn_entry");
                }

                if (entry != null)
                {
                    clicks++;
                    quiet = 0;
                    Log("识别到标题界面“" + entry.Text + "”，点击进入游戏");
                    ClickLine(w.Handle, entry);
                    Thread.Sleep(6000);
                    continue;
                }

                quiet++;
                if (clicks > 0 && quiet >= 2)
                {
                    Log("标题界面已消失，游戏正在进入");
                    return;
                }
                if (clicks == 0 && quiet >= 3)
                {
                    Log("未识别到“点击进入”，交给 BetterGI");
                    return;
                }
                Thread.Sleep(3000);
            }
            Log("标题界面处理到达上限（点击 " + clicks + " 次）");
        }

        private static void WaitGameReady(WinInfo gw, int settleSeconds)
        {
            Log("---------- 阶段四：等待游戏可操作 ----------");
            var deadline = DateTime.Now.AddSeconds(Math.Max(60, settleSeconds * 4));
            DateTime? stableSince = null;
            while (DateTime.Now < deadline)
            {
                if (!IsProcessRunning(Cfg.GameProcessName)) throw new Exception("原神进程已退出");
                var w = FindGameWindow();
                if (w != null)
                {
                    if (stableSince == null) stableSince = DateTime.Now;
                    if ((DateTime.Now - stableSince.Value).TotalSeconds >= settleSeconds)
                    {
                        Log("原神窗口稳定 " + settleSeconds + " 秒，认为已可操作");
                        EnsureInGameOrRetry(w);
                        return;
                    }
                }
                else
                {
                    stableSince = null;
                }
                Thread.Sleep(2000);
            }
            Log("等待可操作超时，继续尝试启动 BetterGI");
        }

        /// <summary>
        /// 进游戏后的最终复核：如果画面里没有 UID/HUD，说明可能还停在协议或标题界面，
        /// 再补一轮"协议 + 点击进入"（协议路径平时很少能触发，这里做兜底）。
        /// </summary>
        private static void EnsureInGameOrRetry(WinInfo gw)
        {
            try
            {
                if (gw == null) return;
                var shot = CaptureForOcr(gw.Handle, "ready_check");
                var lines = shot != null ? OcrShot(shot) : new List<OcrLine>();
                if (lines.Count > 0 && LooksInGame(lines))
                {
                    Log("复核确认已在游戏主界面");
                    return;
                }

                Log("警告: 未检测到游戏主界面（UID/HUD），可能还停在协议或标题界面，补一轮处理");
                HandleAgreement(gw);
                if (Cfg.AutoClickGameEntry) ClickGameEntry(gw);
                Thread.Sleep(5000);

                var shot2 = CaptureForOcr(gw.Handle, "ready_check2");
                var lines2 = shot2 != null ? OcrShot(shot2) : new List<OcrLine>();
                if (lines2.Count > 0 && LooksInGame(lines2)) Log("补充处理后已进入游戏主界面");
                else Log("警告: 补充处理后仍未见主界面 HUD，交给 BetterGI 尝试");
            }
            catch (Exception ex)
            {
                Log("进游戏复核异常: " + ex.Message);
            }
        }

        private static void StartBetterGi()
        {
            Log("---------- 阶段五：启动 BetterGI ----------");
            if (!File.Exists(Cfg.BetterGiPath)) throw new Exception("找不到 BetterGI: " + Cfg.BetterGiPath);

            // 读 BetterGI 源码确认：Win32RuntimeProvider.AttachTo 里 `游戏窗口不能最小化` 会直接抛异常，
            // 并且它会激活窗口后按 1080P 归一化截图，所以启动前必须把窗口还原并置前。
            var gwBefore = FindGameWindow();
            if (gwBefore != null)
            {
                if (Native.IsIconic(gwBefore.Handle))
                {
                    Log("游戏窗口处于最小化，先还原（BetterGI 要求窗口不能最小化）");
                    Native.ShowWindow(gwBefore.Handle, 9);
                    Thread.Sleep(1000);
                }
                EnsureForeground(gwBefore.Handle);
                Log("游戏窗口已就绪: " + gwBefore.Title + " " + gwBefore.Width + "x" + gwBefore.Height);
            }
            else
            {
                Log("警告：启动 BetterGI 前没有找到原神窗口（BetterGI 可能会自己联动启动）");
            }

            var existing = Process.GetProcessesByName("BetterGI");
            if (existing.Length > 0) Log("注意：已有 BetterGI 在运行 (" + existing.Length + " 个)");

            var psi = new ProcessStartInfo(Cfg.BetterGiPath);
            psi.Arguments = Cfg.BetterGiArgs;
            psi.WorkingDirectory = Path.GetDirectoryName(Cfg.BetterGiPath);
            psi.UseShellExecute = false;
            Log("启动 BetterGI: \"" + Cfg.BetterGiPath + "\" " + Cfg.BetterGiArgs);
            var proc = Process.Start(psi);
            if (proc == null) throw new Exception("BetterGI 启动失败");

            Thread.Sleep(5000);
            try { proc.Refresh(); } catch { }
            if (!proc.HasExited)
            {
                BetterGiStarted = true;
                Log("BetterGI 已启动 (pid=" + proc.Id + ")，等待其完成后自动关机");
                return;
            }

            Log("BetterGI 启动后立即退出，退出码 " + proc.ExitCode);
            if (Process.GetProcessesByName("BetterGI").Length > 0)
            {
                BetterGiStarted = true;
                Log("仍有 BetterGI 实例在运行，认为参数已交给该实例");
                return;
            }
            throw new Exception("BetterGI 启动失败");
        }

        #endregion

        #region 关机

        /// <summary>
        /// 收尾：正常跑完删掉本次截图；失败/演练则保留现场（附 _result.txt）
        /// </summary>
        private static bool SkipTodayDone()
        {
            if (!SkippedToday) return false;
            Log("今天已完成过任务，本次不执行任何操作（不启动游戏、不关机）");
            return true;
        }

        /// <summary>成功拉起 BetterGI 后记一笔，防止后面补跑触发点再跑一次</summary>
        private static void WriteTodayMarker()
        {
            try
            {
                if (DryRun || ForceRun) return;
                var marker = Path.Combine(LogDir, "ran_" + DateTime.Now.ToString("yyyyMMdd") + ".flag");
                File.WriteAllText(marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new UTF8Encoding(true));
                Log("已写入今日完成标记: " + Path.GetFileName(marker));
            }
            catch (Exception ex)
            {
                Log("写入完成标记失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 看门狗：BetterGI 起来后守着它。一条龙正常结束会自己关机；
        /// 如果 BetterGI 提前消失（崩溃/被热键停掉/游戏退出），系统还在跑，就安排兜底关机。
        /// </summary>
        private static void RunWatchdog()
        {
            if (!Cfg.WatchdogEnabled)
            {
                Log("看门狗已关闭（WatchdogEnabled = false）");
                return;
            }

            int poll = Math.Max(10, Cfg.WatchdogPollSeconds);
            Log("看门狗已启动：每 " + poll + " 秒检查一次 BetterGI；它退出后若系统没在关机就安排 shutdown（取消：shutdown /a）");

            var start = DateTime.Now;
            while (true)
            {
                Thread.Sleep(poll * 1000);
                if (Process.GetProcessesByName("BetterGI").Length == 0) break;
                if ((DateTime.Now - start).TotalHours >= 9)
                {
                    Log("看门狗已监控 9 小时，停止监控");
                    return;
                }
            }

            WatchdogFired = true;
            Log("检测到 BetterGI 已退出（一条龙结束或异常），等待 30 秒看系统是否自行关机……");
            Thread.Sleep(30000);

            if (!Cfg.FallbackShutdown)
            {
                Log("兜底关机已禁用，看门狗不安排关机");
                return;
            }

            Log("安排兜底关机: shutdown /s /f /t " + Cfg.WatchdogShutdownDelay + "（若系统已在关机中，此命令会报错，属正常）");
            try
            {
                var psi = new ProcessStartInfo("shutdown", "/s /f /t " + Cfg.WatchdogShutdownDelay);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("看门狗关机命令失败: " + ex.Message);
            }
        }

        private static void TryFinalizeRunShots()
        {
            try
            {
                if (string.IsNullOrEmpty(RunShotDir) || !Directory.Exists(RunShotDir)) return;

                // 一次截图都没有（例如启动器没开就退出）——空目录不留
                try
                {
                    if (Directory.GetFileSystemEntries(RunShotDir).Length == 0)
                    {
                        Directory.Delete(RunShotDir);
                        return;
                    }
                }
                catch { }

                if (RunKind != "run")
                {
                    Log("本次为手动模式（自检/演练/校准），截图保留: " + RunShotDir);
                    return;
                }

                if ((BetterGiStarted || StopBeforeBetterGi) && !WatchdogFired && !Cfg.ShotKeepOnSuccess)
                {
                    try
                    {
                        Directory.Delete(RunShotDir, true);
                        Log("运行正常，已删除本次截图（只保留失败现场）");
                    }
                    catch
                    {
                        Log("删除本次截图失败，保留在: " + RunShotDir);
                    }
                    return;
                }

                string reason = WatchdogFired ? "BetterGI 提前退出，看门狗已介入" : ((BetterGiStarted || StopBeforeBetterGi) ? "运行正常（配置要求保留截图）" : "未启动 BetterGI，保留失败现场");
                try
                {
                    File.WriteAllText(Path.Combine(RunShotDir, "_result.txt"),
                        reason + Environment.NewLine + "时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        new UTF8Encoding(true));
                }
                catch { }
                Log("本次截图已保留: " + RunShotDir + "（" + reason + "）");
            }
            catch { }
        }

        private static void ScheduleFallbackShutdown(string reason)
        {
            if (FallbackScheduled) return;
            FallbackScheduled = true;

            if (!Cfg.FallbackShutdown)
            {
                Log("兜底关机已禁用");
                return;
            }

            Log("未启动 BetterGI，执行兜底关机: shutdown /s /f /t " + Cfg.FallbackShutdownDelay + "（" + reason + "）");
            try
            {
                var shutdownExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
                int lastExit = -1;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    // 上一次可能残留了排队中的关机，先取消再重新排（避免 1190 冲突）
                    if (attempt > 1)
                    {
                        try
                        {
                            var psiAbort = new ProcessStartInfo(shutdownExe, "/a");
                            psiAbort.UseShellExecute = false;
                            psiAbort.CreateNoWindow = true;
                            var pa = Process.Start(psiAbort);
                            if (pa != null) pa.WaitForExit(5000);
                            Log("重试前已取消排队的关机");
                            Thread.Sleep(1500);
                        }
                        catch (Exception aex) { Log("取消关机失败: " + aex.Message); }
                    }

                    var psi = new ProcessStartInfo(shutdownExe, "/s /f /t " + Cfg.FallbackShutdownDelay);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    var p = Process.Start(psi);
                    if (p == null)
                    {
                        Log("关机命令没有返回进程对象（第 " + attempt + " 次）");
                        continue;
                    }
                    if (!p.WaitForExit(8000))
                    {
                        Log("关机命令 8 秒未返回（第 " + attempt + " 次）");
                        continue;
                    }
                    string err = "";
                    try { err = p.StandardError.ReadToEnd(); } catch { }
                    lastExit = p.ExitCode;
                    Log("关机命令退出码 " + p.ExitCode + "（第 " + attempt + " 次）" + (string.IsNullOrWhiteSpace(err) ? "" : "，错误输出: " + err.Trim()));
                    if (p.ExitCode == 0) break;
                }
                if (lastExit != 0)
                {
                    Log("警告: 兜底关机可能没有排上（最后退出码 " + lastExit + "），请检查系统关机策略");
                }

                try
                {
                    var flagPath = Path.Combine(LogDir, "fallback_shutdown_pending.flag");
                    File.WriteAllText(flagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new UTF8Encoding(true));
                    Log("已写入兜底关机标记: " + flagPath);
                }
                catch (Exception mex)
                {
                    Log("写兜底关机标记失败: " + mex.Message);
                }
            }
            catch (Exception ex)
            {
                Log("关机命令执行失败: " + ex.Message);
            }
        }

        private static void ClearStaleShutdownMarker()
        {
            try
            {
                string marker = Path.Combine(LogDir, "fallback_shutdown_pending.flag");
                if (!File.Exists(marker)) return;
                Log("检测到上次遗留的兜底关机标记，尝试 shutdown /a");
                var psi = new ProcessStartInfo("shutdown", "/a");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(3000);
                File.Delete(marker);
            }
            catch { }
        }

        #endregion

        #region 自检 / 校准

        private static void SelfTest()
        {
            LogBlank();
            Log("========== 自检 ==========");
            Log("版本: " + Version + "   .NET: " + Environment.Version + "   64位: " + Environment.Is64BitProcess);
            Log("管理员: " + IsAdmin() + "   会话: " + Process.GetCurrentProcess().SessionId);
            Log("--- 显示环境 ---");
            try
            {
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                {
                    Log("  屏幕: " + s.DeviceName + " " + s.Bounds.Width + "x" + s.Bounds.Height + " @(" + s.Bounds.X + "," + s.Bounds.Y + ") 主屏=" + s.Primary);
                }
            }
            catch (Exception ex) { Log("  枚举屏幕失败: " + ex.Message); }
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Log("  桌面 DPI: " + g.DpiX + " x " + g.DpiY);
                }
            }
            catch { }
            Log("配置: " + ConfigPath());
            Log("日志: " + LogFile);
            Log("截图根目录: " + ShotDir);
            Log("本次截图: " + RunShotDir);
            Log("OCR: " + (Ocr == null ? "不可用" : Ocr.RecognizerLanguage.LanguageTag));

            Log("--- 路径 ---");
            Log("启动器: " + File.Exists(Cfg.LauncherPath) + "  " + Cfg.LauncherPath);
            Log("游戏:   " + File.Exists(Cfg.GameExePath) + "  " + Cfg.GameExePath);
            Log("config: " + File.Exists(Cfg.GameConfigIni) + "  " + Cfg.GameConfigIni);
            Log("BGI:    " + File.Exists(Cfg.BetterGiPath) + "  " + Cfg.BetterGiPath);
            Log("游戏版本: " + ReadGameVersion());

            Log("--- 可见窗口 ---");
            foreach (var w in EnumTopWindows())
            {
                Log("  pid=" + w.Pid + "  proc=" + w.Proc + "  " + w.Width + "x" + w.Height + "  class=" + w.Class + "  title=" + w.Title);
            }

            var launcher = FindLauncherWindow();
            if (launcher != null)
            {
                Log("--- 启动器窗口: " + launcher.Title + " ---");
                var shot = CaptureForOcr(launcher.Handle, "selftest_launcher");
                var lines = OcrShot(shot);
                DumpOcr(lines, "selftest_launcher");
                Log("OCR 行数: " + lines.Count);
                Log("OCR 文本: " + JoinTexts(lines, 15));
                Log("截图: " + (shot == null ? "(失败)" : shot.Path));
            }
            else
            {
                Log("--- 未找到启动器窗口 ---");
            }

            var game = FindGameWindow();
            if (game != null)
            {
                Log("--- 原神窗口: " + game.Title + " ---");
                var shot = CaptureForOcr(game.Handle, "selftest_game");
                var lines = OcrShot(shot);
                DumpOcr(lines, "selftest_game");
                Log("OCR 行数: " + lines.Count);
                Log("OCR 文本: " + JoinTexts(lines, 15));
                Log("截图: " + (shot == null ? "(失败)" : shot.Path));
            }
            else
            {
                Log("--- 未找到原神窗口（可能不在当前会话）---");
            }
            Log("========== 自检结束 ==========");
        }

        private static void TestWindow(string titlePart)
        {
            Log("查找标题包含“" + titlePart + "”的窗口……");
            WinInfo target = null;
            foreach (var w in EnumTopWindows())
            {
                if (w.Title.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0) { target = w; break; }
            }
            if (target == null)
            {
                Log("没找到窗口");
                return;
            }
            Log("窗口: " + target.Title + "  pid=" + target.Pid + "  " + target.Width + "x" + target.Height);
            EnsureForeground(target.Handle);
            var shot = Capture(target.Handle, "test_window");
            if (shot == null) { Log("截图失败"); return; }
            var lines = OcrShot(shot);
            DumpOcr(lines, "test_window");
            Log("OCR 行数: " + lines.Count);
            foreach (var l in lines)
                Log("  (" + (int)l.X + "," + (int)l.Y + ") " + (int)l.W + "x" + (int)l.H + "  " + l.Text);
            Log("截图: " + shot.Path);
        }

        private static void Calibrate()
        {
            Log("========== 协议按钮坐标校准 ==========");
            Log("请先手动让原神显示出“用户协议/隐私政策”弹窗。");
            var w = WaitWindow(FindGameWindow, 60, null);
            if (w == null) { Log("未找到原神窗口"); return; }
            Log("原神窗口: " + w.Title + " " + w.Width + "x" + w.Height);
            EnsureForeground(w.Handle);

            Native.RECT rc;
            if (!Native.GetClientRect(w.Handle, out rc)) { Log("取客户区失败"); return; }
            int cw = rc.Right - rc.Left;
            int ch = rc.Bottom - rc.Top;
            Log("客户区: " + cw + "x" + ch);
            Log("把鼠标移到“同意并继续”按钮正中心，5 秒后取样……");
            for (int i = 5; i >= 1; i--) { Log("  " + i); Thread.Sleep(1000); }

            Native.POINT p;
            if (!Native.GetCursorPos(out p)) { Log("读取鼠标失败"); return; }
            var cp = new Native.POINT();
            cp.X = p.X;
            cp.Y = p.Y;
            if (!Native.ScreenToClient(w.Handle, ref cp)) { Log("坐标转换失败"); return; }
            double rx = Math.Round((double)cp.X / cw, 4);
            double ry = Math.Round((double)cp.Y / ch, 4);
            Log("客户区坐标: " + cp.X + "," + cp.Y);
            Log("相对比例: X=" + rx + "  Y=" + ry);
            Log("请把下面两行填进 BetterGIAutoStart.config.json：");
            Log("  \"AgreementRatioX\": " + rx + ",");
            Log("  \"AgreementRatioY\": " + ry);
        }

        #endregion

        #region 杂项

        private static string JoinTexts(List<OcrLine> lines, int max)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var l in lines)
            {
                if (n++ >= max) break;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(l.Text);
            }
            return sb.ToString();
        }

        private static List<OcrLine> OcrLast(string name)
        {
            var w = FindLauncherWindow();
            if (w == null) return new List<OcrLine>();
            var shot = CaptureForOcr(w.Handle, name);
            return OcrShot(shot);
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (var a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool RelaunchElevated(string[] args)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var sb = new StringBuilder();
                foreach (var a in args) sb.Append('"').Append(a).Append("\" ");
                var psi = new ProcessStartInfo(exe, sb.ToString());
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log("提权失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 把最近一次自检的文本与关键结论写成 JSON，供配置界面的「运行自检」读取。
        /// 提权后父进程拿不到子进程的控制台输出，所以用文件交接。
        /// </summary>
        private static void WriteSelfTestJson(string path)
        {
            Log("写自检 JSON 开始: " + path);
            try
            {
                string text = "";
                try
                {
                    if (File.Exists(LogFile)) text = File.ReadAllText(LogFile, Encoding.UTF8);
                }
                catch { }

                const string marker = "========== 自检 ==========";
                int idx = text.LastIndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0) text = text.Substring(idx);
                if (text.Length > 20000) text = text.Substring(text.Length - 20000);

                var obj = new Dictionary<string, object>
                {
                    { "ok", true },
                    { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "admin", IsAdmin() },
                    { "launcher", File.Exists(Cfg.LauncherPath) },
                    { "game", File.Exists(Cfg.GameExePath) },
                    { "bettergi", File.Exists(Cfg.BetterGiPath) },
                    { "ini", File.Exists(Cfg.GameConfigIni) },
                    { "ocr", Ocr == null ? "" : Ocr.RecognizerLanguage.LanguageTag },
                    { "gameVersion", ReadGameVersion() },
                    { "configPath", ConfigPath() },
                    { "logPath", LogFile },
                    { "text", text }
                };

                File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(true));
                Log("写自检 JSON 完成: " + (File.Exists(path) ? new FileInfo(path).Length + " 字节" : "文件不存在"));
            }
            catch (Exception ex)
            {
                Log("写自检 JSON 失败: " + ex.Message);
            }
        }

        #endregion
    }
}
