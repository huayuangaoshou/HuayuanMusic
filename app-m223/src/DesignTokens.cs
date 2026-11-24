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

    // ── 歌单行底（非当前行统一半透明底，压平氛围光晕透出差异）──
    public static readonly Color LightRowBase = Color.Parse("#80FFFFFF");
    public static readonly Color DarkRowBase  = Color.Parse("#80282530");
    public static readonly Color AccentSoftHover = Color.Parse("#14FBDDEB"); // AccentSoft 8%

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

    // R11 行高三档：大标题/区块 1.05~1.1，正文与列表行 1.5，歌词 1.6
    public const double LineHeightTitle = 1.08;
    public const double LineHeightBody  = 1.5;
    public const double LineHeightLyric = 1.6;

    // R11 字重语义：Demibold=大标题，Medium=区块标题/歌名/时间轴，Regular=正文/辅助。禁用 Light。
    public const string FontWeightHeading = "SemiBold"; // MiSans Demibold ≈ SemiBold
    public const string FontWeightBlock   = "Medium";
    public const string FontWeightBody    = "Regular";

    // R11 辅助文本：12px + alpha 0.62；数字等宽 13px Medium
    public const double FontAux    = 12;
    public const double FontMono   = 13;
    public const double AuxOpacity = 0.62;

    // ── 动效时长（R11 三档：微反馈 / 组件开合 / 视图切换）────
    public const int QuickMs   = 140;
    public const int NormalMs  = 200;
    public const int ListStaggerMs  = 18;   // 列表进场错峰
    public const int ListTotalMs   = 300;   // 列表进场总时长硬上限
    public const int LyricScrollMs = 300;   // 歌词容器滚动
    public const int CoverCrossfadeMs = 400; // 切歌背景过渡
    public const int LyricOpacityMs  = 200; // 歌词行透明度过渡

    // R11 动效时长三档（微反馈 150~200 / 组件开合 250~300 / 视图切换 400）
    public const int MotionMicro = 180; // hover、按压、勾选、红心点亮
    public const int MotionOpen  = 280; // 抽屉、弹层、菜单、评论面板
    public const int MotionView  = 400; // 页面跳转、列表重排

    // R11 进场编排：标题 0ms → 内容卡 +60ms → 列表行逐行 +30ms（最多前 10 行）
    public const int ChoreoCardDelay   = 60;
    public const int ChoreoRowStagger  = 30;
    public const int ChoreoRowMax      = 10;
    public const double ChoreoOffsetPx = 12; // 进场元素统一位移 12px + 淡入

    // R11 液态回弹：仅弹层开合与悬浮播放条出现，位移过冲 ≈4%，禁果冻弹跳
    public const double BackEaseAmplitude = 0.4;

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

    // ── Z 轴四层景深（R11）。层级越高 → frost 越低（越透）、阴影越大越淡。──
    public const int DepthLayerAtmosphere = 0; // L0 氛围底纹：无材质、无阴影
    public const int DepthLayerPanel      = 1; // L1 内容卡/列表：panel
    public const int DepthLayerChrome     = 2; // L2 chrome 播放条/侧栏/顶栏
    public const int DepthLayerFloating   = 3; // L3 悬浮层菜单/弹窗/桌面歌词
    // 材质 frost 分档（接 R9 材质体系）
    public const double FrostPanel    = 0.60; // L1
    public const double FrostChrome   = 0.72; // L2
    public const double FrostFloating = 0.48; // L3
    // 阴影分档（唯一来源 GlassLift；层级越高阴影越大越淡）
    public const double ShadowL2Blur     = 24;   // L2 chrome: 0 8 24 α0.18
    public const double ShadowL2Opacity  = 0.18;
    public const double ShadowL3Blur     = 48;   // L3 floating: 0 16 48 α0.25
    public const double ShadowL3Opacity  = 0.25;
    // 弹窗 scrim
    public const double ScrimOpacity = 0.32; // 黑 α0.32 + 模糊 8px
    public static readonly Color ScrimColor = Color.Parse("#52000000"); // α 0.32
}
