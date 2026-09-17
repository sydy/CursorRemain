# macOS 菜单栏（Swift）

SwiftPM 工程：`CursorTokenCore` 可测试的领域逻辑，`CursorRemain` 为 LSUIElement 菜单栏应用。

```bash
swift test
swift run CursorRemain
./scripts/package_app.sh
```

读取 `~/Library/Application Support/CursorRemain/config.json`；若只有旧版 `CursorTokenTray` 目录，启动时会迁过去。Token 用钥匙串中的 AES-GCM 包装密钥加密后写入；该条目使用不绑定具体构建的 ACL，避免每次自动更新都再要一次登录密码。从 Safari 导入 Cookie 需要完全磁盘访问权限（设置窗会检测并提供跳转）。从 Cursor 应用导入的会话可在设置里点「登录到 Cursor」写回客户端切号；浏览器 Cookie 不能写回。打包后的 `.app` 默认对照 GitHub Releases 的 `latest` 自动更新；`swift run` 不会覆盖本机文件。

从 GitHub Releases 下载的 zip 带隔离属性。若提示「已损坏」，先点取消，到「系统设置 → 隐私与安全性」点「仍要打开」并输入密码（Sequoia 取消了右键打开放行）。没有该按钮时再运行 `首次打开.command`，或：

```bash
xattr -cr CursorRemain.app
open CursorRemain.app
```
