namespace HardmacroCraft.Services;

/// <summary>
/// 版本类型识别（fix9，PCL 同款）：愚人节版独立分类。
/// Mojang 清单里愚人节版的 type 与快照同为 "snapshot"，只能按已知 ID 识别。
/// 清单来源：Minecraft Wiki「April Fools' Day jokes」历年年鉴（2013-2026 共 10 个，
/// 2011 / 2012 两年为网站玩笑，无游戏版本）。
/// </summary>
public static class VersionKinds
{
    /// <summary>历届愚人节版本 ID（清单内实际 ID，区分大小写按原样匹配 + 忽略大小写兜底）。</summary>
    public static readonly string[] AprilFoolsVersions =
    {
        "2.0",                   // 2013 Minecraft 2.0
        "1.RV-Pre1",             // 2016 时尚更新（Trendy）
        "15w14a",                // 2016 爱与拥抱（Love and Hugs）
        "3D Shareware v1.34",    // 2019 1990 年代复古 3D
        "20w14infinite",         // 2020 无限维度
        "22w13oneBlockAtATime",  // 2022 一次一个方块
        "23w13a_or_b",           // 2023 投票更新
        "24w14potato",           // 2024 毒土豆更新
        "25w14craftmine",        // 2025 合成矿（Craftmine）
        "26w14a",                // 2026 兽群工艺（Herdcraft）
    };

    /// <summary>是否愚人节版（忽略大小写，容忍清单大小写差异）。</summary>
    public static bool IsAprilFools(string? versionId)
        => versionId != null &&
           AprilFoolsVersions.Contains(versionId, StringComparer.OrdinalIgnoreCase);
}
