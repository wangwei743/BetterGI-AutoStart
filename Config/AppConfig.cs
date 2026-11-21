using System;

namespace BetterGIAutoStart
{
    /// <summary>
    /// 全部配置项。原本内联在 Program.cs 里，为配置界面抽出成独立文件（纯搬家，成员与默认值一字未改）。
    /// 注意：属性名就是 JSON 键名，改名等于改配置格式，必须同步迁移旧配置文件。
    /// </summary>
    internal sealed class AppConfig
    {
        public string LauncherPath { get; set; } = @"D:\miHoYo Launcher\launcher.exe";
        public string LauncherProcessName { get; set; } = "launcher";
        public string GameExePath { get; set; } = @"E:\Genshin Impact Game\YuanShen.exe";
        public string GameConfigIni { get; set; } = @"E:\Genshin Impact Game\config.ini";
        public string GameProcessName { get; set; } = "YuanShen";
        public string BetterGiPath { get; set; } = @"E:\BetterGI\BetterGI.exe";
        public string BetterGiArgs { get; set; } = "startOneDragon";
        /// <summary>截图目录；留空表示 exe 同目录下的 shots</summary>
        public string ShotDirectory { get; set; } = "";
        /// <summary>截图保留天数，超过就自动删除</summary>
        public int ShotRetentionDays { get; set; } = 30;
        /// <summary>截图目录总容量上限（MB），超了从最旧的删</summary>
        public int ShotMaxTotalMB { get; set; } = 500;
        /// <summary>成功时是否也保留截图；默认只保留失败现场</summary>
        public bool ShotKeepOnSuccess { get; set; } = false;
        /// <summary>看门狗：BetterGI 退出后若系统没在关机，就安排兜底关机</summary>
        public bool WatchdogEnabled { get; set; } = true;
        /// <summary>看门狗轮询间隔（秒）</summary>
        public int WatchdogPollSeconds { get; set; } = 30;
        /// <summary>看门狗触发后的关机延时（秒）</summary>
        public int WatchdogShutdownDelay { get; set; } = 300;
        /// <summary>系统启动后至少等这么多分钟再跑，避开开机时的更新/初始化</summary>
        public int MinUptimeMinutes { get; set; } = 10;
        /// <summary>启动器彻底不可用时，是否允许直接启动原神本体；默认关闭（会失去更新判断）</summary>
        public bool AllowDirectGameFallback { get; set; } = false;
        /// <summary>左侧“原神”页签的客户区相对位置（1920x1080 下约 50,903）</summary>
        public double GenshinTabRatioX { get; set; } = 0.026;
        public double GenshinTabRatioY { get; set; } = 0.836;
        /// <summary>协议按钮找不到时是否按比例盲点（绝对坐标兜底）；默认关闭</summary>
        public bool AgreementRatioFallback { get; set; } = false;

        public int PollSeconds { get; set; } = 5;
        public int LauncherStartTimeout { get; set; } = 180;
        public int LauncherStateTimeout { get; set; } = 240;
        public int UpdateTimeout { get; set; } = 1800;
        /// <summary>点击启动器自身更新后，等待其重启的秒数</summary>
        public int LauncherSelfUpdateWaitSeconds { get; set; } = 60;
        public int GameProcessTimeout { get; set; } = 300;
        public int GameWindowTimeout { get; set; } = 120;
        public int AgreementTimeout { get; set; } = 180;
        public int AgreementVerifyRounds { get; set; } = 6;
        public double AgreementRatioX { get; set; } = 0.50;
        public double AgreementRatioY { get; set; } = 0.82;
        public int AgreementRelativeDelay { get; set; } = 20;
        public int MaxRelativeClicks { get; set; } = 3;
        public int GameEntryTimeout { get; set; } = 90;
        public bool AutoClickGameEntry { get; set; } = true;
        public int GameSettleSeconds { get; set; } = 25;
        public int BetterGiVerifySeconds { get; set; } = 20;
        public bool FallbackShutdown { get; set; } = true;
        public int FallbackShutdownDelay { get; set; } = 3600;
        public bool NewVersionCooldown { get; set; } = false;
        public bool CooldownOnlyMajor { get; set; } = true;

        public string[] KwStart { get; set; } = { "开始游戏" };
        public string[] KwUpdate { get; set; } = { "立即更新", "开始更新", "更新游戏", "下载更新", "更新资源", "开始下载" };
        public string[] KwIgnore { get; set; } = { "预下载", "检查更新", "更新公告", "更新说明", "更新内容", "版本更新", "补丁说明", "暂停更新", "继续更新", "暂停下载", "继续下载" };
        public string[] KwDanger { get; set; } = { "不同意", "取消", "拒绝", "跳过", "退出", "注销", "删除", "充值", "关闭" };
        public string[] KwAgree { get; set; } = { "同意并继续", "同意", "接受" };
        public string[] KwEntry { get; set; } = { "点击进入", "点击屏幕", "点击开始" };
        public string[] KwBusy { get; set; } = { "正在更新", "更新中", "下载中", "正在下载", "安装中", "正在安装", "解压中", "校验中", "准备中", "剩余时间", "已暂停" };
        public string[] KwPause { get; set; } = { "暂停更新", "暂停下载", "暂停" };
        /// <summary>出现这些说明启动器不在原神页面（别的游戏/正在修复），不能乱点</summary>
        public string[] KwOtherGame { get; set; } = { "校验并修复", "正在为您校验并下载", "星穹铁道", "星穹", "星神", "阿哈", "走近星穹", "货币战争", "崩坏" };
        public string[] KwError { get; set; } = { "更新失败", "下载失败", "安装失败", "校验失败", "网络异常", "无法连接", "磁盘空间不足", "启动失败", "发生错误" };
    }
}
