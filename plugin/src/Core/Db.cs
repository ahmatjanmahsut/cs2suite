using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2Suite.Core;

/// <summary>
/// 轻量 MySQL 访问层(连接池 + 参数化查询)。
/// 所有模块共用;启动时按需幂等建表(与 db/schema.sql 保持一致)。
/// </summary>
public sealed class Db : IDisposable
{
    private readonly DatabaseConfig _cfg;
    private readonly ILogger _log;
    public string ServerIdRef = "cs2suite";

    private string ConnString =>
        $"Server={_cfg.Host};Port={_cfg.Port};Database={_cfg.Database};" +
        $"Uid={_cfg.User};Password={_cfg.Password};SslMode=Preferred;Pooling=true;" +
        "MaximumPoolSize=24;ConnectionTimeout=5;Charset=utf8mb4;AllowPublicKeyRetrieval=True;";

    /// <summary>数据库(含表结构)是否可用。false 时各模块降级为纯内存玩法。</summary>
    public volatile bool Ready;

    public Db(DatabaseConfig cfg, ILogger log) { _cfg = cfg; _log = log; }

    private async Task<MySqlConnection> OpenAsync()
    {
        var c = new MySqlConnection(ConnString);
        await c.OpenAsync();
        return c;
    }

    public async Task ExecuteAsync(string sql, Action<MySqlCommand>? bind = null)
    {
        if (!Ready) return;
        try
        {
            await using var c = await OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            bind?.Invoke(cmd);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { _log.LogWarning(ex, "[CS2Suite] SQL 执行失败: " + Trunc(sql)); }
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Action<MySqlCommand>? bind = null)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (!Ready) return rows;
        try
        {
            await using var c = await OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            bind?.Invoke(cmd);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < rd.FieldCount; i++)
                    row[rd.GetName(i)] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                rows.Add(row);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "[CS2Suite] SQL 查询失败: " + Trunc(sql)); }
        return rows;
    }

    /// <summary>
    /// 同一连接内执行 INSERT 并取回自增 id。
    /// ★ LAST_INSERT_ID() 是"每连接"语义,连接池下必须与 INSERT 同连接查询,否则拿到别的连接/0 值。
    /// </summary>
    public async Task<long> InsertReturnIdAsync(string sql, Action<MySqlCommand>? bind = null)
    {
        if (!Ready) return 0;
        try
        {
            await using var c = await OpenAsync();
            await using (var ins = c.CreateCommand())
            {
                ins.CommandText = sql;
                bind?.Invoke(ins);
                await ins.ExecuteNonQueryAsync();
            }
            await using var sel = c.CreateCommand();
            sel.CommandText = "SELECT LAST_INSERT_ID();";
            var v = await sel.ExecuteScalarAsync();
            return v is null or DBNull ? 0 : Convert.ToInt64(v);
        }
        catch (Exception ex) { _log.LogWarning(ex, "[CS2Suite] InsertReturnId 失败: " + Trunc(sql)); return 0; }
    }

    public static void P(MySqlCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public static string? S(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) ? v?.ToString() : null;

    public static long L(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null or DBNull) return 0;
        try { return Convert.ToInt64(v); } catch { return 0; }
    }

    private static string Trunc(string s) => s.Length <= 160 ? s : s[..160] + "...";

    /// <summary>首次启动建表;连接失败返回 false(整体降级),不影响其它功能。</summary>
    public async Task<bool> TryEnsureSchemaAsync()
    {
        try
        {
            // 先尝试建库(需要建库权限;没有则要求用户已手工导入 schema,见 README)
            try
            {
                await using var c = new MySqlConnection(
                    $"Server={_cfg.Host};Port={_cfg.Port};Uid={_cfg.User};Password={_cfg.Password};ConnectionTimeout=5;");
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_cfg.Database}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* 权限不足则继续;库已存在时不影响 */ }

            if (!_cfg.AutoCreateTables) { Ready = true; return true; }

            foreach (var ddl in SchemaDdl.Statements)
            {
                await using var c = await OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync();
            }
            Ready = true;
            _log.LogInformation("[CS2Suite] MySQL 表结构检查/创建完成。");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[CS2Suite] MySQL 初始化失败(检查 Database 配置)。数据相关功能禁用。");
            Ready = false;
            return false;
        }
    }

    public async Task HeartbeatAsync(string mode)
    {
        if (!Ready) return;
        var map = CounterStrikeSharp.API.Server.MapName;
        int players = 0;
        foreach (var p in CounterStrikeSharp.API.Utilities.GetPlayers())
            if (!p.IsBot) players++;
        await ExecuteAsync(
            "INSERT INTO cs2suite_servers (server_id, current_map, current_mode, players, last_heartbeat) " +
            "VALUES (@id,@map,@mode,@pl,NOW()) ON DUPLICATE KEY UPDATE " +
            "current_map=@map,current_mode=@mode,players=@pl,last_heartbeat=NOW();",
            cmd => { P(cmd, "@id", ServerIdRef); P(cmd, "@map", map); P(cmd, "@mode", mode); P(cmd, "@pl", players); });
    }

    public void HeartbeatSafe(string mode) => _ = Task.Run(() => HeartbeatAsync(mode));

    public void Dispose() => MySqlConnection.ClearAllPools();
}
