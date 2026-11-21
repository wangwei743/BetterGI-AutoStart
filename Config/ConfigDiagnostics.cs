using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    /// <summary>
    /// 生成"诊断包"：把排查问题需要的材料打成一个 zip，用户直接发回来即可。
    /// 只读，不改配置、不启动游戏；全程直接在 zip 里写条目，不落临时目录
    /// （受限环境里 %TEMP% 可能不可写）。
    /// </summary>
    internal static class ConfigDiagnostics
    {
        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

        /// <summary>写出 zip，返回条目数；失败抛异常由调用方提示。</summary>
        internal static int Export(string zipPath, string selfTestRaw)
        {
            string dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            int n = 0;
            using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddText(zip, "说明.txt", Readme()); n++;
                AddText(zip, "环境.txt", EnvironmentInfo()); n++;
                AddText(zip, "自检.txt", string.IsNullOrEmpty(selfTestRaw) ? "（本次没有跑自检）" : selfTestRaw); n++;
                AddText(zip, "模板.txt", TemplateList()); n++;
                AddText(zip, "进程.txt", ProcessList()); n++;

                string cfg = ConfigStore.DefaultPath;
                if (File.Exists(cfg))
                {
                    AddFile(zip, "配置/BetterGIAutoStart.config.json", cfg);
                    n++;
                }
                n += AddLogs(zip);
                if (AddScreen(zip)) n++;
            }
            return n;
        }

        private static void AddText(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            using (var w = new StreamWriter(s, Utf8Bom))
                w.Write(text ?? "");
        }

        private static void AddFile(ZipArchive zip, string name, string path)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                src.CopyTo(s);
        }

        private static string Readme()
        {
            var sb = new StringBuilder();
            sb.AppendLine("BetterGI-AutoStart 诊断包");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("包里有什么：");
            sb.AppendLine("  说明.txt    本文件");
            sb.AppendLine("  环境.txt    系统版本、屏幕、DPI、程序与配置路径");
            sb.AppendLine("  自检.txt    最近一次 --self-test 的完整输出");
            sb.AppendLine("  配置\\      正在用的配置（只有路径和开关，没有账号信息）");
            sb.AppendLine("  日志\\      最近三个运行日志的尾部");
            sb.AppendLine("  模板.txt    templates 里的识别模板清单（名字、尺寸、大小）");
            sb.AppendLine("  进程.txt    当时在跑的原神/启动器/BetterGI 进程");
            sb.AppendLine("  屏幕.jpg    当时的屏幕截图（识别出问题时这张最有用）");
            sb.AppendLine();
            sb.AppendLine("怎么反馈：把整个 zip 发到仓库 Issues，附一句现象就行。");
            sb.AppendLine("https://github.com/wangwei743/BetterGI-AutoStart/issues");
            return sb.ToString();
        }

        private static string EnvironmentInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("程序版本：" + Application.ProductVersion);
            sb.AppendLine("程序路径：" + Application.ExecutablePath);
            sb.AppendLine("当前目录：" + Environment.CurrentDirectory);
            sb.AppendLine("配置文件：" + ConfigStore.DefaultPath);
            sb.AppendLine();
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine("64 位系统：" + Environment.Is64BitOperatingSystem);
            sb.AppendLine("64 位进程：" + Environment.Is64BitProcess);
            sb.AppendLine(".NET：" + Environment.Version);
            sb.AppendLine("管理员：" + IsAdmin());
            try { sb.AppendLine("开机时长：" + TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm")); } catch { }
            sb.AppendLine();
            try
            {
                var scr = Screen.PrimaryScreen;
                if (scr != null)
                    sb.AppendLine("主屏：" + scr.Bounds.Width + "x" + scr.Bounds.Height
                                  + "（工作区 " + scr.WorkingArea.Width + "x" + scr.WorkingArea.Height + "）");
                sb.AppendLine("显示器数量：" + Screen.AllScreens.Length);
            }
            catch (Exception ex) { sb.AppendLine("屏幕信息读取失败：" + ex.Message); }
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    sb.AppendLine("屏幕 DPI：" + g.DpiX + " x " + g.DpiY);
            }
            catch (Exception ex) { sb.AppendLine("DPI 读取失败：" + ex.Message); }
            return sb.ToString();
        }

        private static bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static string TemplateList()
        {
            var sb = new StringBuilder();
            string dir = Path.Combine(AppContext.BaseDirectory, "templates");
            sb.AppendLine("模板目录：" + dir);
            if (!Directory.Exists(dir))
            {
                sb.AppendLine("目录不存在 —— 这本身就是问题（识别一定失败）");
                return sb.ToString();
            }
            var files = Directory.GetFiles(dir, "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            sb.AppendLine("共 " + files.Length + " 张");
            sb.AppendLine();
            foreach (var f in files)
            {
                string dim;
                try
                {
                    using (var img = Image.FromFile(f)) dim = img.Width + "x" + img.Height;
                }
                catch { dim = "读不出尺寸"; }
                sb.AppendLine(Path.GetFileName(f).PadRight(38) + dim.PadLeft(10) + "   "
                              + new FileInfo(f).Length.ToString().PadLeft(8) + " 字节");
            }
            return sb.ToString();
        }

        private static string ProcessList()
        {
            var sb = new StringBuilder();
            string[] keys = { "YuanShen", "GenshinImpact", "launcher", "HYP", "BetterGI", "BetterGIAutoStart" };
            sb.AppendLine("名称                      进程 ID   内存(MB)   路径");
            int found = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName;
                    bool hit = false;
                    foreach (var k in keys)
                        if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    if (!hit) continue;
                    string path;
                    try { path = p.MainModule != null ? p.MainModule.FileName : ""; } catch { path = "（无权读取）"; }
                    sb.AppendLine(name.PadRight(24) + p.Id.ToString().PadLeft(8) + "  "
                                  + (p.WorkingSet64 / 1024 / 1024).ToString().PadLeft(7) + "   " + path);
                    found++;
                }
                catch { }
            }
            if (found == 0) sb.AppendLine("（当前没有原神/启动器/BetterGI 在运行）");
            return sb.ToString();
        }

        private static int AddLogs(ZipArchive zip)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(dir)) return 0;
            var files = new DirectoryInfo(dir).GetFiles("*.log");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            int n = 0;
            foreach (var f in files)
            {
                if (n >= 3) break;
                try
                {
                    AddText(zip, "日志/" + f.Name, TailOfFile(f.FullName, 200 * 1024));
                    n++;
                }
                catch { }
            }
            return n;
        }

        /// <summary>只取文件尾部，避免把几十兆的日志塞进包里。</summary>
        private static string TailOfFile(string path, int maxChars)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long take = Math.Min(fs.Length, maxChars * 3L);
                fs.Seek(-take, SeekOrigin.End);
                var buf = new byte[take];
                int read = fs.Read(buf, 0, buf.Length);
                string text = Encoding.UTF8.GetString(buf, 0, read);
                if (take < fs.Length)
                    text = "……（前面省略，只保留最后约 200KB）……" + Environment.NewLine + text;
                return text;
            }
        }

        /// <summary>屏幕截图。用 JPEG 而不是 PNG：4K 下 PNG 能有 8MB，压不进 Issues。</summary>
        private static bool AddScreen(ZipArchive zip)
        {
            try
            {
                var bounds = Screen.PrimaryScreen.Bounds;
                using (var full = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (var g = Graphics.FromImage(full))
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, full.Size);

                    // 超过 2560 宽就等比缩一下，像素细节对诊断够用了
                    const int maxW = 2560;
                    Bitmap shot = full;
                    bool shrunk = false;
                    if (full.Width > maxW)
                    {
                        int w = maxW, h = (int)Math.Round(full.Height * (maxW / (double)full.Width));
                        shot = new Bitmap(full, new Size(w, h));
                        shrunk = true;
                    }
                    try
                    {
                        var e = zip.CreateEntry("屏幕.jpg", CompressionLevel.Optimal);
                        using (var s = e.Open())
                        {
                            var codec = GetJpegCodec();
                            if (codec != null)
                            {
                                var ps = new System.Drawing.Imaging.EncoderParameters(1);
                                ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                    System.Drawing.Imaging.Encoder.Quality, 88L);
                                shot.Save(s, codec, ps);
                            }
                            else
                            {
                                shot.Save(s, ImageFormat.Jpeg);
                            }
                        }
                    }
                    finally
                    {
                        if (shrunk) shot.Dispose();
                        else shot.Dispose();
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static System.Drawing.Imaging.ImageCodecInfo GetJpegCodec()
        {
            try
            {
                foreach (var c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            }
            catch { }
            return null;
        }
    }
}
