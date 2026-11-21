# AGENTS.md —— 给 AI Agent 的运行与排障手册

> 这份文档是写给**接手这个项目的 AI Agent** 看的，不是给最终用户的。
> 目标：在**陌生的机器**上快速判断"为什么跑不起来"，并按优先级修复，而不是盲目改代码。

---

## 1. 这是什么

`BetterGI-AutoStart` 是 **BetterGI 的启动伴侣**：无人值守时，它负责把"原神"从启动器一路带到可操作的主界面，然后交给 BetterGI 跑一条龙，最后兜底关机。

```
启动器(launcher.exe/HYP)
  → 识别"更新游戏/开始游戏"
  → 点开始 → 等原神窗口
  → 处理协议弹窗 / 标题界面"点击进入"
  → 等待窗口稳定
  → Start-Process "BetterGI.exe" startOneDragon
  → 看门狗守着 BetterGI，退出后兜底关机
```

识别方案：**OpenCvSharp 模板匹配负责定位，Windows.Media.Ocr 只负责读按钮文字**。代码里没有任何写死的点击坐标。

---

## 2. 新机器上先跑这 4 条（按顺序）

所有命令在 `dist` 目录执行，exe 会**自动提权**（UAC 设为无提示时静默；有提示会弹框）。

```bat
cd /d E:\DSH\BetterGI-AutoStart\dist
BetterGIAutoStart.exe --self-test
```

**期望看到**：`管理员: True`、`屏幕: ... 主屏=True`、四个路径全 `True`、`OCR: zh-Hans-CN`、`OCR 行数 > 0`。

判读：

| 现象 | 结论 |
| --- | --- |
| `管理员: False` 且无提权日志 | 提权失败，后面 OCR/点击都会异常 |
| `OCR: (空)` | 缺中文 OCR 语言包 → 设置 → 时间和语言 → 语言 → 添加中文(简体) |
| `OCR 行数: 0` 但截到图了 | 见 §5.3（OCR 权限问题） |
| 路径有 False | 改 `BetterGIAutoStart.config.json` 里的路径 |
| `屏幕` 不是目标分辨率 | 虚显器/实显器切换问题，见 §6 |

```bat
BetterGIAutoStart.exe --match-file ..\templates\auto_btn_start.png --match-name auto_btn_start
```

**期望**：`score=1.000`（模板与自身匹配）。低于 0.99 说明 OpenCV 原生库没加载，见 §5.2。

```bat
BetterGIAutoStart.exe --launcher-only
```

只启动启动器并等窗口，验证"能不能拉起启动器"——这是历史上最容易出问题的一步。

```bat
BetterGIAutoStart.exe --stop-before-bettergi
```

完整跑到 BetterGI 之前停下（约 1~2 分钟），验证识别/点击链路。**不要**在用户不知情时跑不带参数的完整流程（会占用鼠标键盘、启动游戏、可能关机）。

---

## 3. 日志怎么读

文件：`dist\logs\BetterGIAutoStart_YYYYMMDD.log`（UTF-8）

按顺序出现这些行说明链路正常：

```
管理员: True                              ← 提权成功
启动器窗口就绪: 米哈游启动器 (1280x720)     ← 启动器有窗口
确认在原神页面（启动器页面 hash=hk4e_cn）   ← 老版启动器
  或  模板定位到原神页签: ... 点击切换       ← 新版启动器
模板定位主按钮（auto_btn_start 得分 1.00），OCR 读出的文字: “开始游戏”
原神窗口就绪: 原神 1920x1080
模板识别到"点击进入"按钮
原神窗口稳定 25 秒
BetterGI 已启动 (pid=...)
运行正常，已删除本次截图
```

失败时一定有 `错误:` 开头的一行 + 异常栈，后面跟 `未启动 BetterGI，执行兜底关机`。

---

## 4. 重要的状态文件

