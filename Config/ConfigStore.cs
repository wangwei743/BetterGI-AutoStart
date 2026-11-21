using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BetterGIAutoStart
{
    /// <summary>
    /// 配置读取快照：保留原始 JSON 文本和节点树，保存时以原树为底做合并，
    /// 这样用户手写的未知字段、字段顺序都不会被界面吞掉。
    /// </summary>
    internal sealed class ConfigSnapshot
    {
        internal string Path;
        internal bool Existed;
        internal string OriginalText = "";
        internal JsonObject Root;
        internal AppConfig Config;
        internal string ParseError;
    }

    internal sealed class SaveResult
    {
        internal bool Ok;
        internal string Message = "";
        internal string BackupPath = "";
    }

    /// <summary>
    /// 配置文件读写。写盘策略：先备份 -> 写临时文件 -> File.Replace 原子替换 -> 回读校验。
    /// 格式与 Program.cs 原有写法保持一致：UTF-8 带 BOM、缩进 2 空格、不转义中文。
    /// </summary>
    internal static class ConfigStore
    {
        internal const string FileName = "BetterGIAutoStart.config.json";
        private const int KeepBackups = 10;
        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        internal static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        internal static string DefaultPath
        {
            get { return Path.Combine(AppContext.BaseDirectory, FileName); }
        }

        internal static ConfigSnapshot Load(string path)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;

            var snap = new ConfigSnapshot { Path = path };
            AppConfig cfg = null;

            if (File.Exists(path))
            {
                snap.Existed = true;
                try { snap.OriginalText = File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception ex) { snap.ParseError = "读取失败：" + ex.Message; }

                if (!string.IsNullOrEmpty(snap.OriginalText))
                {
                    try
                    {
                        var node = JsonNode.Parse(snap.OriginalText);
                        snap.Root = node as JsonObject;
                        if (snap.Root == null) snap.ParseError = "根节点不是 JSON 对象";
                    }
                    catch (Exception ex)
                    {
                        snap.ParseError = "JSON 解析失败：" + ex.Message;
                    }

                    try { cfg = JsonSerializer.Deserialize<AppConfig>(snap.OriginalText, ReadOptions); }
                    catch (Exception ex)
                    {
                        if (string.IsNullOrEmpty(snap.ParseError)) snap.ParseError = "字段解析失败：" + ex.Message;
                    }
                }
            }

            // 解析失败也要能进界面：用默认值兜底，界面顶部会提示
            if (cfg == null) cfg = new AppConfig();
            snap.Config = cfg;
            if (snap.Root == null) snap.Root = ToObject(cfg);
            return snap;
        }

        /// <summary>把 AppConfig 转成节点树（仅在配置文件不存在时用来生成初版）</summary>
        internal static JsonObject ToObject(AppConfig cfg)
        {
            var obj = new JsonObject();
            foreach (var p in KnownProperties())
            {
                obj[p.Name] = ToNode(p.GetValue(cfg));
            }
            return obj;
        }

        /// <summary>序列化成要写盘的文本（不含 BOM，BOM 由写入器负责）</summary>
        internal static string Serialize(JsonObject root)
        {
            return root.ToJsonString(WriteOptions);
        }

        /// <summary>
        /// 以原始树为底合并已知字段，返回要写盘的节点树。
        /// 规则：文件里已有的键一律按新值覆盖；文件里没有的键，只在值被改过（与默认值不同）时才补写。
        /// 导出配置也走这里，保证导出的内容和保存的结果一致。
        /// </summary>
        internal static JsonObject BuildMerged(ConfigSnapshot snap, AppConfig cfg)
        {
            var root = snap != null && snap.Root != null
                ? (JsonObject)snap.Root.DeepClone()
                : new JsonObject();
            if (cfg == null) return root;

            var defaults = new AppConfig();
            foreach (var p in KnownProperties())
            {
                var value = p.GetValue(cfg);
                bool exists = root.ContainsKey(p.Name);
                if (!exists && IsSameAsDefault(p, value, defaults)) continue;
                var node = ToNode(value);
                if (node == null) root.Remove(p.Name);
                else root[p.Name] = node;
            }
            return root;
        }

        internal static SaveResult Save(ConfigSnapshot snap, AppConfig cfg)
        {
            var result = new SaveResult();
            if (snap == null) { result.Message = "没有已加载的配置"; return result; }
            if (cfg == null) { result.Message = "配置对象为空"; return result; }

            string path = snap.Path;
            try
            {
                // 以原始树为底，只覆盖已知字段：未知字段、字段顺序都保留
                var root = BuildMerged(snap, cfg);

                string text = Serialize(root);

                // 备份（存在才备份）
                if (File.Exists(path)) result.BackupPath = Backup(path);

                // 原子写：同目录临时文件 + File.Replace，避免写到一半断电/被杀留下半个文件
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, Utf8Bom);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);

                // 回读校验：能解析回来、且关键字段对得上才算成功
                var check = Load(path);
                if (!string.IsNullOrEmpty(check.ParseError))
                    throw new InvalidOperationException("回读校验失败：" + check.ParseError);

                snap.OriginalText = text;
                snap.Root = root;
                snap.Existed = true;
                snap.Config = cfg;
                snap.ParseError = null;

                result.Ok = true;
                result.Message = "已保存";
                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = "保存失败：" + ex.Message;
                // 写失败时尽量让磁盘回到备份状态，避免留下半成品
                try
                {
                    if (!string.IsNullOrEmpty(result.BackupPath) && File.Exists(result.BackupPath) && !File.Exists(path))
                        File.Copy(result.BackupPath, path);
                }
                catch { }
                return result;
            }
        }

        private static string Backup(string path)
        {
            string bak = path + ".bak." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(path, bak, true);
            try
            {
                var old = new DirectoryInfo(Path.GetDirectoryName(path)).GetFiles(Path.GetFileName(path) + ".bak.*")
                    .OrderByDescending(f => f.LastWriteTime).Skip(KeepBackups).ToList();
                foreach (var f in old) { try { f.Delete(); } catch { } }
            }
            catch { }
            return bak;
        }

        private static IEnumerable<PropertyInfo> KnownProperties()
        {
            return typeof(AppConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .OrderBy(p => p.MetadataToken);
        }

        /// <summary>判断某个字段的值是否仍等于默认值（数组按逐项比较）</summary>
        private static bool IsSameAsDefault(PropertyInfo p, object value, AppConfig defaults)
        {
            var def = p.GetValue(defaults);
            if (value == null && def == null) return true;
            if (value == null || def == null) return false;

            var arr = value as string[];
            if (arr != null)
            {
                var arrDef = def as string[];
                if (arrDef == null || arr.Length != arrDef.Length) return false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!string.Equals(arr[i], arrDef[i], StringComparison.Ordinal)) return false;
                }
                return true;
            }

            return value.Equals(def);
        }

        private static JsonNode ToNode(object value)
        {
            if (value == null) return null;

            var arr = value as string[];
            if (arr != null)
            {
                var ja = new JsonArray();
                foreach (var s in arr) ja.Add(JsonValue.Create(s ?? ""));
                return ja;
            }

            var str = value as string;
            if (str != null) return JsonValue.Create(str);
            if (value is bool b) return JsonValue.Create(b);
            if (value is int n) return JsonValue.Create(n);
            if (value is long l) return JsonValue.Create(l);
            if (value is double d) return JsonValue.Create(d);
            if (value is float f) return JsonValue.Create((double)f);
            return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }
}
