# 云同步服务

FastAPI + SQLite，供客户端同步账号、设置和用量。客户端地址固定为 `https://sync.harker.cn`。使用说明见仓库根目录 [README.md](../README.md)。

新注册默认把明细存在服务器上，落库前按字段做 AES-256-GCM 加密（`enc1:` 前缀），密钥优先取 `DATA_ENCRYPTION_KEY`，未配置时从 `JWT_SECRET` 经 HKDF 派生。信封模式由客户端用独立口令做 PBKDF2-SHA256（600,000 次）和 AES-256-GCM，服务器不解密。登录密码只用于账户认证，重置密码不会改变数据加密口令。

`JWT_SECRET` 至少 32 个字符，且不能是 `dev-only-change-me`。只有 `ALLOW_DEV_JWT=1` 才允许开发默认值。生产环境必须配置 SMTP，否则验证邮件和重置邮件发不出去。

`DATA_ENCRYPTION_KEY` 可选，取值是 Base64 编码的 32 字节随机串（`openssl rand -base64 32`）。单独配置后轮换 `JWT_SECRET` 不会让已加密的存量数据失效；未配置则沿用从 `JWT_SECRET` 派生的密钥，升级后开箱即用。

```bash
cd server
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.lock
export JWT_SECRET=请换成至少32位的随机串
# 可选：export DATA_ENCRYPTION_KEY=$(openssl rand -base64 32)
export SMTP_HOST=smtp.example.com
export SMTP_PORT=587
export SMTP_USER=sync@example.com
export SMTP_PASSWORD=邮件服务授权码
export SMTP_FROM='CursorRemain <sync@example.com>'
export DATABASE_PATH=./data/sync.db
PYTHONPATH=. uvicorn app.main:app --host 127.0.0.1 --port 8000
PYTHONPATH=. python -m unittest tests.test_api -v
```

465 端口使用 TLS，其他端口默认 STARTTLS（`SMTP_STARTTLS=0` 可关闭）。需要局域网访问时再改成 `--host 0.0.0.0`，此时不要开 `TRUST_PROXY`。

Docker：在 `server/` 设置 `JWT_SECRET` 后执行 `docker compose up -d --build`。API 默认只监听 `127.0.0.1:8000`。前面的 HTTPS 反代只有在会覆盖 `X-Forwarded-For` 时才设 `TRUST_PROXY=1`。

`PUT /v1/sync` 的信封约限制 1MB，`PUT /v1/store` 约限制 8MB。revision 不一致返回 409。两种存储互不覆盖。SQLite 距上次备份超过 24 小时会复制到 `data/backups/`，最多保留 7 份。

认证路径以 `/v1/auth/` 开头，包括注册、验证邮箱、登录、重置密码、刷新、退出和修改密码。登录密码至少 8 位。验证码 15 分钟内最多尝试 5 次。Access token 与 refresh token 默认 10 年长期有效，不会因到期掉线。

同步路径：`GET/PUT /v1/sync` 是信封，`GET/PUT /v1/store` 是逐字段落库加密的明细（客户端拿到的仍是明文 JSON）。`GET /v1/me`、`PUT /v1/me/storage` 和 `DELETE /v1/me` 管理账号与存储方式。`GET /health` 是健康检查。

存量数据迁移：历史明文行不做一次性改写，读取时原样返回，任一端下一次 `PUT /v1/store` 后该用户数据会整体转成密文。SQLite 已开 `PRAGMA secure_delete=ON`，删除/覆盖的页会清零；若要立刻清掉升级前留在 WAL 和空闲页里的明文残留，在停写窗口执行：

```bash
sqlite3 data/sync.db "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;"
```

忘记或更换 `DATA_ENCRYPTION_KEY`/`JWT_SECRET` 会让既有密文无法解密，服务端返回 500「云端同步数据损坏」。换密钥前请先备份 `data/` 并评估是否需要客户端重新上传。
