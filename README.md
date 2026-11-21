# 原神无人值守自动化（BetterGIAutoStart）

> 凌晨无人值守：自动拉起米哈游启动器 → 判断「更新 / 开始游戏」→ 处理更新与协议 → 进入游戏 → 启动 BetterGI 一条龙 → 完成后自动关机。
>
> 技术栈：C# / .NET 10 (WinForms) + OpenCvSharp（模板匹配）+ Windows.Media.Ocr（辅助识别）
> 版本：v1.0（2026-11-20）

---

## 一、它能做什么

一句话：**在没人看着的情况下，把"打开启动器 → 进游戏 → 跑一条龙 → 关机"整条流程走完。**

覆盖的真实场景：

| 场景 | 行为 |
| --- | --- |
| 有新版本 | 识别「更新游戏」→ 点击 → 等待下载/安装完成 → 再点「开始游戏」 |
| 启动器自己更新 | 识别「立即更新」→ 点击 → 等 60 秒重启 → 重新判断状态 |
| 启动器停在别的游戏页 | 模板定位左侧「原神」页签 → 点击切换 → 复检 |
| 启动器白屏 | 检测整屏全白 → 最小化/还原强制重绘 → 重试 |
| 有用户协议弹窗 | 模板/OCR 找到「同意」→ 点击 → 复核弹窗消失 |
| 标题界面 | 找到「点击进入」→ 点击 → 确认进入主界面 |
| 启动器卡死 | 检测"有进程没窗口"→ 提权清理残留 → 重启启动器 |
| 任何一步失败 | 记录原因 + 保留截图现场 → 1 小时后兜底关机 |
| BetterGI 中途挂掉 | 看门狗发现进程消失 → 5 分钟后兜底关机 |
| 当天已成功过 | 后续触发点直接跳过，不会重复跑 |

---

## 二、运行环境

- Windows 11（本机 26100）
- 显示：实测过 **3840×2160 @150% DPI** 和 **1920×1080 @96 DPI**（虚显器/欺骗器）
- 账户：需要有**米哈游启动器登录态**的那个 Windows 账户，且开机自动登录到桌面
- 游戏：原神国服，1920×1080 窗口化或全屏均可（推荐 1080p，BetterGI 以此为基准）
- BetterGI：`E:\BetterGI\BetterGI.exe`
- 关掉锁屏/休眠/屏保（程序启动时会关屏保，但睡眠策略需要自己设）

---

## 三、部署

### 3.1 文件结构

```
BetterGIAutoStart\
├─ BetterGIAutoStart.exe              主程序（文件夹部署，不要单文件打包）
├─ *.dll / runtimes\            .NET 运行时 + OpenCV 原生库
├─ BetterGIAutoStart.config.json      全部可调参数
├─ templates\                   按钮/页面模板（按分辨率自动选择）
├─ logs\
│   ├─ BetterGIAutoStart_YYYYMMDD.log 每天一个日志
│   ├─ ran_YYYYMMDD.flag        当天成功标记（防重复）
│   └─ fallback_shutdown_pending.flag  兜底关机标记
└─ shots\
    └─ YYYYMMDD_HHmmss\         失败现场（成功会自动删除）
```

### 3.2 编译

```bat
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false -p:DebugType=none -o dist
```

> **重要**：必须用 `PublishSingleFile=false`（文件夹部署）。
> 单文件版本每次启动要把 90MB 解压到临时目录，实测出现过解压失败（.NET 主机错误码 `0x800080A0`），进程在写日志之前就退出了。

### 3.3 计划任务

- 用户名：与启动器登录态一致的账户
- 运行方式：**只在用户登录时运行**（Interactive）
- 权限：**最低权限**（Limited，不要用"最高权限"）
- 触发：04:30 / 04:45 / 05:00（三次机会，`IgnoreNew` 防并发）

```powershell
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$action = New-ScheduledTaskAction -Execute 'E:\DSH\BetterGIAutoStart\dist\BetterGIAutoStart.exe' -WorkingDirectory 'E:\DSH\BetterGIAutoStart\dist'
$triggers = @(
  (New-ScheduledTaskTrigger -Daily -At '04:30'),
  (New-ScheduledTaskTrigger -Daily -At '04:45'),
  (New-ScheduledTaskTrigger -Daily -At '05:00')
)
Register-ScheduledTask -TaskName 'BetterGIAutoStartDaily' -Action $action -Trigger $triggers -Principal $principal -Force
```

