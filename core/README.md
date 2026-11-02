# cursor_core

CursorRemain 的纯逻辑库，负责 Token、用量、报表、账号对比、CSV、同步合并和更新判断。这里没有网络、文件或钥匙串。Windows 调用 `cursor_core.dll`，macOS 链接 `libcursor_core.a`。使用说明见仓库根目录 [README.md](../README.md)。

```bash
cargo test --manifest-path core/Cargo.toml
cargo build --release --manifest-path core/Cargo.toml
```