| 文件 | 作用 | 误判风险 |
| --- | --- | --- |
| `logs\ran_YYYYMMDD.flag` | 当天成功标记；存在则**直接跳过**（日志："今天已经成功运行过"） | ⚠️ 最高频"假故障"。用户说"程序秒退/闪退"时**先查这个** |
| `logs\fallback_shutdown_pending.flag` | 已安排兜底关机 | 下次启动会自动 `shutdown /a` |
| `shots\YYYYMMDD_HHmmss\` | 失败现场（截图 + `_result.txt`） | 成功会自动删除 |

排查"秒退"：`--force` 可绕过标记且**不写**标记；或直接删除该 flag。

---

## 5. 故障树（现象 → 首查 → 根因 → 修复）

### 5.1 计划任务拉起了 exe，但启动器没有窗口

**首查**：日志是否出现 `启动器窗口就绪`；`dist\logs` 里该时间的日志是否存在。

**根因 A（最常见）**：exe 被加了"兼容性 → 始终以管理员身份运行"标记。

```powershell
$k='HKCU:\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
(Get-Item $k).Property | Where-Object { $_ -match 'BetterGIAutoStart' }
```

有输出 → 删除该项（`Remove-ItemProperty`）。**这条标记会让任务用服务令牌强制提权，HYP 在这种令牌下初始化注册表管理器失败，弹 `Failed to initialize registry manager` 且不出主窗口。**

**根因 B**：残留的 HYP 进程（有进程无窗口）堵住单实例。
→ 程序会自动清理；手动验证：`Get-Process HYP`，有但无窗口就 `Stop-Process -Force`（需管理员）。

**根因 C**：启动器白屏（CEF 未渲染）。
→ 程序有"全白检测 + 最小化/还原"自愈；确认日志里有 `执行最小化/还原强制重绘`。

**根因 D**：计划任务 run level 设成了"最高权限"。
→ 必须是 **Limited（最低权限）** + 程序内部自提权。

### 5.2 模板匹配全部低分 / OpenCV 报错

**首查**：`--match-file` 自测；日志有 `DllNotFoundException` / `OpenCvSharpExtern`。

**根因**：用了 `PublishSingleFile=true`，原生库没有随包解压。
→ 必须文件夹部署：`-p:PublishSingleFile=false`，且 `runtimes\win-x64\native\OpenCvSharpExtern.dll` 存在。

### 5.3 OCR 全部返回 0 行

**首查**：日志 `OCR 失败(原始): ... 拒绝访问 ... StorageApplicationPermissions`。

**根因**：**WinRT OCR 在受限令牌下无法访问文件**（实测：非提权进程读截图被拒）。
→ 程序已改为"除 `--dry-run`/`--match-file` 外一律自动提权"。若仍失败，检查：
- 是否提权失败（UAC 弹窗被拒、账户不是管理员）
- 截图文件是否被占用（`shots` 目录不可写）
- 中文 OCR 语言包是否存在

### 5.4 找不到按钮 / 识别不到"开始游戏"

**首查**：`shots\...\launcher.png` 和 `launcher_ocr.txt`。

**根因 A**：分辨率/DPI 与模板不匹配 → 检查 `--self-test` 的 DPI；模板按 `name_宽x高.png` 优先，通用模板支持 0.4~2.0 缩放。
**根因 B**：启动器改版 → 重新采集模板（跑一次成功流程会自动采集）。
**根因 C**：启动器停在别的游戏页 → 看日志是否有 `模板定位到原神页签`。

### 5.5 点错位置 / 点到别的窗口

**首查**：日志 `窗口不在前台，跳过本次截图` 是否出现。

**根因**：窗口被覆盖时 `CopyFromScreen` 会截到别人的画面。
→ 程序已加前台硬校验（最小化/还原强抢，抢不到就跳过）。若仍点错，检查是否有窗口总在最前（如录屏/悬浮窗）。

### 5.6 更新卡住 / 一直等待

**首查**：日志 `更新等待中…… 已 Ns`。

**根因**：大版本更新超时（默认 3600 秒）或磁盘满。
→ 检查 `UpdateTimeout`、磁盘剩余（游戏目录所在盘至少留 40GB）。

### 5.7 跑完不关机

**首查**：`shutdown` 是否被取消（`fallback_shutdown_pending.flag` 是否存在）；看门狗日志。

**根因 A**：`FallbackShutdown=false`。
**根因 B**：任务根本没跑（未登录、任务被禁用）。
**根因 C**：BetterGI 自己关了机（正常）。

### 5.8 完全没有日志、任务结果码非 0

**首查**：任务计划程序 → 历史记录（Operational 日志需手动开启）。

**根因 A**：exe 路径/工作目录写错（任务动作）。
**根因 B**：单文件 exe 解压失败（`0x800080A0`）→ 必须文件夹部署。
**根因 C**：任务 run level 与 exe 权限标记冲突（§5.1 A）。

---

## 6. 环境差异（跨机器最容易踩的）

| 差异 | 影响 | 处理 |
| --- | --- | --- |
| 分辨率/DPI 不同 | 模板尺寸不同 | 用尺寸专用模板，或让程序自动采集；验证 `--match-file` |
| 虚显器（欺骗器）1080p | 启动器以 1280×720 渲染 | 已有 1280 专用模板（页签/开始按钮） |
| 多 Windows 账户 | 启动器登录态只在某个账户 | 任务必须跑在那个账户；`query session` 确认 |
| RDP 会话 | 窗口/截图/输入按会话隔离 | 必须跑在**控制台会话**（`query session` 看 console 的 ID） |
| 杀毒软件（如火绒） | 首次启动扫描导致延迟/拦截 | 把 `dist` 加入信任区 |
| UAC 提示模式 | 自提权会弹框，无人值守就卡住 | 设为"无提示提权"，或改用管理员任务 |
| 开机 RTC | 开机后系统忙（更新/杀软） | `MinUptimeMinutes` 设 20~30 |

---

## 7. 安全红线（任何 Agent 都必须遵守）

1. **绝不**点击：取消 / 不同意 / 拒绝 / 跳过 / 退出 / 注销 / 删除 / 充值 / 关闭。
2. **绝不**修改、删除、移动游戏文件（只读 `config.ini` 的 `game_version`）。
3. **绝不**在用户不知情时执行不带参数的完整流程（会控制鼠标键盘、启动游戏、可能关机）。
4. **绝不**绕过启动器直接开游戏（`AllowDirectGameFallback` 默认关闭，因为会失去更新判断）。
5. 兜底关机只在"BetterGI 未启动"时安排；BetterGI 一旦成功启动，**不得**再主动关机。
6. 排查请优先用只读命令（`Get-Process`、读日志、`--match-file`），再考虑改动。

---

## 8. 命令速查

```bat
BetterGIAutoStart.exe                     完整流程
BetterGIAutoStart.exe --dry-run           演练：只识别不点击、不启动、不关机
BetterGIAutoStart.exe --self-test         自检
BetterGIAutoStart.exe --stop-before-bettergi  跑到 BetterGI 前停
BetterGIAutoStart.exe --force             忽略当天成功标记，且不写标记
BetterGIAutoStart.exe --skip-uptime       跳过开机等待
BetterGIAutoStart.exe --launcher-only     只启动启动器并等窗口
BetterGIAutoStart.exe --test-window 标题   截图 + OCR 指定窗口
BetterGIAutoStart.exe --match-file 图片 --match-name 模板   离线模板匹配
BetterGIAutoStart.exe --click-template 模板                模板定位并点击
BetterGIAutoStart.exe --click-client X,Y                   客户区坐标点击
```

注意：新版启动器不再写页面 URL，`--dry-run` 会在"切换原神页签"处结束（演练模式点不了），**这是预期行为**，不是 bug。

---

## 9. 代码导航（`Program.cs` 单文件）

| 区域 | 内容 |
| --- | --- |
| `AppConfig` | 全部配置项与默认值 |
| `Main` | 参数解析、提权、模式分发、整体 try/finally（兜底关机在此） |
| `RunFlow` | 阶段一~五主流程 |
| `StartLauncherAndWait` / `KillLauncherFamily` | 启动器启动与残留清理 |
| `SwitchToGenshinPage` / `ReadLauncherPageHash` | 页面判断（hash / 页签模板） |
| `WaitLauncherState` | 主按钮状态机（模板定位 + OCR 读字 + 自更新处理） |
| `WaitUpdateComplete` | 更新等待 |
| `HandleAgreement` / `ClickGameEntry` / `WaitGameReady` / `EnsureInGameOrRetry` | 游戏侧处理 |
| `TemplateEngine` | OpenCV 模板匹配（按尺寸模板优先 + 缩放搜索） |
| `OcrShot` / `OcrRegion` / `OcrButtonLabel` | OCR（含路径兜底） |
| `CaptureForOcr` / `BringToFront` / `ForceRepaint` | 截图、前台校验、白屏自愈 |
| `ScheduleFallbackShutdown` / `ClearStaleShutdownMarker` / `TryFinalizeRunShots` | 关机与收尾 |

改完代码必须：

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o dist
BetterGIAutoStart.exe --self-test
BetterGIAutoStart.exe --stop-before-bettergi   （在用户同意下）
```

