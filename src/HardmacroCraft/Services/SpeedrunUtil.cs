namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.7：速通工具箱纯数学（传送门坐标换算 / 末影之眼三角定位 / 计时格式）。
/// 独立静态类，无 UI 依赖，headless 可测。
/// </summary>
public static class SpeedrunUtil
{
    /// <summary>主世界 ↔ 下界 1:8 换算（Y 不变）。netherToOverworld=false 时主世界→下界（÷8 向零取整）。</summary>
    public static int PortalConvert(int coord, bool netherToOverworld)
    {
        // 向零取整（floor 对负数会偏一格；MC 实际生成按最近门户，向零取整更贴近玩家预期）
        return netherToOverworld ? coord * 8 : (int)((double)coord / 8);
    }

    /// <summary>计时格式化：MM:SS.mmm（小时不封顶，H:MM:SS.mmm）。</summary>
    public static string FormatTimer(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
    }

    /// <summary>
    /// 末影之眼三角定位：两次投掷点 + F3 Facing 的 yaw 角 → 最近要塞交汇点。
    /// MC yaw 约定：0=南(+Z)、90=西(-X)、180=北(-Z)、270=东(+X)，
    /// 即水平方向向量 D = (-sin(yaw), cos(yaw))（度）。
    /// 解 X = P1 + t1·D1 = P2 + t2·D2 的最小二乘交点；返回命中点与两次投掷的水平距离。
    /// rays 平行（行列式≈0）返回 null。
    /// </summary>
    public static (double X, double Z, double Dist1, double Dist2)? Triangulate(
        (double X, double Z, double Yaw) throw1, (double X, double Z, double Yaw) throw2)
    {
        var (dx1, dz1) = DirectionFromYaw(throw1.Yaw);
        var (dx2, dz2) = DirectionFromYaw(throw2.Yaw);
        // 2×2 线性方程组：P2 - P1 = t1·D1 - t2·D2
        var px = throw2.X - throw1.X;
        var pz = throw2.Z - throw1.Z;
        var det = dx1 * (-dz2) - dz1 * (-dx2);
        if (Math.Abs(det) < 1e-9) return null; // 平行 / 共线：无唯一交点
        // Cramer：t1 = (px·(-dz2) - pz·(-dx2)) / det
        var t1 = (px * (-dz2) - pz * (-dx2)) / det;
        var x = throw1.X + t1 * dx1;
        var z = throw1.Z + t1 * dz1;
        var d1 = Math.Sqrt(Math.Pow(x - throw1.X, 2) + Math.Pow(z - throw1.Z, 2));
        var d2 = Math.Sqrt(Math.Pow(x - throw2.X, 2) + Math.Pow(z - throw2.Z, 2));
        return (x, z, d1, d2);
    }

    /// <summary>MC yaw（度）→ 水平单位方向向量 (dx, dz)。yaw=0 → (0,1) 南。</summary>
    public static (double Dx, double Dz) DirectionFromYaw(double yaw)
    {
        var rad = yaw * Math.PI / 180.0;
        return (-Math.Sin(rad), Math.Cos(rad));
    }
}
