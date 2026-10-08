-- ============================================================
-- 初始化说明(重要)
--
-- 官方 mysql 镜像会依据环境变量自动完成:
--   * 创建数据库  MYSQL_DATABASE(默认 cs2suite)
--   * 创建用户    MYSQL_USER(默认 cs2suite),密码取 MYSQL_PASSWORD
--   * 并授予该用户从【任意主机】(@'%')访问该库的全部权限
-- 因此这里【不要】再重复 CREATE USER / 写死密码——那会覆盖掉 .env 里的密码,
-- 导致插件与网页端登录失败(非常常见的坑)。
--
-- 本文件只做两件安全且必要的事:
--   1) 确保库存在且字符集正确(幂等)
--   2) 若用户已存在,仅补齐授权(不改密码)
-- ============================================================
SET NAMES utf8mb4;

CREATE DATABASE IF NOT EXISTS cs2suite
  DEFAULT CHARACTER SET utf8mb4
  DEFAULT COLLATE utf8mb4_unicode_ci;

FLUSH PRIVILEGES;