---

## 10. 一句话总结

**先看日志、先查状态文件、先跑 `--self-test`；90% 的"跑不起来"是环境问题（权限标记/OCR 提权/分辨率/账户/开机太早），不是代码逻辑问题。**
### 12.1 配置界面（`--config`）与自检 JSON

- `BetterGIAutoStart.exe --config` 打开图形配置界面。它在 `LoadConfig()` 之后、`InitPaths()`/提权/`InitOcr()` 之前 `return ConfigUi.Run()`，所以**不建 logs/shots、不写日志、不提权**。改这部分时别把这段挪位置。
- 界面代码在 `Config/`：`AppConfig.cs`（配置模型，50 个属性）、`ConfigStore.cs`（读写 + JSON 保真 + 原子保存 + `Diff`）、`ConfigForm.cs`（壳 + 路径页 + 自动化页）、`ConfigPages.cs`（其余四页 + `FlatTextArea`）、`ConfigDialogs.cs`（diff / 自检结果对话框）、`ConfigSelfTest.cs`（`--self-test` 子进程 + 轮询结果）、`Theme.cs` / `Controls.cs`（WinUI 风格自绘控件）。
- 自检结果交接：界面用 `Process.Start(..., Verb="runas")` 跑 `--self-test --self-test-json "<dist>\ConfigUi.selftest.json"`；主程序在 `Main` 的 selfTest 分支末尾调 `WriteSelfTestJson(path)` 写 `ok/time/admin/launcher/game/bettergi/ini/ocr/gameVersion/configPath/logPath/text`。**提权后父进程拿不到子进程 stdout，只能走文件。**
- 排查：界面崩溃写 `dist\ConfigUi.error.txt`；自检没反应先看 `dist\logs\BetterGIAutoStart_YYYYMMDD.log` 里的 `写自检 JSON 开始/完成` 两行。
- 自绘控件（`FlatButton`/`FlatTextBox`/`ToggleSwitch`/`NavRail`）都按 `Ui.Px()`（`DeviceDpi/96`）手动缩放，**不要**再启用 `AutoScaleMode.Dpi`——150% DPI 下 WinForms 自动缩放会失效。
- 自动化测试注意：`NavRail` 的项没有 UIA 矩形（要按客户坐标点：`x=114*S`，第 i 项 `y=(30+38*i)*S`）；`FlatButton` 必须继承 `Button` 才有 InvokePattern；模态对话框打开后不要再对父窗体做 UIA 查找（会超时），改用 `FindWindow` 或截图。

