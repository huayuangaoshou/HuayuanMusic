using Avalonia.Media;

namespace HuayuanPreview;

/// <summary>
/// 全项目唯一的视觉设计令牌来源（Design Token）。
/// 颜色 / 间距 / 圆角 / 字号 / 字体 / 动效时长均在此定义；
/// 视觉表现层不得再散写硬编码 hex 或魔法数字。
/// 色板主题：粉主 · 蓝辅 · 白底（Pink primary / Blue secondary / White surface）。
/// </summary>
public static class DesignTokens
{
    // ── 主色（粉） ────────────────────────────────────────────
    /// <summary>播放键 / 当前歌词 / 进度条</summary>
    public static readonly Color AccentStrong = Color.Parse("#E88AB8");
    /// <summary>hover 态</summary>
    public static readonly Color AccentMid    = Color.Parse("#F0A9C9");
    /// <summary>选中底色</summary>
    public static readonly Color AccentSoft   = Color.Parse("#FBDDEB");
    /// <summary>次级点缀（蓝），禁大面积使用</summary>
    public static readonly Color BlueMid      = Color.Parse("#8FA8E8");

    // ── 浅色主题 ─────────────────────────────────────────────
    public static readonly Color LightBase    = Color.Parse("#FAF7F4");
    public static readonly Color LightSurface = Color.Parse("#FFFFFF");
    public static readonly Color LightText    = Color.Parse("#2A2530");
    public static readonly Color LightSub     = Color.Parse("#7A7385");
    public static readonly Color LightLine    = Color.Parse("#E5E0E8");

    // ── 深色主题 ─────────────────────────────────────────────
    public static readonly Color DarkBase     = Color.Parse("#1C1A22");
    public static readonly Color DarkSurface  = Color.Parse("#282530");
    public static readonly Color DarkText     = Color.Parse("#F2EFF5");
    public static readonly Color DarkSub      = Color.Parse("#A8A2B2");
    public static readonly Color DarkLine     = Color.Parse("#3A3542");

    // ── 派生色（选中底 / 悬停 / 斑马纹的深浅两态）────────────
    public static readonly Color LightHover = Color.Parse("#FBF9F6");
    public static readonly Color LightStripe = Color.Parse("#FBF9F6");
    public static readonly Color DarkHover  = Color.Parse("#352C3A");
    public static readonly Color DarkStripe = Color.Parse("#211E28");
    public static readonly Color DarkTint   = Color.Parse("#3A2A36");

    // ── 状态色（浅色 / 深色）────────────────────────────────
    public static readonly Color LightError   = Color.Parse("#9B2745");
    public static readonly Color DarkError    = Color.Parse("#FFB4C0");
    public static readonly Color LightSuccess = Color.Parse("#176449");
    public static readonly Color DarkSuccess  = Color.Parse("#84DBC4");
    public static readonly Color LightWarning = Color.Parse("#7A5100");
    public static readonly Color DarkWarning  = Color.Parse("#F1D090");

    // ── 间距（4pt 网格）──────────────────────────────────────
    public const double Space1 = 4;
    public const double Space2 = 8;
    public const double Space3 = 12;
    public const double Space4 = 16;
    public const double Space5 = 24;
    public const double Space6 = 32;

    // ── 圆角（三档）──────────────────────────────────────────
    public const double RadiusS = 8;
    public const double RadiusM = 12;
    public const double RadiusL = 16;

    // ── 字号（阶梯）──────────────────────────────────────────
    public const double FontXs   = 13;   // 最小 13px
    public const double FontSm   = 14;
    public const double FontMd   = 16;
    public const double FontLg   = 20;
    public const double FontXl   = 28;

    // ── 字体栈（唯一来源）────────────────────────────────────
    public const string FontLatin  = "Segoe UI Variable, Segoe UI";
    public const string FontCjk    = "Microsoft YaHei UI";
    public const string FontFamily = "MiSans, MiSans, Segoe UI Variable, Microsoft YaHei UI";

    // ── 行高系数 ─────────────────────────────────────────────
    public const double LineHeightRatio = 1.45;

    // ── 动效时长 ─────────────────────────────────────────────
    public const int QuickMs   = 140;
    public const int NormalMs  = 200;
    public const int ListStaggerMs  = 18;   // 列表进场错峰
    public const int ListTotalMs   = 300;   // 列表进场总时长硬上限
    public const int LyricScrollMs = 300;   // 歌词容器滚动
    public const int CoverCrossfadeMs = 400; // 切歌背景过渡
    public const int LyricOpacityMs  = 200; // 歌词行透明度过渡

    // ── 阴影 ─────────────────────────────────────────────────
    public const double HoverLift   = -2;       // hover 上移 px
    public const double ShadowBlurIdle  = 16;
    public const double ShadowBlurHover = 24;
    public const double ShadowOpacityIdle  = 0.08;
    public const double ShadowOpacityHover = 0.16;

    // ── 封面取色参数 ─────────────────────────────────────────
    public const int CoverSampleSize = 64;      // 缩 64×64
    public const double CoverMinSaturation = 0.15; // S>0.15 优先
    public const double CoverGrayWeight    = 0.5;  // 灰像素减半

    /// <summary>深色模式封面染背景：S≤0.35、V≤0.28</summary>
    public const double DarkBackSaturation = 0.35;
    public const double DarkBackValue      = 0.28;
    /// <summary>浅色模式封面染背景：S≤0.20、V≥0.92</summary>
    public const double LightBackSaturation = 0.20;
    public const double LightBackValue      = 0.92;
    /// <summary>进度条 / 播放键底光：S≤0.5</summary>
    public const double GlowSaturation = 0.5;

    // ── Acrylic ──────────────────────────────────────────────
    public const double AcrylicTintOpacity = 0.6; // ExperimentalAcrylicBorder TintOpacity 上限
    public const double AcrylicShadowBlur   = 24;
    public const double AcrylicShadowOpacity = 0.14;
}
