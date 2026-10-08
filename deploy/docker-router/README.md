# 在路由器上用 Docker 部署 CS2Suite 的 MySQL

把数据库放在路由器上,游戏服务器与网页端**远程连接**它。
适合:家里已有跑 Docker 的路由器/NAS,想省一台常开的机器。

---

## 一、先确认你的路由器能不能跑(重要)

**不是所有路由器都能跑 Docker。** 请先核对四项,任一项不满足就别硬上(见文末替代方案):

| 条件 | 要求 | 怎么查 |
|---|---|---|
| CPU 架构 | **x86_64** 或 **arm64/aarch64** | 路由后台看型号规格;ARMv7(32 位)也勉强能跑 mysql 但更容易 OOM |
| 内存 | **≥ 512MB 可用**(建议 1GB+) | MySQL 8 空载约 200-300MB,给 64M buffer pool 时更省 |
| 存储 | **≥ 2GB 可用** + 支持持久化挂载 | 皮肤/贴纸数据不大,但 InnoDB 日志会占空间 |
| 系统 | 支持 **Docker**(OpenWrt/iStoreOS/群晖 DSM/爱快等) | SSH 执行 `docker version` 有输出即可 |

常见可跑 Docker 的固件/设备:iStoreOS(OpenWrt)、群晖/威联通 NAS、软路由(x86 小主机)、
部分高配华硕/网件(刷梅林 + Entware + Docker 插件)、树莓派类设备。

> ⚠️ 若你的路由器是普通家用型号(如大多数 128-256MB 内存的 Wi-Fi 路由),**跑不动 MySQL 8**。
> 请直接看最后一节「替代方案」。

---

## 二、部署步骤

### 1. 把文件传到路由器

把 `deploy/docker-router/` 整个目录上传到路由器,例如 `/opt/cs2suite-mysql/`:

```bash
# 在路由器上(SSH)
mkdir -p /opt/cs2suite-mysql
# 用 scp / WinSCP / 路由器文件管理 把 docker-compose.yml、.env.example、initdb/ 传进去
cd /opt/cs2suite-mysql
ls    # 应看到 docker-compose.yml  .env.example  initdb/
```

### 2. 生成配置并改密码(必做)

```bash
cp .env.example .env
vi .env      # 改 MYSQL_ROOT_PASSWORD / MYSQL_PASSWORD,确认 TZ 和内存参数
```

**内存参数怎么填**(看路由器总内存):

| 路由器内存 | `MYSQL_BUFFER_POOL` | `MYSQL_MEM_LIMIT` |
|---|---|---|
| 512MB | `64M` | `384m` |
| 1GB | `128M` | `640m` |
| 2GB+ | `256M` | `1g` |

### 3. 启动

```bash
docker compose up -d
docker compose logs -f mysql
```

看到 `ready for connections` 即成功(首次启动会建库建表,约 30-60 秒)。
按 `Ctrl+C` 退出日志查看(容器继续在后台跑)。

### 4. 验证

```bash
# 在路由器本机验证
docker exec -it cs2suite-mysql mysql -ucs2suite -p cs2suite -e 'SHOW TABLES;'

# 在游戏服务器上验证远程连接(把 IP 换成路由器 LAN IP)
mysql -h 192.168.1.1 -P 3306 -ucs2suite -p cs2suite -e 'SELECT COUNT(*) FROM cs2suite_accounts;'
```

应能看到 18 张 `cs2suite_*` 表。

---

## 三、让游戏服务器与网页端连上

改两处配置,**填路由器 IP**(不是 127.0.0.1):

**A. 插件** `addons/counterstrikesharp/configs/plugins/CS2Suite/CS2Suite.json`:
```json
"Database": {
  "Host": "192.168.1.1",
  "Port": 3306,
  "User": "cs2suite",
  "Password": "与 .env 里 MYSQL_PASSWORD 一致",
  "Database": "cs2suite"
}
```

**B. 网页端** `web/config.json`:
```json
"database": {
  "host": "192.168.1.1",
  "port": 3306,
  "user": "cs2suite",
  "password": "与 .env 里 MYSQL_PASSWORD 一致",
  "database": "cs2suite"
}
```

改完重启插件与网页端。插件日志出现 `[CS2Suite] MySQL ready` 即为连通。

---