### 12.2 备份与恢复页、试跑范围

- 备份文件命名 `BetterGIAutoStart.config.json.bak.yyyyMMdd_HHmmss`；`ConfigBackup.ListBackups()` 额外用 `StartsWith(文件名 + ".bak.")` 过滤，因为 Windows 通配符会连带匹配没有时间戳后缀的 `.bak`。
- 恢复流程：确认 → 当前配置另存 `.before-restore.yyyyMMdd_HHmmss` → 覆盖 → `ConfigStore.Load` 校验，失败则回滚 → `_rail.Selected = 0` 同步导航 → `ShowPage(0)`。
- `ShowPage` 首尾会置 `_suppressDirty`，并把 `_dirty` 复位成进入前的值：建页面时代码给控件赋值会触发 `TextChanged`，不抑制就会误标"有未保存的修改"（早期恢复后状态栏显示成"有未保存的修改"就是这个原因）。所有用户改动统一走 `MarkDirty()`。
- `FlatButton` 自绘时必须 `FlatStyle = Flat + FlatAppearance.BorderSize = 0 + UseVisualStyleBackColor = false`，否则基础 Button 会把同一个字符串再画一遍（Subtle 变体上看得到重影）；Subtle 的底色也不能用纯透明，ClearType 画在未清底的透明层上会留残影，改用 `Ui.BackdropOf(this, Theme.Card)`。
- 试跑范围：`ConfigPilotDialog` 返回 `Mode`（1 = `--launcher-only`，2 = `--stop-before-bettergi`，0 = 取消）。
