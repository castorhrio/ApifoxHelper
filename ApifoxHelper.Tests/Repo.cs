using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 测试基建：定位仓库根目录与源码文件。测试程序集在 ApifoxHelper.Tests/bin 下，逐级上溯找到工程文件即仓库根。
/// </summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string DesignDoc => Path.Combine(Root, "docs", "DESIGN.md");

    public static string DesignDocText { get; } = File.ReadAllText(DesignDoc);

    /// <summary>设计基线中指定章节的文本（"## N." 到下一个 "## " 之前）。</summary>
    public static string DesignSection(int number)
    {
        var lines = DesignDocText.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith($"## {number}.", StringComparison.Ordinal));
        Assert.True(start >= 0, $"DESIGN.md 缺少第 {number} 节。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>Desktop/Native 下的界面源码（令牌接线机械检查的扫描对象）。</summary>
    public static IReadOnlyList<NativeSource> NativeSources() =>
        Directory.GetFiles(Path.Combine(Root, "Desktop", "Native"), "*.cs")
            .Select(f => new NativeSource(f, File.ReadAllText(f)))
            .ToList();

    /// <summary>
    /// 服务层源码目录（用户可见文案的扫描对象）。清单是封闭的：新增服务层目录要在这里登记，
    /// 否则它的文案不会被中文排版检查覆盖——「漏一个目录」正是这条检查要防的事。
    /// </summary>
    public static readonly string[] ServiceDirectories =
        ["Api", "Comments", "Sync", "Apifox", "Profiles", "Tasks", "Source", "Contracts", "Reports", "Infrastructure"];

    /// <summary>服务层源码（相对路径 + 全文）。相对路径进断言消息，两个同名文件不会互相冒充。</summary>
    public static IReadOnlyList<ServiceSource> ServiceSources() =>
        ServiceDirectories
            .SelectMany(dir => Directory.GetFiles(Path.Combine(Root, dir), "*.cs"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new ServiceSource(Path.GetRelativePath(Root, f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();

    public static string Source(params string[] relative) => File.ReadAllText(Path.Combine([Root, .. relative]));

    /// <summary>冒烟器源码（读多次不重开文件：段名单、桶数、读数对账都要用它）。</summary>
    public static string SmokeProgram { get; } = Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>
    /// 冒烟器 <c>Main</c> 里<b>注册</b>过的段名（<c>Section("…", nameof(RunXxx))</c>）。
    /// <para>
    /// 这是「段」的唯一权威名单，两处对账都取它：<c>GateSourceConsistencyTests</c> 判「声明了就要注册」，
    /// <c>ReadingConsistencyTests</c> 判「读数记录里跑出来的段 == 冒烟器注册的段」。放在这里是为了让它们
    /// <b>共用同一条取法</b>——两份各写一个正则，就会有一处改了另一处没跟上，而那时「对得上」
    /// 只是两份口径各自漂移后的巧合。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> SmokeSectionNames() =>
        System.Text.RegularExpressions.Regex.Matches(SmokeProgram, @"Section\(""[^""]*"",\s*nameof\((Run[A-Za-z0-9_]*)\)\)")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApifoxHelper.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "未找到仓库根（ApifoxHelper.csproj）。");
        return dir!.FullName;
    }
}

internal sealed class NativeSource(string path, string text)
{
    public string Name { get; } = Path.GetFileName(path);
    public string Text { get; } = text;

    /// <summary>
    /// 剥掉注释后的代码（第一百九十七轮）。「界面不就地拼写某句文案」这类断言判的是<b>代码</b>：
    /// 注释里点名一颗按钮、引用一句文案，是读者顺着注释找到实物的路，不是第二份字面量。
    /// 去注释口径只有一份（<see cref="CodeScan"/>），这里只做转发——本仓已经因为「各写一份去注释」
    /// 犯过四次（见 <c>CodeScan</c> 类注释），不再开第五处。
    /// </summary>
    public string Code { get; } = CodeScan.Text(text);
    public bool IsUiTokens => Name == "UiTokens.cs";
    public bool IsUi => Name == "Ui.cs";

    public bool Contains(string pattern) => Text.Contains(pattern, StringComparison.Ordinal);

    public MatchCollection Matches(string pattern) => Regex.Matches(Text, pattern);
}

internal sealed class ServiceSource(string relative, string text)
{
    /// <summary>相对仓库根的路径（如 <c>Api/ApifoxRoutes.cs</c>）。</summary>
    public string Relative { get; } = relative;

    public string Text { get; } = text;

    public bool Contains(string pattern) => Text.Contains(pattern, StringComparison.Ordinal);
}
