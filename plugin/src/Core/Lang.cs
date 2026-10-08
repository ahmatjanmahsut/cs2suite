using System.Text.Json;

namespace CS2Suite.Core;

/// <summary>
/// 迷你本地化:加载插件目录 lang/{zh,en}.json;缺键返回键名,缺文件回退英文再回退中文键。
/// 聊天输出经 ChatMsg() 统一转义,避免玩家内容注入格式化控制符。
/// </summary>
public sealed class Lang
{
    private readonly Dictionary<string, string> _kv = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _fallbackLang;

    public Lang(string moduleDir, string language)
    {
        _fallbackLang = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        Load(Path.Combine(moduleDir, "lang", language + ".json"));
    }

    public Lang Fallback(string moduleDir)
    {
        Load(Path.Combine(moduleDir, "lang", _fallbackLang + ".json"));
        return this;
    }

    private void Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    _kv.TryAdd(prop.Name, prop.Value.GetString() ?? prop.Name);
        }
        catch { /* 语言文件损坏不致命 */ }
    }

    public string T(string key) => _kv.TryGetValue(key, out var v) ? v : key;

    public string T(string key, params (string, object)[] vars)
    {
        var s = T(key);
        foreach (var (k, val) in vars)
            s = s.Replace("{" + k + "}", val?.ToString() ?? "");
        return s;
    }
}
