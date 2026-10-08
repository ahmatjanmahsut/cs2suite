#!/bin/bash
# ============================================================
# CS2Suite - fallback grant script (runs ONLY on first init)
#
# Why this exists:
#   The official mysql image already creates MYSQL_USER / MYSQL_PASSWORD
#   and grants access from '%'. But on some router/ARM environments, or when
#   using a custom MYSQL_DATABASE name, the user may end up granted only for
#   'localhost' - which makes the remote game server fail to connect.
#   This script re-asserts, using the env vars from .env:
#     - the database exists with utf8mb4
#     - the user's password matches .env
#     - the grant covers '%' (any host)
#
# NOTE: .sql files cannot read environment variables, therefore this is a .sh
#       (the container executes it with bash). Messages are ASCII on purpose
#       to stay readable regardless of container locale.
# ============================================================
set -e

db="${MYSQL_DATABASE:-cs2suite}"
user="${MYSQL_USER:-cs2suite}"
pass="${MYSQL_PASSWORD:-}"

if [ -z "$pass" ]; then
  echo "[cs2suite-init] MYSQL_PASSWORD not set - skipping fallback grant"
  exit 0
fi

echo "[cs2suite-init] ensuring database and remote account: db=$db user=$user"

mysql --protocol=socket -uroot -p"${MYSQL_ROOT_PASSWORD}" <<-EOSQL
	CREATE DATABASE IF NOT EXISTS \`$db\` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
	CREATE USER IF NOT EXISTS '$user'@'%' IDENTIFIED BY '$pass';
	ALTER USER '$user'@'%' IDENTIFIED BY '$pass';
	GRANT ALL PRIVILEGES ON \`$db\`.* TO '$user'@'%';
	FLUSH PRIVILEGES;
EOSQL

echo "[cs2suite-init] done: $user@'%' granted on $db"
