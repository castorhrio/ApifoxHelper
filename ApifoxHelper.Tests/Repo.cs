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

    public static string Source(params string[] relative) => File.ReadAllText(Path.Combine([Root, .. relative]));

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
    public bool IsUiTokens => Name == "UiTokens.cs";
    public bool IsUi => Name == "Ui.cs";

    public bool Contains(string pattern) => Text.Contains(pattern, StringComparison.Ordinal);

    public MatchCollection Matches(string pattern) => Regex.Matches(Text, pattern);
}
