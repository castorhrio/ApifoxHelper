using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 证据措辞的诚实性（第一百五十三轮，简报 R6：wine ≠ 真机）。
/// <para>
/// <b>为什么要有这个文件</b>：本仓至今**没有任何 Windows 真机证据**——全部 GUI 证据来自
/// Linux 容器里的 wine 10.0 + Xvfb。但此前 <c>docs/NOTES.md</c> 有一处把 wine 量到的列宽
/// 写成「真机实测」，冒烟器与冒烟用例的类摘要也把 wine 结果写成「真实执行证据」。
/// 这类措辞不会让代码出错，却会让答辩口径失真：读者以为真机已经跑过，于是 G-D5 那道门槛
/// 被一句话悄悄跨过去了。本文件把「wine 证据只能叫 wine 证据」钉成机械断言。
/// </para>
/// <para>
/// 真机到位后怎么办：把真机证据写进 NOTES.md 的「Windows 真机 / wine 模拟器验证清单」B 段，
/// 并**连同本文件一起改**——那时「真机实测」才有出处，这条断言就该换成「真机证据必须带截图路径」。
/// </para>
/// </summary>
public class WordingHonestyTests
{
    [Fact]
    public void Notes_DoNotCallWineResultsRealMachineMeasurements()
    {
        // 本仓没有 Windows 真机证据：任何「真机实测」都是把 wine 结果说成真机。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.DoesNotContain("真机实测", notes);
    }

    [Fact]
    public void SmokeProgram_CallsItsEvidenceWineEvidence()
    {
        var program = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("wine 模拟器跑过一遍", program);
        Assert.DoesNotContain("真机/模拟器跑过一遍", program);
    }

    [Fact]
    public void SmokeTests_CallTheirEvidenceWineEvidence()
    {
        var tests = Repo.Source("ApifoxHelper.Tests", "NativeDialogSmokeTests.cs");
        Assert.Contains("wine 模拟器执行证据", tests);
        Assert.DoesNotContain("真实执行证据", tests);
    }

    [Fact]
    public void Checklist_SeparatesRealMachineFromWine()
    {
        // 清单标题必须把两者分开写：一个笼统的「真机/模拟器」标题读起来像「真机也跑过了」。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("## Windows 真机 / wine 模拟器验证清单", notes);
        Assert.DoesNotContain("## 真机/模拟器验证清单", notes);
    }
}
