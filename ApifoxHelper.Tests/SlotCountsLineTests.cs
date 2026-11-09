using System.Reflection;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Source;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释缺口清单的计数行口径（第一百三十六轮）。
/// <para>
/// 存在理由：计数行此前是 <c>ScanResultsDialog.UpdateCounts</c> 里的私有拼字，而冒烟器
/// （<c>ApifoxHelper.Smoke</c>）要在真实控件上断言「计数行与扫描产物对得上」——两边各拼一份的话，
/// 断言比的是第二份文字、不是屏幕上那一份（正文改了而冒烟器没跟上，冒烟照样 PASS）。
/// 现文字收进不依赖 WinForms、与测试工程共享编译的 <see cref="SlotText.CountsLine"/>，
/// 对话框与冒烟器取同一份。
/// </para>
/// <para>
/// 本文件钉两件事：① 计数口径本身（空清单、逐状态计数、未知状态计入「其他」而不是丢掉）；
/// ② 接线（对话框不再就地统计、冒烟器断言取同一份口径）。
/// </para>
/// </summary>
public class SlotCountsLineTests
{
    private static DocSlot Slot(string status) => new()
    {
        Id = "s-" + status,
        SymbolId = "M:Probe.Api.Controllers.ProbeController.Get",
        SymbolKind = "method",
        Display = "GET /api/probe",
        Tag = "summary",
        Status = status,
        File = "Probe.Api/Controllers/ProbeController.cs",
        Line = 12,
        Role = "endpoint",
        Editable = true,
    };

    [Fact]
    public void CountsLine_EmptyList_SaysThereAreNoSlots()
    {
        // 空清单不是「位置 0 · 有效 0 …」：那一屏的下一步是去扫描，不是读一串零。
        Assert.Equal("没有注释位置", SlotText.CountsLine([]));
    }

    [Fact]
    public void CountsLine_CountsEachStatus()
    {
        // 逐状态计数与顺序逐字钉住：这一行是用户判断「还剩多少要补」的唯一数字来源。
        var slots = new List<DocSlot>
        {
            Slot("valid"), Slot("valid"), Slot("missing"), Slot("empty"), Slot("stale"),
        };
        Assert.Equal("位置 5 · 有效 2 · 缺失 1 · 空 1 · 失效 1 · 其他 0", SlotText.CountsLine(slots));
    }

    [Fact]
    public void CountsLine_UnknownStatus_CountsAsOther()
    {
        // 扫描新增状态值（inheritDoc / invalidXml / unsupported …）时不能悄悄少算一条：
        // 总数与各档之和必须相等，否则「位置 42」与后面几个数字自相矛盾。
        var slots = new List<DocSlot>
        {
            Slot("valid"), Slot("inheritDoc"), Slot("invalidXml"), Slot("unsupported"), Slot(""),
        };
        var line = SlotText.CountsLine(slots);
        Assert.Equal("位置 5 · 有效 1 · 缺失 0 · 空 0 · 失效 0 · 其他 4", line);
    }

    [Fact]
    public void CountsLine_TotalEqualsTheSumOfTheBuckets()
    {
        // 不变量：总数 = 各档之和（任何状态值都不许被丢掉）。
        var slots = new List<DocSlot>
        {
            Slot("valid"), Slot("missing"), Slot("empty"), Slot("stale"), Slot("inheritDoc"), Slot("whatever"),
        };
        var line = SlotText.CountsLine(slots);
        var numbers = System.Text.RegularExpressions.Regex.Matches(line, @"\d+")
            .Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(6, numbers.Count);
        Assert.Equal(numbers[0], numbers.Skip(1).Sum());
    }

    [Fact]
    public void CountsLine_TakesOnlyTheSlots()
    {
        // 计数行讲的是整份扫描：口径里不得出现筛选或排序参数——一旦带上，筛到零行时
        // 那几个数字就会跟着变，而它们正是「整份还有多少」的唯一来源（ListFilter.IsFilteredEmpty 同一取舍）。
        var method = typeof(SlotText).GetMethod(nameof(SlotText.CountsLine), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(IReadOnlyList<DocSlot>), parameters[0].ParameterType);
    }

    [Fact]
    public void Dialog_TakesTheCountsLineFromTheSharedSource()
    {
        // 接线：对话框不再就地统计（那一份私有拼字正是「两处各拼一份」的来源）。
        var dialog = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        Assert.Contains("_counts.Text = SlotText.CountsLine(_slots);", dialog);
        Assert.DoesNotContain("case \"valid\": valid++;", dialog);
        Assert.DoesNotContain("· 有效 {valid}", dialog);
    }

    [Fact]
    public void SmokeProgram_AssertsTheCountsLineAgainstTheSameSource()
    {
        // 冒烟器在真实控件上断言计数行：取的是同一份口径，不是第二份拼字。
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("SlotText.CountsLine(output.Slots)", smoke);
        Assert.Contains("CountsLabel(dialog).Text", smoke);
    }

    [Fact]
    public void DesignDoc_RecordsTheCountsLineSource()
    {
        // 设计基线 §8 记着这条口径（改了实现不更新基线即红）。
        var section = Repo.DesignSection(8);
        Assert.Contains("计数行", section);
        Assert.Contains("SlotText.CountsLine", section);
    }
}
