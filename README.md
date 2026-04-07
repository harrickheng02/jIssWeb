# JIssWeb

## 本地数据库（Docker）

| 服务 | 宿主机端口 | 账号 | 密码 |
|------|------------|------|------|
| MongoDB | 37017 | harrickheng | qq!219673605 |
| Redis | 6380 | （默认用户，仅密码） | qq!219673605 |

容器内 Mongo 27017、Redis 6379；Compose 内用服务名 `mongo`、`redis`。

Redis 使用 `requirepass`，客户端/RedisInsight 填 Host `127.0.0.1`、Port `6380`、Password；用户名留空或 `default`。

修改 `docker/redis.conf` 或从 ACL 改为 `requirepass` 后，若仍连不上，先删 Redis 卷再启：`docker compose down`，`docker volume rm jissweb_redis_data`（或 `docker compose down -v`），再 `docker compose up -d`。
