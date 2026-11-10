using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// wine 试跑脚本的可移植性（第一百四十二轮，简报 P1）。
/// <para>
/// 为什么钉这一条：<c>.winecheck/</c> 下的脚本是入库的开发期辅助（不是产品运行路径），
/// 但它们此前写死了个人环境的绝对路径（某个用户主目录下的工作区、发行版打包的 wine 路径）——
/// 换一台机器就整段失效，而失效方式是「脚本跑起来、什么都没发生」，看不出是路径问题。
/// 现在全部改成可覆盖的环境变量，本文件钉住「不再写死个人路径」与「使用说明在位」。
/// </para>
/// </summary>
public class WineCheckScriptsTests
{
    private static string Directory => Path.Combine(Repo.Root, ".winecheck");

    private static IReadOnlyList<(string Name, string Text)> Scripts() =>
        System.IO.Directory.GetFiles(Directory, "*.sh")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

    /// <summary>脚本里不得出现个人环境的绝对路径（主目录、发行版打包路径、仓库外的固定目录）。</summary>
    [Fact]
    public void Scripts_HaveNoPersonalAbsolutePaths()
    {
        var forbidden = new[] { "/home/", "/Users/", "/usr/lib/", "/opt/" };
        var scripts = Scripts();
        Assert.NotEmpty(scripts);
        foreach (var (name, text) in scripts)
        {
            foreach (var path in forbidden)
            {
                Assert.False(
                    text.Contains(path, StringComparison.Ordinal),
                    $"{name} 里写死了个人环境绝对路径「{path}」——改成可覆盖的环境变量。");
            }
        }
    }

    /// <summary>每个脚本都从环境变量取路径，且缺变量时立刻失败（`set -u`），不静默跑空。</summary>
    [Fact]
    public void Scripts_TakeTheirPathsFromEnvironmentVariables()
    {
        foreach (var (name, text) in Scripts())
        {
            Assert.Contains("set -u", text, StringComparison.Ordinal);
            Assert.Contains("WINEPREFIX=${WINEPREFIX:-", text, StringComparison.Ordinal);
            Assert.Contains("DISPLAY=${DISPLAY:-", text, StringComparison.Ordinal);
        }
    }

    /// <summary>使用说明在位：wine 版本、Xvfb 依赖、产物位置三件事都写清楚了。</summary>
    [Fact]
    public void Readme_DocumentsDependenciesAndArtifacts()
    {
        var readme = Path.Combine(Directory, "README.md");
        Assert.True(File.Exists(readme), ".winecheck/README.md 缺失（脚本没有使用说明）。");
        var text = File.ReadAllText(readme);
        Assert.Contains("wine-10.0", text, StringComparison.Ordinal);
        Assert.Contains("Xvfb", text, StringComparison.Ordinal);
        Assert.Contains("产物位置", text, StringComparison.Ordinal);
        Assert.Contains("/tmp/ah-smoke-report.txt", text, StringComparison.Ordinal);
        // 说明里要讲清「这不是 Windows 真机证据」，否则截图会被当成观感复核。
        Assert.Contains("不能替代真机复核", text, StringComparison.Ordinal);
    }
}
