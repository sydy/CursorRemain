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