> **重要**：exe 路径**不能**存在"兼容性 → 始终以管理员身份运行"的标记（注册表
> `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers`）。
> 有这条标记时，计划任务会用服务令牌强制提权，米哈游启动器（HYP）在这种令牌下初始化
> 注册表管理器失败，弹 `Failed to initialize registry manager` 且不出主窗口——这是本项目
> 排查最久的一个坑。

---

## 四、识别策略：模板优先，OCR 辅助

### 4.1 总体原则

- **模板匹配（OpenCvSharp matchTemplate）负责"找位置"**
- **OCR 只负责"读那几颗字"**（例如按钮是「开始游戏」还是「更新游戏」）
- **任何点击坐标都来自识别结果**，代码里没有任何写死的绝对坐标

### 4.2 分辨率适配

模板按分辨率存两种：

```
name_1920x1080.png   尺寸专用模板（优先级最高，scale≈1.0）
name.png             通用模板（0.4~2.0 宽范围缩放搜索）
```

这样 4K 采的模板在 1080p 下按 0.5 倍也能匹配，换分辨率不用重新采。

### 4.3 各环节识别方式

| 环节 | 主识别 | 兜底 |
| --- | --- | --- |
| 当前在哪个游戏页 | 老版启动器读 CEF 日志里的页面 hash（`hk4e_cn`=原神）；新版读不到就模板定位页签点击 | 页面模板匹配 |
| 主按钮状态 | 模板定位黄色胶囊 → 裁剪区域 OCR 读文字 | 整图 OCR |
| 协议按钮 | 模板 `auto_btn_agree` | OCR「同意/同意并继续/接受」 |
| 标题界面 | 模板 `auto_btn_entry` | OCR「点击进入/点击屏幕」 |
| 启动器白屏 | 整屏亮度均值 >235 且方差极小 → 判定白屏 | — |

### 4.4 模板库

```
launcher_tab_genshin*.png      左侧原神页签（切页用）
launcher_page_genshin*.png     原神页标识（老版启动器页面判断）
launcher_btn_update.png        更新游戏按钮
auto_btn_start*.png            开始游戏按钮
auto_btn_agree.png             协议「同意」按钮（来自真实弹窗）
auto_btn_entry*.png            标题界面「点击进入」
auto_btn_update_*.png          更新按钮（运行时自动采集）
```

模板会在识别成功时**自动采集**（不存在才采），换环境跑一次就能把当前环境的外观记下来。

---

## 五、完整流程

```
阶段一：启动启动器
  ├ 已有可见窗口 → 直接用
  ├ 有进程没窗口 → 判为卡死 → 提权清理 → 重新启动
  └ 启动 launcher.exe（45 秒等不到就重试，第二次改用 explorer.exe 方式）

阶段二：判断启动器状态
  ├ 确认在"原神"页面（hash/模板/页签点击）
  ├ 模板定位主按钮 + OCR 读字
  │   ├「立即更新」→ 点它 → 等 60 秒重启 → 重新判断
  │   ├「更新游戏」→ 点它 → 等待更新完成（识别下载中/暂停/进度）
  │   └「开始游戏」→ 继续
  └ 下载/校验进行中 → 只等待，绝不点击

阶段三：处理协议弹窗（默认 180 秒窗口）
  ├ 出现「同意」→ 点击 → 复核弹窗消失
  ├ 已在标题界面 → 跳过
  └ 检测到 UID/HUD（已进世界）→ 跳过

阶段三-2：标题界面
  └「点击进入」→ 点击 → 复核标题消失

阶段四：等待可操作
  └ 窗口稳定 25 秒 → 复核画面里有 UID/HUD
      └ 没有 → 补一轮"协议 + 点击进入"（兜底）

阶段五：启动 BetterGI
  └ Start-Process "E:\BetterGI\BetterGI.exe" startOneDragon
       → 写当天成功标记 → 启动看门狗

看门狗
  └ 每 30 秒检查 BetterGI；进程消失后等 30 秒，若系统没在关机 → shutdown /s /f /t 300
```

---

## 六、健壮性设计

