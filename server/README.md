# CursorRemain 云同步服务

FastAPI + SQLite。客户端用独立数据加密口令经 PBKDF2-SHA256（600,000 次迭代）派生密钥，把账号、配置和用量封进 AES-256-GCM 信封后上传。登录密码只用于账户认证。**服务器不解密，也看不到 Token 或数据加密口令**。

对外地址写死在客户端：`https://sync.harker.cn`

## 本地运行

`JWT_SECRET` 至少 32 个字符，且不能是 `dev-only-change-me`。只有显式设置 `ALLOW_DEV_JWT=1` 时才允许开发默认值（仅限本机/单测）。生产环境必须配置 SMTP，否则注册验证邮件和密码重置邮件无法发送。用 `SMTP_HOST`、`SMTP_PORT`、`SMTP_USER`、`SMTP_PASSWORD`、`SMTP_FROM` 配置邮件；465 端口自动使用 TLS，其他端口默认使用 STARTTLS（`SMTP_STARTTLS=0` 可关闭）。

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
```

需要让局域网访问时再改成 `--host 0.0.0.0`，此时不要开 `TRUST_PROXY`。

```bash
PYTHONPATH=. python -m unittest tests.test_api -v
```

## Docker

```bash
cd server
export JWT_SECRET=请换成至少32位的随机串
docker compose up -d --build
```

Compose 默认把 API 绑到本机 `127.0.0.1:8000`，且 `TRUST_PROXY=0`。不要在公网直暴露端口时信任客户端的 `X-Forwarded-For`。

前面用 Caddy / Nginx 做 HTTPS，反代到 `127.0.0.1:8000`，并把 `sync.harker.cn` 指过来。**只有**反代会改写 `X-Forwarded-For`（覆盖客户端原值）时才开信任：

```bash
export TRUST_PROXY=1
docker compose up -d
```

## 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health` | 健康检查 |
| POST | `/v1/auth/register` | 创建待验证账号并发验证邮件，`{email,password}` |
| POST | `/v1/auth/verify-email` | 验证邮箱，`{email,code}` |
| POST | `/v1/auth/verification-email` | 重发验证邮件，`{email,password}` |
| POST | `/v1/auth/login` | 登录 |
| POST | `/v1/auth/password-reset/request` | 请求重置邮件，`{email}`；不泄露邮箱是否注册 |
| POST | `/v1/auth/password-reset/confirm` | 校验验证码并更新登录密码，`{email,code,new_password}` |
| POST | `/v1/auth/refresh` | `{refresh_token}` |
| POST | `/v1/auth/logout` | 作废 refresh |
| POST | `/v1/auth/password` | 修改登录密码：`{old_password,new_password,revision,envelope?}`。有云端密文时客户端需带上用当前数据加密口令重封的信封；成功后作废全部 refresh |
| GET | `/v1/me` | 当前用户，含 `sync_storage`（`server` 或 `envelope`） |
| PUT | `/v1/me/storage` | `{mode}` 切换同步方式。新注册默认 `server`；旧库升级默认 `envelope` |
| DELETE | `/v1/me` | `{password}` 注销账号并删除云端密文和明细 |
| GET | `/v1/sync` | 取加密信封 + revision |
| PUT | `/v1/sync` | `{revision, envelope}`，revision 不对返回 409 |
| GET | `/v1/store` | 取明文明细快照 + revision |
| PUT | `/v1/store` | `{revision, snapshot}`，按账号、删除记录、设置和用量拆成行；revision 不对返回 409 |

登录密码至少 8 位，邮箱小写去重。新注册账号必须先验证邮箱；数据库升级时旧账号视为已验证。验证码 15 分钟过期，最多尝试 5 次；密码重置会撤销所有 refresh token。**密码重置只恢复账户登录，不会重置数据加密口令**。Access token 默认 15 分钟，refresh 30 天；过期或已作废的 refresh 会在登录 / 刷新时清掉。
`PUT /v1/sync` 限制 JSON 约 1MB、ciphertext 长度，以及 KDF iterations 在 1000–600000。信封可带 `compression=gzip`。`PUT /v1/store` 默认限制约 8MB。超限返回 413 或 400。两种存储互不覆盖：切到明细模式后，旧信封还在，但客户端不再读取它。

SQLite 启动时若距上次备份超过 24 小时，会复制到 `data/backups/sync-*.db`，最多保留 7 份。
