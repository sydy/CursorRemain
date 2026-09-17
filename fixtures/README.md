# Native ports

Golden JSON fixtures shared by Python, Swift (`macos/`), and C# (`windows/`).

- `usage_summary_cases.json` — Dashboard usage-summary payloads and expected snapshots
- `sand_usage_cases.json` — Grok Bot weekly allowance (`get-sand-usage-status`) and eligibility rules
- `token_cases.json` — WorkosCursorSessionToken normalize / variants / account id
- `format_cases.json` — membership, USD, token count, status pill / plan caption
- `aggregated_usage_cases.json` — model token aggregation
- `usage_events_cases.json` — filtered usage events parse, kind labels, report aggregation, RMB allocation
- `account_sync_cases.json` — encrypted account-sync merge, per-field settings timestamps, usage remaining / history / events, path resolve, AES-GCM vector
- `password_login_cases.json` — email sanitize, default label, cookie → token, autofill script
- `update_release_cases.json` — GitHub 正式版 / Latest 发布解析、版本号与 SHA 比较、选择正式版或滚动预发布、下载地址与是否可自更新

Python tests in `tests/test_fixtures.py` lock these to the reference parser.
Swift and C# unit tests load the same files so both native ports stay aligned.
