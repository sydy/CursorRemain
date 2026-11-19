# Cursor 余量 (CursorRemain)

轻量级 Cursor 额度监控工具，常驻 Windows 系统托盘 / macOS 菜单栏，实时查看套餐剩余百分比与用量详情。

## 特性

- **状态监控**：托盘/菜单栏显示用量进度环、剩余百分比或色点，悬浮卡片展示本周期消耗与耗尽预测。
- **多账号管理**：支持多账号独立监控与告警，一键将导入的会话写回 Cursor 客户端完成切号。
- **便捷导入**：一键从本地 Cursor 读取，亦支持浏览器 Cookie、直接粘贴 Token 或账号密码登录。
- **用量分析**：内置明细报表、模型消耗排行、多账号横向对比，支持导出 CSV。
- **提醒与自启**：多档额度阈值通知、耗尽风险预警、开机自启与静默自动更新。
- **多端同步**：可选将账号与用量同步到云端（支持端到端加密信封）。

## 下载安装

前往 [Releases](https://github.com/sydy/CursorRemain/releases/latest) 下载最新版本：

- **Windows** (10/11)：
  - **安装版**：`CursorRemain-windows-setup.exe`（推荐，内置运行时，支持原地自动更新）
  - **便携版**：`CursorRemain-windows.zip`（解压即用）
- **macOS** (13+)：
  - 下载 `CursorRemain-macos.zip`，解压后将 `CursorRemain.app` 拖入「应用程序」
  - 若打开时提示“已损坏”，在终端执行 `xattr -cr /Applications/CursorRemain.app` 即可

## 账号添加

在「设置 → 账号」中添加：
1. **从 Cursor 导入**（推荐）：自动读取本机已登录的桌面会话，后续可直接切号。
2. **浏览器导入**：支持 Safari / Firefox。
3. **手动添加**：支持直接粘贴 Token (`WorkosCursorSessionToken`) 或账号密码。

## 豆包额度

除 Cursor 账号外，也可以接入豆包 App（专业版）的会员额度：

1. **登录接入**：设置页「豆包账号 → 登录豆包账号」，在按账号 id 隔离的内嵌登录窗口里完成登录，程序自动抓取登录态；也可以「粘贴 Cookie」作为兜底（需含 `sessionid` 或 `uid_tt`）。
2. **额度口径**：主百分比取 7 天 / 每月窗口中已用百分比的较大值，两者都没有时依次退回 5 小时、创作额度包、活动赠送额度；`usage_exhausted=true` 时剩余记 0。悬浮卡片逐行显示 5 小时 / 近 7 天 / 每月窗口，以及创作额度包和活动赠送额度（后者标注条目数与剩余，如「活动赠送额度（24 项）· 已用 10% · 剩 90%」），5 小时窗口显示重置倒计时；套餐名优先取 `member_info`，免费/个人订阅回退 `current_subscription.display.product_name`（再退 `short_name`）。
3. **刷新节流**：豆包额度最短 5 分钟拉取一次，并与全局刷新间隔取较大值；「立即刷新」不受此限制。
4. **存储与同步**：Cookie 用与 Cursor Token 相同的本机加密方式保存，**不参与云同步**，也不会写进日志；豆包账号只在本机显示，「当前账号」始终指 Cursor 账号。
5. **失效判定**：接口 HTTP 恒为 200，登录失效以响应体 `code=710012001` 判定，命中时提示「豆包登录已失效」并触发一次通知；其它业务码按普通失败处理。
6. **告警**：额度阈值告警对豆包同样生效；本期关闭豆包的「耗尽风险」预警（豆包没有周期起止，无法估算）。

## 开发与构建

核心逻辑（解析、报表、同步、更新判断）由 Rust (`core/`) 实现，外壳采用纯原生开发。

### Windows (C# / .NET 8)

```powershell
dotnet run --project windows/CursorRemain/CursorRemain.csproj -c Release
dotnet test windows/CursorTokenCore.Tests/CursorTokenCore.Tests.csproj
```

### macOS (Swift)

```bash
cargo build --release --manifest-path core/Cargo.toml
swift run --package-path macos CursorRemain
```

### Rust Core

```bash
cargo test --manifest-path core/Cargo.toml
```

配置文件路径：
- Windows: `%APPDATA%\CursorRemain\config.json`
- macOS: `~/Library/Application Support/CursorRemain/config.json`