| 机制 | 说明 |
| --- | --- |
| 前台硬校验 | 截图/点击前确认窗口真的在前台，否则最小化+还原强抢；抢不到就跳过本次操作，绝不对着别的窗口操作 |
| 白屏自愈 | 整屏全白（CEF 未渲染）→ 最小化/还原强制重绘后重试 |
| 鼠标避让 | 每次点击后把鼠标移到屏幕角落，避免 hover 改变按钮样式影响后续识别 |
| 残留清理 | 启动器家族进程（HYP/HYPHelper/launcher）有进程无窗口 → 提权结束 |
| 启动器自更新 | 「立即更新」单独处理，点击后等 60 秒重启再判断 |
| 开机等待 | 系统启动不足 `MinUptimeMinutes` 分钟先等待（默认 25），避开开机时更新/杀软启动 |
| 防重复 | 成功启动 BetterGI 后写 `ran_YYYYMMDD.flag`，当天后续触发直接跳过；`--force` 可绕过 |
| 多触发点 | 04:30/04:45/05:00 三次机会，`IgnoreNew` 保证同一时刻只有一个实例 |
| 兜底关机 | 任何一步失败 → `shutdown /s /f /t 3600`（绝对路径 + 冲突重试 + 退出码日志） |
| 看门狗 | BetterGI 中途退出且系统没在关机 → 5 分钟后兜底关机 |
| 成功不关机 | 只要 BetterGI 成功启动，本程序绝不主动关机 |
| 截图管理 | 按次分目录；成功即删、失败保留（含 `_result.txt`）；保留 30 天、上限 500 MB |

---

## 七、踩坑与修复记录

按发现顺序，每条都是真机复现过的：

### 1. PowerShell 脚本无 BOM 导致解析错误
PS 5.1 按 GBK 读无 BOM 文件，中文全乱、报 200+ 语法错误。→ 一律写 UTF-8 BOM。

### 2. 日志路径被覆盖导致初始化失败
配置区设了 `$LogFilePath`，脚本内部状态区又把它置 `$null`，初始化必然失败并触发兜底关机。
→ 删除内部覆盖。

### 3. OCR 读不出「开始游戏」
整图 OCR 读成 `0开始游`（最后一个字被吞）。→ 裁右下角 0.50~0.95、放大 2 倍再 OCR，
读出 `0开始游戏`。**这就是"模板/区域预处理"思路的起点**。

### 4. 单文件 exe 启动失败（计划任务 0x800080A0）
90MB 单文件每次启动要解压，任务上下文里解压失败，进程在写日志前退出，看起来像"任务没跑"。
→ 改为文件夹部署。

### 5. 启动器被关到托盘 / 停在星穹铁道页
HYP 是受保护的常驻进程，关窗口后进程还在；`launcher.exe` 见已有实例就秒退，而窗口查找只认
可见窗口 → 永远等不到。另外多游戏启动器会记住上次页面。
→ 加"有进程没窗口 → 清理重启"；页面判断从模板改为"CEF 日志 hash → 页签点击"。

### 6. 启动器白屏
CEF 偶发不渲染，整屏纯白，OCR/模板全废。→ 白屏检测 + 最小化/还原强制重绘。

### 7. 启动器被别的窗口盖住时乱点
截图走 `CopyFromScreen`，启动器不在前台时截到的是别的窗口——曾经把工具界面上的一行
「立即更新」当成按钮点了出去。→ 前台硬校验，不在前台就跳过，绝不盲操作。

### 8. 下载中被误点成暂停
更新按钮和"下载中"按钮是同一颗黄胶囊，`KwUpdate` 里含"下载"导致把进度文字识别成按钮。
→ 关键词收紧（只认「更新游戏/立即更新/更新资源」等），并且"下载中/更新中"优先判定为忙碌。

### 9. 开始/更新按钮无法用模板区分
两颗按钮形状完全一样，`launcher_btn_update` 在开始游戏页也能匹配到 0.926。
→ 模板只负责定位，按钮文字交给 OCR 读。

### 10. exe 被标记"始终以管理员身份运行"（最深的坑）
注册表 `AppCompatFlags\Layers` 里有一条 `RUNASADMIN`。于是计划任务无论如何配置都会用
服务令牌强制提权，而 HYP 在这种令牌下初始化注册表管理器失败，弹
`Failed to initialize registry manager` 且不出主窗口；手动双击走 UAC 提权却正常。
→ 删除该注册表项 + 任务改"最低权限" + exe 内部按需 UAC 自提权。

---

## 八、验证记录（真机）

