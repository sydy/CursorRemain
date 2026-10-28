# cursor_core

CursorRemain 的纯解析核心。第一阶段只覆盖 Token、用量摘要、Grok Bot 周额度、模型聚合，以及套餐和金额格式化。

这里没有网络、文件、钥匙串或环境变量。Windows 通过 `cursor_core.dll` 调用，macOS 通过 `libcursor_core.a` 调用。两端覆盖 Token、用量摘要、Grok Bot、模型聚合、套餐金额格式化，以及用量明细的解析、分类、合并、费用格式化、报表聚合、图表分桶、人民币分摊和账号对比。账号有效期、状态文案、登录解析、更新说明解析和账号同步合并也已接入。CSV 导出、本地 JSONL、加解密、团队 ID 和用户 ID 还留在各端。

先构建原生库，再跑 Windows 测试：

```powershell
cargo build --release --manifest-path core/Cargo.toml
dotnet test windows/CursorTokenCore.Tests/CursorTokenCore.Tests.csproj -c Release
```

```powershell
cargo test --manifest-path core/Cargo.toml
```
