namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 字体栈的有序回退口径（docs/DESIGN.md §3）：按顺序探测，找到即停，全不匹配回退系统无衬线。
/// 不依赖 WinForms 与 GDI+，与测试工程共享编译——「先试哪个、试不到怎么办」这套判定
/// 在 Linux 上真执行，界面层只提供「这个名字本机装没装」这一个事实。
///
/// 存在的理由：WinForms 的 <c>Control.Font</c> 不支持 CSS 式有序回退——把字体名写死在一个
/// <c>Font</c> 构造里，在缺这个字体的机器（老 Windows、精简系统）上会静默落到系统默认字体，
/// 而 DESIGN §3 承诺的是「Segoe UI → Microsoft YaHei UI → Microsoft YaHei → SimHei，找到即停」。
/// 承诺与实现之间差的就是这一层探测：栈本身在这里，探测结果由调用方给，回退顺序只写一份。
///
/// 两处共用同一份口径：界面控件（<see cref="UiTokens"/> 的四档字号工厂）与进程默认字体
/// （<c>Application.SetDefaultFont</c>，决定 MessageBox、菜单等未显式设字体的控件）。
/// 两处各写一份必然漂移——一处回退到雅黑、另一处回退到系统默认，同一只壳里两种字。
/// </summary>
public static class FontStack
{
    /// <summary>主字体栈（docs/DESIGN.md §3，顺序即回退顺序；刻意排除衬线中文字体）。</summary>
    public static readonly string[] Families = ["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei"];

    /// <summary>等宽栈（docs/DESIGN.md §3：计数、进度、ID、路径、差异文本必须等宽）。</summary>
    public static readonly string[] MonoFamilies = ["Cascadia Mono", "Consolas", "Courier New"];

    /// <summary>
    /// 按顺序取第一个「本机已装」的字体名；空栈、全不匹配、名字为空白一律回退
    /// <paramref name="fallback"/>（调用方给系统无衬线的名字）。
    /// 空白项跳过而不是当成一个字体名去探测：配置或文档里多一个空串不该让整条栈失效。
    /// </summary>
    public static string Resolve(IReadOnlyList<string> stack, Func<string, bool> isInstalled, string fallback)
    {
        foreach (var name in stack)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (isInstalled(name)) return name;
        }
        return fallback;
    }
}