| 日期 | 内容 | 结果 |
| --- | --- | --- |
| 11-01 | 首次完整跑通（启动器→游戏→BetterGI） | ✅ |
| 11-02 | 凌晨任务：启动器无窗口，兜底关机生效 | ⚠️ 失败但兜底正确 |
| 11-18 | 开机 5 分钟后运行，HYP 白屏/无窗口 | ⚠️ 暴露多个问题 |
| 11-19 | 4K 环境：启动器→协议（真实弹窗）→标题→BetterGI→关机 | ✅ 协议首次实测 |
| 11-20 11:44 | **虚显器 1280×720**：页签→开始→游戏→标题 | ✅ |
| 11-20 14:45 | **更新分支**：识别更新→点击→等 19 分钟→开始游戏 | ✅ |
| 11-20 15:14 | **协议弹窗**（另一账户）：模板命中→点击→弹窗消失 | ✅ |
| 11-20 15:05 | 更新完成后一条龙启动 | ✅ |

---

## 九、配置说明

`BetterGIAutoStart.config.json`（全部键都有默认值，缺省自动补）：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `LauncherPath` | `D:\miHoYo Launcher\launcher.exe` | 启动器路径 |
| `GameExePath` | `E:\Genshin Impact Game\YuanShen.exe` | 游戏主程序 |
| `GameProcessName` | `YuanShen` | 进程名（国际服改 GenshinImpact） |
| `BetterGiPath` / `BetterGiArgs` | `E:\BetterGI\BetterGI.exe` / `startOneDragon` | 参数不带横杠 |
| `MinUptimeMinutes` | 25 | 开机后至少等多少分钟 |
| `UpdateTimeout` | 3600 | 更新最多等多久（秒） |
| `LauncherStateTimeout` | 240 | 启动器状态识别超时 |
| `LauncherSelfUpdateWaitSeconds` | 60 | 启动器自更新后等待 |
| `AgreementTimeout` | 180 | 协议阶段最长等待 |
| `GameSettleSeconds` | 25 | 画面稳定多久算可操作 |
| `FallbackShutdown` / `FallbackShutdownDelay` | true / 3600 | 失败后关机 |
| `WatchdogEnabled` / `WatchdogPollSeconds` / `WatchdogShutdownDelay` | true / 30 / 300 | 看门狗 |
| `AllowDirectGameFallback` | false | 是否允许绕过启动器直接开游戏（会失去更新判断，默认关） |
| `AgreementRatioFallback` | false | 协议找不到按钮时是否按比例盲点（默认关） |
| `ShotRetentionDays` / `ShotMaxTotalMB` | 30 / 500 | 截图保留策略 |
| `ShotKeepOnSuccess` | false | 成功时是否也保留截图 |
| `Kw*` | 见文件 | 各类 OCR 关键词 |

---

## 九点五、图形化配置界面（`--config`）

不想手改 JSON，双击 `打开配置.bat`（或给快捷方式目标加 `--config`）：

```
dist\BetterGIAutoStart.exe --config
```

WinForms 自绘的 WinUI 风格界面，七个页面：

| 页面 | 作用 |
| --- | --- |
| 路径与程序 | 启动器 / 原神本体 / BetterGI 三个路径，带「✔ 已找到」实时校验 |
| 自动化行为 | 看门狗、兜底关机、直接启动原神三个开关 + 开机等待、看门狗轮询 |
| 时间与超时 | 13 个阶段超时（启动器、更新、协议、进入、稳定、兜底…） |
| 截图与日志 | 截图目录、保留天数与容量上限，外加打开日志/截图目录、按策略清理截图 |
| 关键词表 | 10 组识别词，实时冲突检测（危险词吞了开始按钮、忽略词吞了更新按钮等） |
| 高级（坐标） | 页签/协议按钮相对坐标、盲点兜底、进程名、config.ini 路径等 |
| 备份与恢复 | 列出历史 `.bak`，一键恢复 / 删除 / 清理（只留 5 份）；恢复前会另存一份 `.before-restore` |

四个要点：

1. **只加一个启动参数**。不带参数时行为与以前完全一致；`--config` 在提权、初始化 OCR、创建日志目录**之前**就返回，所以打开界面没有任何副作用。
2. **保存前先看 diff**。点「保存」先弹「即将改动 N 项」，确认后才写盘；没有改动直接提示"没有检测到改动，未写盘"。
3. **JSON 保真**。以原文件为底只改你动过的键，未知字段与键顺序全部保留（往返字节完全一致）。
4. **原子保存**。写 `.tmp` → `File.Replace` 原子替换 → 回读校验，成功前留一份 `.bak.yyyyMMdd_HHmmss`（最多 10 份）。

底部还有两个按钮：

