using Avalonia.Animation.Easings;
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
    public static readonly Color LightAccentInk = Color.Parse("#942B5A");
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

    // ── 歌单表面与行状态 ──
    public static readonly Color LightRowBase = Color.Parse("#FFFFFFFF");
    public static readonly Color DarkRowBase  = Color.Parse("#FF282530");
    public const double SongHoverOpacity = .05;
    public const double SongPlayingOpacity = .09;
    public const double SongSelectionOpacity = .65;
    public const double DockSpectrumOpacity = .55;

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
    public static Avalonia.CornerRadius NoticeRadius { get; } = new(RadiusL);

    // ── R10 布局数学（YesPlayMusic 移植：只抄参数与节奏）──────
    public const double ContentPadTop     = 64;   // 内容区上留白
    public const double ContentPadBottom  = 96;   // 底部留白（给悬浮播放条让位）
    public const double ContentPadSideMin = 64;   // 左右留白下限（5vw 与 64px 取大）
    public const double ContentMaxWidth   = 1336; // 内容最大宽居中封顶
    public const double DockHeight        = 64;   // 悬浮播放条高
    public const double PageTitleMin      = 36;   // 页面大标题 36~42
    public const double PageTitleMax      = 42;
    public const double BlockTitleSize    = 18;   // 区块标题
    public const double SongRowCover      = 46;   // 歌曲行内封面
    public const double SongRowHeight     = 62;   // 歌曲行自然高度
    public const double QueueRowHeight    = 56;
    public const double SongRowPad        = 8;    // 歌曲行 padding

    // ── 字号（阶梯）──────────────────────────────────────────
    public const double FontXs   = 13;   // 最小 13px
    public const double FontSm   = 14;
    public const double FontMd   = 16;
    public const double FontLg   = 20;
    public const double FontXl   = 28;

    // ── 字体栈（唯一来源）────────────────────────────────────
    public static readonly Color DesktopLyricShadow = Color.Parse("#0B1020");
    public static TimeSpan ProviderUiTimeout { get; } = TimeSpan.FromSeconds(20);
    public const int StateSkeletonRows = 4;
    public const double StateMessageWidth = 420;
    public const double NoticeLineHeight = 24; // 15px × 1.6
    public const double StateCopySize = 15;
    public const double TitleTracking = -0.30;
    public const double PlayingTitleSize = 24;
    public const double DesktopLyricEntryOffset = 6;
    public const double TrackingCaptionSize = 11;
    public const double CaptionTracking = 0.11; // +1% at 11px
    public const string LyricSerifFonts = "avares://HuayuanMusic.P17.Nightflight/Assets/Fonts#LXGW WenKai Lite, SimSun, 宋体, $Default";
    public static Avalonia.Media.FontFamily LyricSerif { get; } = new(LyricSerifFonts);

    public const string FontLatin  = "Segoe UI Variable, Segoe UI";
    public const string FontCjk    = "Microsoft YaHei UI";
    public const string FontFamily = "avares://HuayuanMusic.P17.Nightflight/Assets/Fonts/MiSans-Regular.ttf#MiSans, MiSans, Segoe UI Variable, Microsoft YaHei UI";
    public static Avalonia.Media.FontFamily BodyFont { get; } = new(FontFamily);
    public static Avalonia.Media.FontFamily BodyDemibold { get; } =
        new("avares://HuayuanMusic.P17.Nightflight/Assets/Fonts/MiSans-Demibold.ttf#MiSans, MiSans, Segoe UI Variable, Microsoft YaHei UI");
    // Xiaomi's original TTF assigns this weight class to Demibold.
    public static FontWeight MiSansDemiboldWeight => (FontWeight)450;

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

    // Spatial 用于位移、尺寸和缩放，允许过冲。Effects 用于透明度和颜色，保持单调。
    public sealed record MotionSpec(TimeSpan Duration, Easing Curve);

    public static MotionSpec MotionSpatialFast { get; } = new(TimeSpan.FromMilliseconds(350), Easing.Parse(".42,1.67,.21,.90"));
    public static MotionSpec MotionSpatialDefault { get; } = new(TimeSpan.FromMilliseconds(500), Easing.Parse(".38,1.21,.22,1"));
    public static MotionSpec MotionSpatialSlow { get; } = new(TimeSpan.FromMilliseconds(650), Easing.Parse(".39,1.29,.35,.98"));
    public static MotionSpec MotionEffectsFast { get; } = new(TimeSpan.FromMilliseconds(170), Easing.Parse(".2,0,0,1"));
    public static MotionSpec MotionEffectsDefault { get; } = new(TimeSpan.FromMilliseconds(260), Easing.Parse(".2,0,0,1"));
    public static MotionSpec MotionEffectsSlow { get; } = new(TimeSpan.FromMilliseconds(350), Easing.Parse(".2,0,0,1"));
    public static Easing EmphasizedDecelerate { get; } = Easing.Parse(".05,.7,.1,1");
    public static Easing EmphasizedAccelerate { get; } = Easing.Parse(".3,0,.8,.15");
    public const int MotionStaggerStep = 32;

    // XAML 的 x:Static 读取单层成员。
    public static TimeSpan MotionSpatialFastDuration => MotionSpatialFast.Duration;
    public static Easing MotionSpatialFastEasing => MotionSpatialFast.Curve;
    public static TimeSpan MotionSpatialDefaultDuration => MotionSpatialDefault.Duration;
    public static Easing MotionSpatialDefaultEasing => MotionSpatialDefault.Curve;
    public static TimeSpan MotionSpatialSlowDuration => MotionSpatialSlow.Duration;
    public static Easing MotionSpatialSlowEasing => MotionSpatialSlow.Curve;
    public static TimeSpan MotionEffectsFastDuration => MotionEffectsFast.Duration;
    public static Easing MotionEffectsFastEasing => MotionEffectsFast.Curve;
    public static TimeSpan MotionEffectsDefaultDuration => MotionEffectsDefault.Duration;
    public static Easing MotionEffectsDefaultEasing => MotionEffectsDefault.Curve;
    public static TimeSpan MotionEffectsSlowDuration => MotionEffectsSlow.Duration;
    public static Easing MotionEffectsSlowEasing => MotionEffectsSlow.Curve;

    public static TimeSpan BufferPulseDuration { get; } = TimeSpan.FromMilliseconds(680);

    public const int ChoreoCardDelay = 60;
    public const int ChoreoRowMax = 10;
    public const double ChoreoOffsetPx = 12;

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
    // Regular 用于导航和弹层；Clear 只用于媒体背景上的控件。
    public const double FrostRegular = 0.64;
    public const double FrostClear = 0.32;
    public const double ClearBackdropDimOpacity = 0.35;
    public const double ClearOpticsOpacity = 0.28;
    // R9 透出色提饱和幅度（随 frost 档位对齐：chrome +18% / panel +28% / floating +34%）
    // 数值 = 1 + 提幅，shader 内 mix(luma, color, saturation)。
    public const double SaturationChrome   = 1.18; // L2 chrome 播放条/侧栏/顶栏
    public const double SaturationPanel    = 1.28; // L1 内容卡/列表
    public const double SaturationFloating = 1.34; // L3 悬浮层菜单/弹窗
    // 夜间模式提饱和幅度降为 1.15（避免暗底上彩色溢出刺眼）
    public const double SaturationNight = 1.15;
    // R9 降级：软渲染 / 低性能设备跳过采样与色散（保留提饱和与边缘高光廉价近似）
    public static bool ShaderDegrade => Program.Args.Contains("--software") || Program.Args.Contains("--low");
    // 阴影分档（唯一来源 GlassLift；层级越高阴影越大越淡）
    public const double ShadowL2Blur     = 24;   // L2 chrome: 0 8 24 α0.18
    public const double ShadowL2Opacity  = 0.18;
    public const double ShadowL3Blur     = 48;   // L3 floating: 0 16 48 α0.25
    public const double ShadowL3Opacity  = 0.25;
    // 弹窗 scrim
    public const double ScrimOpacity = 0.32; // 黑 α0.32 + 模糊 8px
    public static readonly Color ScrimColor = Color.Parse("#52000000"); // α 0.32
}
