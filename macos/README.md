# macOS

Swift 菜单栏程序。`CursorTokenCore` 是可测试的领域逻辑，`CursorRemain` 是菜单栏应用。使用说明见仓库根目录 [README.md](../README.md)。

先构建 Rust 静态库，并且只链接 `libcursor_core.a`。若链接成动态库，可执行文件会带上构建机的绝对路径，换一台机器就无法启动。

```bash
cargo build --release --manifest-path ../core/Cargo.toml
swift test
swift run CursorRemain
./scripts/package_app.sh
```

配置在 `~/Library/Application Support/CursorRemain/config.json`。版本号来自仓库根目录 `VERSION`，打包脚本会写入 Info.plist。`swift run` 不会覆盖已安装的程序。