- **运行自检**：提权跑一次 `--self-test`，结果用 JSON 文件交接回来，弹窗列出 7 条结论（三个路径 / config.ini / 管理员 / OCR / 游戏版本）并附完整自检原文。
- **试跑**：先问「跑到哪一步就停」——`只跑启动器`（`--launcher-only`，约 30 秒）或 `跑到游戏就绪`（`--stop-before-bettergi`，3–10 分钟）。两者都不会启动一条龙、不会关机。
- **导出诊断包**：自检结果窗口里点「导出诊断包…」，把环境信息、自检输出、当前配置、最近三个日志、模板清单和一张屏幕截图打成一个 zip（约 0.2MB）。出问题直接发这个，比截一堆图快。命令行也可以：`BetterGIAutoStart.exe --diagnostics 包.zip`。
## 十、命令行

```bat
BetterGIAutoStart.exe                       正常执行完整流程
BetterGIAutoStart.exe --dry-run             演练：只识别不点击、不启动、不关机
BetterGIAutoStart.exe --self-test           自检：权限/显示/DPI/路径/窗口/OCR
BetterGIAutoStart.exe --calibrate           校准协议按钮比例（已默认关闭盲点）
BetterGIAutoStart.exe --test-window 标题    截取指定窗口并 OCR
BetterGIAutoStart.exe --stop-before-bettergi  跑到 BetterGI 之前停（不关机）
BetterGIAutoStart.exe --force               忽略"当天已完成"标记，且不写标记
BetterGIAutoStart.exe --skip-uptime         跳过开机等待
BetterGIAutoStart.exe --match-file 图片 --match-name 模板   离线模板匹配测试
BetterGIAutoStart.exe --click-template 模板 模板定位并点击（调试用）
BetterGIAutoStart.exe --click-client X,Y    客户区坐标点击（调试用）
BetterGIAutoStart.exe --launcher-only       只启动启动器并等窗
BetterGIAutoStart.exe --config              打开图形化配置界面
BetterGIAutoStart.exe --diagnostics 包.zip  导出诊断包（环境/自检/配置/日志/截图）口（诊断用）
```

---

## 十一、已知限制

1. **协议弹窗只能在条款更新时出现**，无法主动触发；已验证过一次真实弹窗，
   并加了"进游戏后复核 HUD → 补一轮处理"的兜底。
2. 启动器大版本更新（如 1.19→1.20）可能改变 UI；模板会自动采集，但页面判断依赖
   "页签模板 + 点击"，若页签布局大改需要重新采模板。
3. 需要目标账户保持登录且不锁屏；否则启动器会要扫码登录，流程必然失败。
4. UAC 若设置为"提示"，exe 自提权会弹框；建议"无提示提权"或让任务以管理员身份运行
   （但后者可能触发上面第 10 条的问题）。
5. 大版本更新耗时可能超过 `UpdateTimeout`，建议保留 1 小时以上。
6. **WinRT OCR 需要提权运行**：受限令牌下 `StorageFile.GetFileFromPathAsync` 会报
   `拒绝访问（StorageApplicationPermissions）`。程序已改为除 `--dry-run`/`--match-file`
   外一律自动提权重启，因此正常使用不受影响。

---

## 十二、排查

> 如果你是 AI Agent 或帮别人排查，请直接看 [AGENTS.md](AGENTS.md)——里面有新机器检查清单、故障树和安全红线。


| 现象 | 先看 |
| --- | --- |
| 任务跑完但什么都没发生 | `logs\BetterGIAutoStart_*.log` 是否有"今天已经成功运行过" |
| 启动器起不来 | 是否有 `RUNASADMIN` 兼容标记；HYP 是否残留（有进程无窗口） |
| 识别不到按钮 | `shots\<时间>\` 里的截图和 `*_ocr.txt`；用 `--match-file` 离线验证模板 |
| 点错位置 | 是否窗口不在前台（看日志"窗口不在前台，跳过本次截图"） |
| 跑完没关机 | 看是否 BetterGI 自己关了；看门狗是否触发 |
| 电脑空转 | 任务是否根本没跑（未登录/任务被禁用）；兜底关机是否被取消 |

---

## ⚠️ 免责声明

- 本项目仅供**个人学习与自动化研究**使用，请勿用于商业用途。
- 使用本工具可能违反游戏用户协议，存在账号风险，**使用者需自行承担全部后果**。
- 本项目不修改游戏文件、不读写游戏内存、不注入进程，所有操作均为**模拟用户界面点击**。
- 项目中 `templates/` 下的少量图片仅用于界面元素识别（互操作性目的），版权归米哈游所有。
- 与米哈游 / HoYoverse / BetterGI 官方均无关联。

## 许可证

MIT License，详见 [LICENSE](LICENSE)。