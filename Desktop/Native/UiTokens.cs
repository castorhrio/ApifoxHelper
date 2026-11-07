using System.Drawing;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 设计令牌的唯一代码落点，值与 docs/DESIGN.md 一一对应；
/// 改任何值必须同一次改动更新该文档（设计验收 D1：无就地发明值）。
/// 尺寸均为 96dpi 逻辑像素，随系统 PerMonitorV2 缩放，不做手工换算。
/// </summary>
internal static class UiTokens
{
    // ── 颜色（docs/DESIGN.md §2）───────────────────────────────────────────
    public static readonly Color Bg = FromHex(0xF5F7FB);
    public static readonly Color Panel = FromHex(0xFFFFFF);
    public static readonly Color Line = FromHex(0xE3E8F0);
    public static readonly Color Text = FromHex(0x1F2937);
    public static readonly Color Muted = FromHex(0x6B7280);
    public static readonly Color Nav = FromHex(0x111827);
    public static readonly Color NavText = FromHex(0xE5E7EB);
    public static readonly Color NavSelected = FromHex(0x1F2937);
    public static readonly Color Accent = FromHex(0x0B5394);
    public static readonly Color AccentHover = FromHex(0x0A4A82);
    public static readonly Color OkText = FromHex(0x15803D);
    public static readonly Color WarnText = FromHex(0xB45309);
    public static readonly Color BadText = FromHex(0xB91C1C);

    // ── 字号四档（docs/DESIGN.md §3，磅 = 96dpi 像素 × 0.75）───────────────
    public const float CaptionPt = 9f;    // 12px
    public const float BodyPt = 10.5f;    // 14px
    public const float SectionPt = 15f;   // 20px
    public const float TitlePt = 21f;     // 28px

    // ── 间距五档（docs/DESIGN.md §4）──────────────────────────────────────
    public const int Xs = 4;
    public const int Sm = 8;
    public const int Md = 12;
    public const int Lg = 16;
    public const int Xl = 24;

    // 字体栈（docs/DESIGN.md §3）：有序回退，找到即停，全不匹配用系统无衬线。
    private static readonly string[] FamilyStack = ["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei"];
    private static readonly string[] MonoStack = ["Cascadia Mono", "Consolas", "Courier New"];

    private static readonly Lazy<string> Family = new(ResolveFamily(FamilyStack));
    private static readonly Lazy<string> MonoFamily = new(ResolveFamily(MonoStack));

    // 共享字体实例只创建不释放（与进程同生命周期），避免每控件新建。
    public static readonly Lazy<Font> Caption = new(() => Create(CaptionPt));
    public static readonly Lazy<Font> CaptionBold = new(() => Create(CaptionPt, FontStyle.Bold));
    public static readonly Lazy<Font> Body = new(() => Create(BodyPt));
    public static readonly Lazy<Font> BodyBold = new(() => Create(BodyPt, FontStyle.Bold));
    public static readonly Lazy<Font> Section = new(() => Create(SectionPt));
    public static readonly Lazy<Font> Title = new(() => Create(TitlePt));
    public static readonly Lazy<Font> Mono = new(() => new Font(MonoFamily.Value, BodyPt));

    public static Font Create(float points, FontStyle style = FontStyle.Regular) => new(Family.Value, points, style);

    private static string ResolveFamily(string[] stack)
    {
        foreach (var name in stack)
        {
            try
            {
                using var probe = new FontFamily(name);
                return name;
            }
            catch (ArgumentException)
            {
                // 本机未安装该字体（FontFamily 构造抛 ArgumentException），尝试下一个。
            }
        }
        return FontFamily.GenericSansSerif.Name;
    }

    private static Color FromHex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