## 四、网络与安全(必读)

### 只在局域网使用时
- 正常放行即可:游戏服与网页端都在同一 LAN,直连路由器 IP:3306
- 建议在路由器防火墙里**限制 3306 只允许游戏服务器的内网 IP**,不要对全部内网开放

### 游戏服务器在外网(公网)时
**绝对不要**把 3306 直接暴露到公网 —— MySQL 在公网上每天会被扫描爆破。请任选其一:

1. **推荐:WireGuard / OpenVPN 组网**,游戏服通过 VPN 内网 IP 连数据库,3306 不对公网开放
2. **SSH 隧道**:游戏服上 `ssh -L 3306:127.0.0.1:3306 user@路由器` 常驻,再连 `127.0.0.1:3306`
3. 若必须开公网端口:改**非标端口** + 防火墙**仅白名单游戏服 IP** + 强密码

### 密码与账号
- `MYSQL_PASSWORD` 用 16 位以上随机串;库账号只有 `cs2suite` 库权限(非 root)
- root 仅用于容器内管理,不要在插件/网页里使用 root

---

## 五、日常运维

```bash
docker compose ps                 # 查看状态
docker compose logs --tail 100 mysql   # 看日志
docker compose restart mysql      # 重启
docker compose down               # 停止(数据保留在卷里)
docker compose pull && docker compose up -d   # 升级镜像
```

### 备份与恢复(重要)

```bash
# 备份到路由器当前目录
docker exec cs2suite-mysql mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" \
  --single-transaction --databases cs2suite > cs2suite-backup-$(date +%F).sql

# 恢复
docker exec -i cs2suite-mysql mysql -uroot -p"$MYSQL_ROOT_PASSWORD" < cs2suite-backup-2026-10-08.sql
```

建议把备份目录同步到 NAS/网盘,或加个 cron 定时备份。

### 数据卷位置
数据在命名卷 `cs2suite-mysql-data` 里。查看实际路径:
```bash
docker volume inspect cs2suite-mysql-data
```
路由器扩容/换机时,备份这个卷(或直接用上面的 mysqldump)即可迁移。

---

## 六、常见问题

| 现象 | 原因与解决 |
|---|---|
| 容器反复重启 | 内存不足。调小 `MYSQL_BUFFER_POOL`,调低 `MYSQL_MEM_LIMIT`,或看 `docker compose logs mysql` 的具体报错 |
| 插件报 `MySQL init failed` | ①Host 写成了 127.0.0.1(必须写路由器 IP)②密码不一致 ③路由器防火墙拦了 3306 |
| 网页能连、插件不能 | 插件与网页连的是不是同一个 IP/账号;插件容器与游戏服是否同网段 |
| `Access denied for user 'cs2suite'@'...'` | 该来源 IP 没被授权。默认已授权 `%`(任意主机);若你手工限制过,需要补授权 |
| 时间显示差 8 小时 | `.env` 的 `TZ` / `MYSQL_TZ` 与实际时区不一致;改完 `docker compose up -d` 重建 |
| 端口 3306 已被占用 | 路由器上可能已有其他 MySQL。把 `.env` 的 `MYSQL_PORT` 改成 3307,并同步改插件/网页配置 |
| 表不见了 | 数据卷被删或换了卷名。用备份恢复 |

> 说明:插件**启动时会自动建表**(幂等),所以即便 `initdb` 没跑成功,插件首次连接也会补齐表结构。
> `initdb/` 主要用于:①建库 ②创建可远程连接的账号 ③省去插件首次建表的等待。

---

## 七、替代方案(路由器跑不动时)

| 方案 | 说明 |
|---|---|
| **NAS 上跑**(群晖/威联通) | 与本方案完全相同的 compose 文件,套件里直接装 Docker/Container Manager 即可 |
| **游戏服务器本机装 MySQL** | 最简单:插件用 `127.0.0.1`。Windows 服务器用 MySQL 8 安装包,LINUX 用 `apt install mysql-server` |
| **云数据库 / VPS** | 游戏服在外网时更合适,注意加白名单与强密码 |
| **免费/低配 x86 小主机** | 几百元工控机装 Debian + Docker,性能和稳定性都远好于路由器 |

无论哪种方式,**插件与网页只需改 `Host`/`Port`/`Password` 三个字段**,其余不用动。
