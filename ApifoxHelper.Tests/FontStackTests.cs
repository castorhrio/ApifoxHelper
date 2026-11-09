using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 字体栈有序回退断言（DESIGN.md §3、§8 本轮条款）：DESIGN §3 承诺
/// 「Segoe UI → Microsoft YaHei UI → Microsoft YaHei → SimHei，找到即停，全不匹配回退系统字体」，
/// 而 WinForms 的 <c>Control.Font</c> 不支持 CSS 式回退——<c>new Font("Microsoft YaHei UI", 9)</c>
/// 在缺这个字体的机器上会静默落到系统默认，承诺与实现之间差的正是这一层探测。
/// 本轮把栈与回退判定收进 <see cref="FontStack"/>（不依赖 WinForms、与测试工程共享编译），
/// 界面控件与进程默认字体（<c>Application.SetDefaultFont</c>）共用同一份结果。
///
/// 断言分两半：① 判定本身在 Linux 上真执行（真值表 + 探测顺序 + 空白项跳过）；
/// ② 接线用源码切片与行首正则钉住（界面层只提供「装没装」这一个事实，两处入口不再各写一份字体名）。
/// </summary>
public class FontStackTests
{
    private const string Fallback = "系统无衬线";

    // ── ① 判定本身（真执行）────────────────────────────────────────────

    [Fact]
    public void Resolve_PicksTheFirstInstalledFamily()
    {
        var probed = new List<string>();
        var resolved = FontStack.Resolve(["Segoe UI", "Microsoft YaHei UI", "SimHei"], name =>
        {
            probed.Add(name);
            return name is "Microsoft YaHei UI" or "SimHei";
        }, Fallback);

        Assert.Equal("Microsoft YaHei UI", resolved);
        // 找到即停：排在后面的 SimHei 不该被探测（探测一次就是一次 FontFamily 构造）。
        Assert.Equal(["Segoe UI", "Microsoft YaHei UI"], probed);
    }

    [Fact]
    public void Resolve_FirstEntryInstalled_ProbesOnlyOnce()
    {
        var probed = new List<string>();
        var resolved = FontStack.Resolve(["Segoe UI", "Microsoft YaHei UI"], name =>
        {
            probed.Add(name);
            return true;
        }, Fallback);

        Assert.Equal("Segoe UI", resolved);
        Assert.Single(probed);
    }

    [Fact]
    public void Resolve_AllMissing_FallsBackToSystemSansSerif()
    {
        var resolved = FontStack.Resolve(["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei"], _ => false, Fallback);
        Assert.Equal(Fallback, resolved);
    }

    [Fact]
    public void Resolve_EmptyStack_FallsBack()
    {
        Assert.Equal(Fallback, FontStack.Resolve([], _ => true, Fallback));
    }

    [Fact]
    public void Resolve_SkipsBlankEntries_WithoutProbingThem()
    {
        var probed = new List<string>();
        var resolved = FontStack.Resolve(["", "   ", "SimHei"], name =>
        {
            probed.Add(name);
            return true;
        }, Fallback);

        Assert.Equal("SimHei", resolved);
        // 空白项不当成一个字体名去探测：配置或文档里多一个空串不该让整条栈失效。
        Assert.Equal(["SimHei"], probed);
    }

    [Fact]
    public void Resolve_OnlyBlankEntries_FallsBack()
    {
        Assert.Equal(Fallback, FontStack.Resolve(["", " "], _ => true, Fallback));
    }

    [Fact]
    public void Resolve_ProbesInStackOrder()
    {
        var probed = new List<string>();
        FontStack.Resolve(["A", "B", "C", "D"], name =>
        {
            probed.Add(name);
            return false;
        }, Fallback);

        Assert.Equal(["A", "B", "C", "D"], probed);
    }

    // ── ② 栈本身与 DESIGN §3 同序 ───────────────────────────────────────

