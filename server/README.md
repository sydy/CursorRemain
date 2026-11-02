# 云同步服务

FastAPI + SQLite，供客户端同步账号、设置和用量。客户端地址固定为 `https://sync.harker.cn`。使用说明见仓库根目录 [README.md](../README.md)。

新注册默认把明细存在服务器上。信封模式由客户端用独立口令做 PBKDF2-SHA256（600,000 次）和 AES-256-GCM，服务器不解密。登录密码只用于账户认证，重置密码不会改变数据加密口令。

`JWT_SECRET` 至少 32 个字符，且不能是 `dev-only-change-me`。只有 `ALLOW_DEV_JWT=1` 才允许开发默认值。生产环境必须配置 SMTP，否则验证邮件和重置邮件发不出去。

```bash
cd server
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
export JWT_SECRET=请换成至少32位的随机串
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

认证路径以 `/v1/auth/` 开头，包括注册、验证邮箱、登录、重置密码、刷新、退出和修改密码。登录密码至少 8 位。验证码 15 分钟内最多尝试 5 次。Access token 15 分钟，refresh token 30 天。

同步路径：`GET/PUT /v1/sync` 是信封，`GET/PUT /v1/store` 是明文明细。`GET /v1/me`、`PUT /v1/me/storage` 和 `DELETE /v1/me` 管理账号与存储方式。`GET /health` 是健康检查。
