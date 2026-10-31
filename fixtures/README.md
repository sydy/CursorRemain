# Golden Fixtures

Golden JSON fixtures shared by Rust core (`core/`), Swift (`macos/`), and C# (`windows/`).

- `usage_summary_cases.json` — Dashboard usage-summary payloads and expected snapshots
- `sand_usage_cases.json` — Grok Bot weekly allowance (`get-sand-usage-status`) and eligibility rules
- `token_cases.json` — WorkosCursorSessionToken normalize / variants / account id
- `format_cases.json` — membership, USD, token count, status pill / plan caption
- `aggregated_usage_cases.json` — model token aggregation
- `usage_events_cases.json` — filtered usage events parse, kind labels, report aggregation, RMB allocation
- `account_sync_cases.json` — encrypted account-sync merge, per-field settings timestamps, usage remaining / history / events, path resolve, AES-GCM vector
- `password_login_cases.json` — email sanitize, default label, cookie → token, autofill script
- `update_release_cases.json` — GitHub 正式版 / Latest 发布解析、版本号与 SHA 比较、选择正式版或滚动预发布、下载地址与是否可自更新；Windows 另有不含运行时的 `CursorRemain-windows-light.zip`

Rust 核心库 `core/` 单源实现并维护所有解析与纯业务逻辑，直接运行这批测试（`cargo test`）。
Swift 与 C# 单元测试加载相同 JSON 文件，并通过 FFI 调用 Rust core，确保全平台严格对齐。