    [Fact]
    public void Families_AreTheDesignStack_AndContainNoBlanks()
    {
        Assert.Equal(["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei"], FontStack.Families);
        Assert.Equal(["Cascadia Mono", "Consolas", "Courier New"], FontStack.MonoFamilies);
        Assert.All(FontStack.Families, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.All(FontStack.MonoFamilies, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    /// <summary>栈只有一份：界面层不得再留一份自己的字体栈字面（两份必然漂移）。</summary>
    [Fact]
    public void Stack_IsDeclaredOnlyInFontStack()
    {
        // 只算代码行：注释里引用旧写法是说明理由，不是第二份栈。
        var uiTokens = CodeOnly(Repo.Source("Desktop", "Native", "UiTokens.cs"));
        Assert.DoesNotContain("FamilyStack", uiTokens, StringComparison.Ordinal);
        Assert.DoesNotContain("MonoStack", uiTokens, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Microsoft YaHei UI\"", uiTokens, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Segoe UI\"", uiTokens, StringComparison.Ordinal);

        var fontStack = CodeOnly(Repo.Source("Desktop", "Native", "FontStack.cs"));
        Assert.Contains("Families = [\"Segoe UI\"", fontStack, StringComparison.Ordinal);
        Assert.Contains("MonoFamilies = [\"Cascadia Mono\"", fontStack, StringComparison.Ordinal);
    }

    // ── ③ 接线：界面层只提供「装没装」，两处入口共用同一份结果 ──────────

    /// <summary>界面层把「本机装没装」这一个事实交给共享口径，回退顺序不在界面层重写。</summary>
    [Fact]
    public void UiTokens_DelegatesTheFallbackOrderToFontStack()
    {
        var uiTokens = Repo.Source("Desktop", "Native", "UiTokens.cs");

        Assert.Matches(@"FontStack\.Resolve\(stack, IsInstalled, FontFamily\.GenericSansSerif\.Name\)", uiTokens);
        Assert.Matches(@"ResolveFamily\(FontStack\.Families\)", uiTokens);
        Assert.Matches(@"ResolveFamily\(FontStack\.MonoFamilies\)", uiTokens);
        // 探测只在一处：FontFamily 构造在界面层恰一次（IsInstalled 里那一次）。
        Assert.Single(Regex.Matches(uiTokens, @"new FontFamily\("));
        // 回退顺序不在界面层重写：界面层不出现 foreach 遍历字体名。
        Assert.DoesNotContain("foreach (var name in stack)", uiTokens, StringComparison.Ordinal);
    }

    /// <summary>进程默认字体与界面控件同一份结果：两处入口都经 UiTokens 取字体，不再各写一个字体名。</summary>
    [Theory]
    [InlineData("Desktop", "DesktopApp.cs")]
    [InlineData("ApifoxHelper.Smoke", "Program.cs")]
    public void ProcessDefaultFont_UsesTheSameResolvedFamily(string dir, string file)
    {
        var source = Repo.Source(dir, file);

        Assert.Contains("SetDefaultFont(", source, StringComparison.Ordinal);
        Assert.Matches(@"SetDefaultFont\([^)]*UiTokens\.Create\([^)]*UiTokens\.CaptionPt\)\)", source);
        // 写死字体名的那一行是本轮修掉的缺陷：缺这个字体的机器上会静默落到系统默认。
        Assert.DoesNotContain("new Font(\"Microsoft YaHei UI\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDefaultFont(new Font(", source, StringComparison.Ordinal);
    }

    /// <summary>进程默认字体取的是 DESIGN §3 的 Caption 档（9pt），不是就地写一个磅值。</summary>
    [Fact]
    public void ProcessDefaultFont_UsesTheCaptionTier()
    {
        // 界面层是 internal（测试工程不引用主工程），故按源码钉住：Caption 档仍是 9pt（12px × 0.75）。
        var uiTokens = Repo.Source("Desktop", "Native", "UiTokens.cs");
        Assert.Matches(@"public const float CaptionPt = 9f;", uiTokens);

        foreach (var (dir, file) in new[] { ("Desktop", "DesktopApp.cs"), ("ApifoxHelper.Smoke", "Program.cs") })
        {
            var source = Repo.Source(dir, file);
            Assert.DoesNotContain("SetDefaultFont(new Font(", source, StringComparison.Ordinal);
            // 就地写磅值（9F / 10.5F …）等于绕过字号档：档位只在 UiTokens 一处。
            Assert.DoesNotMatch(@"SetDefaultFont\([^)]*\d+(\.\d+)?F", source);
        }
    }

    /// <summary>口径文件不依赖 WinForms / System.Drawing：判定要在 Linux 上真执行。</summary>
    [Fact]
    public void FontStack_DoesNotReferenceWinFormsOrDrawing()
    {
        var source = CodeOnly(Repo.Source("Desktop", "Native", "FontStack.cs"));
        Assert.DoesNotContain("System.Drawing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Font(", source, StringComparison.Ordinal);
    }

    /// <summary>共享编译清单：判定跑的是主工程同一份代码，不是测试侧另抄一份。</summary>
    [Fact]
    public void FontStack_IsSharedCompiledIntoTheTestAssembly()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\FontStack.cs", csproj, StringComparison.Ordinal);
    }

    // ── ④ 基线文档 ─────────────────────────────────────────────────────

    [Fact]
    public void DesignDoc_RecordsTheFontStackFallbackAsImplemented()
    {
        var section3 = Repo.DesignSection(3);
        Assert.Contains("找到即停", section3, StringComparison.Ordinal);

        var section8 = Repo.DesignSection(8);
        Assert.Contains("字体栈有序回退", section8, StringComparison.Ordinal);
        Assert.Contains("FontStack", section8, StringComparison.Ordinal);
        // 进程默认字体那一行不再是「未改（冻结原则）」的挂账：本轮已收口。
        Assert.DoesNotContain("`Desktop/DesktopApp.cs` 的 `SetDefaultFont(\"Microsoft YaHei UI\", 9pt)` 是过渡壳的既有行为，未改", section8, StringComparison.Ordinal);
    }

    /// <summary>只留代码行（去掉 <c>//</c> 与 <c>///</c> 注释行）：注释里引用旧写法是说明理由，不是第二份实现。</summary>
    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
