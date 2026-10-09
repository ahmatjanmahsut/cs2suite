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
    public void SetServerId(string? id) { if (!string.IsNullOrWhiteSpace(id)) ServerIdRef = id!; }

    private string ConnString =>
        BaseConnString + $"Database={_cfg.Database};";

    /// <summary>不含库名的连接串(建库探测用)。ConnectionTimeout 给足,避免游戏进程启动期线程池繁忙导致握手超时。</summary>
    private string BaseConnString =>
        $"Server={_cfg.Host};Port={_cfg.Port};" +
        $"Uid={_cfg.User};Password={_cfg.Password};SslMode=Preferred;Pooling=true;" +
        "MaximumPoolSize=16;ConnectionTimeout=15;Charset=utf8mb4;AllowPublicKeyRetrieval=True;";

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
                await using var c = new MySqlConnection(BaseConnString);
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_cfg.Database}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "[CS2Suite] 建库探测跳过(通常是无建库权限,库已存在时不影响)");
            }

            if (!_cfg.AutoCreateTables) { Ready = true; return true; }

            // ★ 复用单条连接执行全部 DDL:避免 19 次握手,
            //   也避免游戏进程启动期大量并发连接导致握手超时(实测过该问题)。
            await using var conn = await OpenAsync();
            foreach (var ddl in SchemaDdl.Statements)
            {
                await using var cmd = conn.CreateCommand();
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
            _log.LogError($"[CS2Suite] 实际连接目标: Host='{_cfg.Host}' Port={_cfg.Port} User='{_cfg.User}' Database='{_cfg.Database}' PasswordLength={_cfg.Password?.Length ?? 0}");
            Ready = false;
            return false;
        }
    }

    /// <summary>
    /// 心跳写入。★ 必须在游戏线程调用:内部读取 Server.MapName / Utilities.GetPlayers()。
    /// 早期版本用 Task.Run 包裹导致这些调用跑在线程池线程上,
    /// 异常被上层 try/catch 吞掉 → 服务器列表里永远看不到本服(已在真机复现并修正)。
    /// </summary>
    public async Task HeartbeatAsync(string mode)
    {
        if (!Ready) return;

        // 游戏线程内先取快照
        string map;
        int players = 0;
        try
        {
            map = CounterStrikeSharp.API.Server.MapName;
            foreach (var p in CounterStrikeSharp.API.Utilities.GetPlayers())
                if (p.IsValid && !p.IsBot) players++;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[CS2Suite] 心跳快照失败(非游戏线程?),本轮跳过");
            return;
        }

        await ExecuteAsync(
            "INSERT INTO cs2suite_servers (server_id, current_map, current_mode, players, last_heartbeat) " +
            "VALUES (@id,@map,@mode,@pl,NOW()) ON DUPLICATE KEY UPDATE " +
            "current_map=@map,current_mode=@mode,players=@pl,last_heartbeat=NOW();",
            cmd => { P(cmd, "@id", ServerIdRef); P(cmd, "@map", map); P(cmd, "@mode", mode); P(cmd, "@pl", players); });
    }

    /// <summary>从游戏线程发起:仅把纯 DB 写入交给线程池,快照已在调用线程内完成。</summary>
    public void HeartbeatSafe(string mode)
    {
        if (!Ready) { _log.LogWarning("[CS2Suite] 心跳跳过:数据库未就绪"); return; }
        string map;
        int players = 0;
        try
        {
            map = CounterStrikeSharp.API.Server.MapName;
            foreach (var p in CounterStrikeSharp.API.Utilities.GetPlayers())
                if (p.IsValid && !p.IsBot) players++;
        }
        catch (Exception ex) { _log.LogWarning(ex, "[CS2Suite] 心跳快照失败"); return; }

        var id = ServerIdRef;
        _log.LogInformation($"[CS2Suite] 心跳写入: id={id} map={map} mode={mode} players={players}");
        _ = Task.Run(async () =>
        {
            await ExecuteAsync(
                "INSERT INTO cs2suite_servers (server_id, current_map, current_mode, players, last_heartbeat) " +
                "VALUES (@id,@map,@mode,@pl,NOW()) ON DUPLICATE KEY UPDATE " +
                "current_map=@map,current_mode=@mode,players=@pl,last_heartbeat=NOW();",
                cmd => { P(cmd, "@id", id); P(cmd, "@map", map); P(cmd, "@mode", mode); P(cmd, "@pl", players); });
            _log.LogInformation("[CS2Suite] 心跳写入完成");
        });
    }

    public void Dispose() => MySqlConnection.ClearAllPools();
}
