using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BetterGIAutoStart
{
    internal sealed partial class ConfigShellForm
    {
        private sealed class BakRow
        {
            internal Label Info;
            internal FlatButton Restore;
            internal FlatButton Delete;
            internal string Path;
        }

        private FlatCard _bakCard;
        private readonly List<BakRow> _bakList = new List<BakRow>();
        private Label _bakEmpty;
        private Button _bakRefresh, _bakClean, _bakOpen;

        private string BackupPattern => Path.GetFileName(_snap.Path) + ".bak.*";

        private List<FileInfo> ListBackups()
        {
            try
            {
                var dir = Path.GetDirectoryName(_snap.Path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return new List<FileInfo>();
                var prefix = Path.GetFileName(_snap.Path) + ".bak.";
                return new DirectoryInfo(dir).GetFiles(BackupPattern)
                    .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.Name.Length > prefix.Length)
                    .OrderByDescending(f => f.LastWriteTime).ToList();
            }
            catch { return new List<FileInfo>(); }
        }

        private static string BackupStamp(string fileName)
        {
            // BetterGIAutoStart.config.json.bak.20261121_233842 → 2026-11-21 23:38:42
            int i = fileName.LastIndexOf(".bak.", StringComparison.Ordinal);
            if (i < 0) return "";
            var s = fileName.Substring(i + 5);
            if (s.Length == 15 && s[8] == '_')
                return s.Substring(0, 4) + "-" + s.Substring(4, 2) + "-" + s.Substring(6, 2) +
                       " " + s.Substring(9, 2) + ":" + s.Substring(11, 2) + ":" + s.Substring(13, 2);
            return s;
        }

        private void BuildBackupPage()
        {
            int y = 0;

            var tip = AddCard(null, y, Ui.Px(100));
            var t1 = MkLabel("每次保存都会自动留一份 .bak（最多 10 份）。恢复前会把当前配置再另存一份 .before-restore，所以恢复本身也能反悔。", 10f, Theme.TextSecondary, false);
            t1.SetBounds(Ui.Px(20), Ui.Px(16), ContentW - Ui.Px(40), Ui.Px(24));
            tip.Controls.Add(t1);
            var warn = MkLabel("⚠ 恢复会覆盖当前配置，未保存的修改会丢失。", 10f, Theme.Warning, true);
            warn.SetBounds(Ui.Px(20), Ui.Px(50), ContentW - Ui.Px(40), Ui.Px(24));
            tip.Controls.Add(warn);
            y += tip.Height + Ui.Px(12);

            var bar = AddCard(null, y, Ui.Px(66));
            _bakRefresh = new FlatButton("刷新", FlatButton.Kind.Standard);
            _bakClean = new FlatButton("清理旧备份（只留 5 份）", FlatButton.Kind.Standard);
            _bakOpen = new FlatButton("打开备份所在文件夹", FlatButton.Kind.Subtle);
            _bakRefresh.Click += (s, e) => { ReloadBackupList(); LayoutBackupPage(); };
            _bakClean.Click += (s, e) => { CleanupBackups(); ReloadBackupList(); LayoutBackupPage(); };
            _bakOpen.Click += (s, e) => OpenFolder(Path.GetDirectoryName(_snap.Path));
            bar.Controls.AddRange(new Control[] { _bakRefresh, _bakClean, _bakOpen });
            y += bar.Height + Ui.Px(12);

            _bakCard = AddCard(null, y, Ui.Px(80));
            _bakEmpty = MkLabel("（还没有备份）", 10f, Theme.TextTertiary, false);
            _bakEmpty.SetBounds(Ui.Px(20), Ui.Px(20), ContentW - Ui.Px(40), Ui.Px(24));
            _bakCard.Controls.Add(_bakEmpty);

            ReloadBackupList();
        }

        private void ReloadBackupList()
        {
            if (_bakCard == null) return;
            foreach (var r in _bakList)
            {
                _bakCard.Controls.Remove(r.Info);
                _bakCard.Controls.Remove(r.Restore);
                _bakCard.Controls.Remove(r.Delete);
                r.Info.Dispose(); r.Restore.Dispose(); r.Delete.Dispose();
            }
            _bakList.Clear();

            foreach (var f in ListBackups())
            {
                var row = new BakRow { Path = f.FullName };
                row.Info = MkLabel(BackupStamp(f.Name) + "    " + (f.Length / 1024.0).ToString("0.0") + " KB  ·  " + f.Name.Substring(f.Name.LastIndexOf(".bak.", StringComparison.Ordinal) + 5), 9.5f, Theme.TextPrimary, false);
                row.Restore = new FlatButton("恢复", FlatButton.Kind.Standard);
                row.Delete = new FlatButton("删除", FlatButton.Kind.Subtle);
                var path = f.FullName;
                row.Restore.Click += (s, e) => RestoreBackup(path);
                row.Delete.Click += (s, e) => { DeleteBackup(path); ReloadBackupList(); LayoutBackupPage(); };
                _bakCard.Controls.Add(row.Info);
                _bakCard.Controls.Add(row.Restore);
                _bakCard.Controls.Add(row.Delete);
                _bakList.Add(row);
            }

            _bakEmpty.Visible = _bakList.Count == 0;
            LayoutBackupPage();
        }

        private void LayoutBackupPage()
        {
            if (_bakCard == null) return;
            var host = _scroll ?? _pageHost;
            var cards = host.Controls.OfType<FlatCard>().ToList();
            if (cards.Count < 3) return;

            var tip = cards[0]; var bar = cards[1];
            tip.SetBounds(0, 0, ContentW, tip.Height);
            bar.SetBounds(0, tip.Bottom + Ui.Px(12), ContentW, bar.Height);

            int bx = Ui.Px(20), by = Ui.Px(19), bh = Ui.Px(28);
            foreach (var bb in new Control[] { _bakRefresh, _bakClean, _bakOpen })
            {
                int bw = TextRenderer.MeasureText(bb.Text, bb.Font).Width + Ui.Px(36);
                bb.SetBounds(bx, by, bw, bh);
                bx += bw + Ui.Px(10);
            }

            int rowH = Ui.Px(36), y0 = Ui.Px(16);
            _bakEmpty.SetBounds(Ui.Px(20), y0, ContentW - Ui.Px(40), rowH);

            int y = y0;
            int btnH = Ui.Px(26);
            foreach (var r in _bakList)
            {
                int wRestore = TextRenderer.MeasureText(r.Restore.Text, r.Restore.Font).Width + Ui.Px(32);
                int wDelete = TextRenderer.MeasureText(r.Delete.Text, r.Delete.Font).Width + Ui.Px(32);
                r.Delete.SetBounds(ContentW - Ui.Px(20) - wDelete, y, wDelete, btnH);
                r.Restore.SetBounds(ContentW - Ui.Px(20) - wDelete - Ui.Px(8) - wRestore, y, wRestore, btnH);
                r.Info.SetBounds(Ui.Px(20), y + Ui.Px(4), ContentW - Ui.Px(20) - wDelete - Ui.Px(8) - wRestore - Ui.Px(16), btnH);
                y += rowH;
            }

            _bakCard.SetBounds(0, bar.Bottom + Ui.Px(12), ContentW, Math.Max(Ui.Px(64), y + Ui.Px(14)));
        }

        private void RestoreBackup(string bakPath)
        {
            if (MessageBox.Show(this,
                    "要把这份备份恢复到当前配置吗？\n\n" + Path.GetFileName(bakPath) +
                    "\n\n当前配置会先另存一份 .before-restore。",
                    "恢复备份", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                string safety = _snap.Path + ".before-restore." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (File.Exists(_snap.Path)) File.Copy(_snap.Path, safety, true);
                File.Copy(bakPath, _snap.Path, true);

                var re = ConfigStore.Load(_snap.Path);
                if (re.Config == null)
                {
                    UpdateStatus("✘ 备份文件读不出来，已放弃替换：" + re.ParseError, false, true);
                    File.Copy(safety, _snap.Path, true);
                    return;
                }
                _snap = re;
                _cfg = re.Config;
                _dirty = false;
                if (_rail != null) _rail.Selected = 0;
                ShowPage(0);
                _dirty = false;
                UpdateStatus("✔ 已恢复这份备份，原文件另存为 .before-restore", false, false);
            }
            catch (Exception ex)
            {
                UpdateStatus("✘ 恢复失败：" + ex.Message, false, true);
            }
        }

        private void DeleteBackup(string bakPath)
        {
            if (MessageBox.Show(this, "删除这份备份？\n\n" + Path.GetFileName(bakPath), "删除备份",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(bakPath); UpdateStatus("已删除 " + Path.GetFileName(bakPath), false, false); }
            catch (Exception ex) { UpdateStatus("✘ 删除失败：" + ex.Message, false, true); }
        }

        private void CleanupBackups()
        {
            var list = ListBackups();
            if (list.Count <= 5)
            {
                UpdateStatus("当前只有 " + list.Count + " 份备份，不需要清理", false, false);
                return;
            }
            int n = 0;
            try { foreach (var f in list.Skip(5)) { f.Delete(); n++; } }
            catch (Exception ex) { UpdateStatus("✘ 清理时出错：" + ex.Message, false, true); return; }
            UpdateStatus("已清理 " + n + " 份旧备份，保留最近 5 份", false, false);
        }
    }
}
