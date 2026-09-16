# CursorTokenTray 云同步服务

FastAPI + SQLite。客户端把账号、配置和用量封进 PBKDF2 + AES-256-GCM 信封后上传，**服务器不解密，也看不到 Token**。

对外地址写死在客户端：`https://sync.harker.cn`

## 本地运行

`JWT_SECRET` 至少 32 个字符，且不能是 `dev-only-change-me`。只有显式设置 `ALLOW_DEV_JWT=1` 时才允许开发默认值（仅限本机/单测）。

```bash
cd server
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
export JWT_SECRET=请换成至少32位的随机串
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
| POST | `/v1/auth/register` | 开放注册，`{email,password}` |
| POST | `/v1/auth/login` | 登录 |
| POST | `/v1/auth/refresh` | `{refresh_token}` |
| POST | `/v1/auth/logout` | 作废 refresh |
| GET | `/v1/me` | 当前用户 |
| GET | `/v1/sync` | 取加密信封 + revision |
| PUT | `/v1/sync` | `{revision, envelope}`，revision 不对返回 409 |

密码至少 8 位，邮箱小写去重，无需验证。Access token 默认 15 分钟，refresh 30 天。
`PUT /v1/sync` 限制 JSON 约 1MB、ciphertext 长度，以及 KDF iterations 在 1000–600000。超限返回 413 或 400。
