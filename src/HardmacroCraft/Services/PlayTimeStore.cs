using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.0 游戏时长统计（HMCL/PCL2 同类功能）。
/// config/playtime.json：{ "版本id": 累计秒 }。游戏退出时由 LauncherService 落账，
/// 一律先写临时文件再改名（与 DownloadEngine 落盘习惯一致）。
/// </summary>
public static class PlayTimeStore
{
    private static readonly object Gate = new();
    private static ConcurrentDictionary<string, long>? _cache;
    private static string StorePath => Path.Combine(AppSettings.ConfigDir, "playtime.json");

    private static ConcurrentDictionary<string, long> Load()
    {
        if (_cache is not null) return _cache;
        var dict = new ConcurrentDictionary<string, long>();
        try
        {
            var path = StorePath;
            if (File.Exists(path))
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? new())
                    dict[kv.Key] = kv.Value;
        }
        catch { /* 损坏文件按空表处理，下次落账时覆盖 */ }
        _cache = dict;
        return dict;
    }

    /// <summary>累计一次会话时长（秒；非正数忽略）。</summary>
    public static void AddSession(string versionId, double seconds)
    {
        if (seconds <= 0 || string.IsNullOrEmpty(versionId)) return;
        var dict = Load();
        dict.AddOrUpdate(versionId, (long)seconds, (_, old) => old + (long)seconds);
        Save();
    }

    /// <summary>累计游玩秒数（未记录返回 0）。</summary>
    public static long GetTotalSeconds(string versionId)
    {
        var dict = Load();
        return dict.TryGetValue(versionId, out var v) ? v : 0;
    }

    /// <summary>展示用短文本（如 "1.5 小时" / "23 分钟"）。</summary>
    public static string Format(long totalSeconds)
    {
        if (totalSeconds <= 0) return "暂无记录";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalMinutes < 1) return $"{ts.Seconds} 秒";
        if (ts.TotalHours < 1) return $"{ts.Minutes} 分钟";
        return $"{ts.TotalHours:F1} 小时";
    }

    private static void Save()
    {
        try
        {
            var path = StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var snapshot = new Dictionary<string, long>(Load());
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* 落盘失败静默：统计非关键数据 */ }
    }
}
