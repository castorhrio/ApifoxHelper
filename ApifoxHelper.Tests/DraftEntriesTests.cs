using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释草稿其余入口的口径（冻结页面与原生界面同一规则，DraftEntries 单一落点）：
/// 模型生成只在配置启用 OpenAI 兼容模型服务时可用，发送材料前先确认范围（位置数、涉及源码文件、
/// 已排除配置/凭据文件并脱敏、模型不改文件不调用 Apifox 如实说明，超长文件清单截断如实显示）；
/// 导入与导出的结果按数量与拒绝原因如实报告。候选校验本身由 DraftService 单一口径执行。
/// </summary>
public class DraftEntriesTests
{
    [Theory]
    [InlineData("openai", true)]
    [InlineData("none", false)]
    [InlineData("file", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("OpenAI", false)]
    public void CanGenerateModel_OnlyForOpenAiMode(string? mode, bool expected) =>
        Assert.Equal(expected, DraftEntries.CanGenerateModel(mode));

    [Theory]
    [InlineData("t_ab12cd34", "material-t_ab12cd34.json")]
    [InlineData("t1", "material-t1.json")]
    public void MaterialFileName_MatchesWebDownloadName(string taskId, string expected) =>
        Assert.Equal(expected, DraftEntries.MaterialFileName(taskId));

    [Fact]
    public void ModelConfirmText_StatesScopeFilesAndBoundaries()
    {
        var text = DraftEntries.ModelConfirmText(7, ["src/A.cs", "src/B.cs"]);
        Assert.Contains("7 个注释位置", text);
        Assert.Contains("src/A.cs", text);
        Assert.Contains("src/B.cs", text);
        Assert.Contains("已排除配置/凭据文件并脱敏", text);
        Assert.Contains("不会修改文件或调用 Apifox", text);
        Assert.Contains("逐条审阅", text);
        Assert.DoesNotContain("（其余", text);
    }

    [Fact]
    public void ModelConfirmText_NoFiles_SaysSoExplicitly()
    {
        var text = DraftEntries.ModelConfirmText(3, []);
        Assert.Contains("（无证据片段）", text);
        Assert.Contains("3 个注释位置", text);
    }

    [Fact]
    public void ModelConfirmText_LongFileList_TruncatesHonestly()
    {
        var files = Enumerable.Range(1, 25).Select(i => $"src/F{i}.cs").ToList();
        var text = DraftEntries.ModelConfirmText(30, files);
        Assert.Contains("src/F20.cs", text);
        Assert.DoesNotContain("src/F21.cs", text);
        Assert.Contains("（其余 5 个文件略）", text);
    }

    [Fact]
    public void MaterialExportDoneText_PointsBackToImportEntry()
    {
        var text = DraftEntries.MaterialExportDoneText(12);
        Assert.Contains("12 个注释位置", text);
        Assert.Contains("导入编码助手结果", text);
    }

    [Fact]
    public void ImportDoneText_ReportsCountsAndFirstErrors()
    {
        var text = DraftEntries.ImportDoneText(4, 2, ["slotId s9 不存在于本次扫描。", "P1：说明为空"]);
        Assert.Contains("导入 4 条候选", text);
        Assert.Contains("拒绝 2 条", text);
        Assert.Contains("slotId s9 不存在于本次扫描。", text);
        Assert.Contains("说明为空", text);
    }

    [Fact]
    public void ImportDoneText_MoreThanThreeErrors_ShowsOnlyThree()
    {
        var errors = Enumerable.Range(1, 5).Select(i => $"错误{i}").ToList();
        var text = DraftEntries.ImportDoneText(0, 5, errors);
        Assert.Contains("错误3", text);
        Assert.DoesNotContain("错误4", text);
    }

    [Fact]
    public void ImportDoneText_NoErrors_OmitsErrorPart()
    {
        var text = DraftEntries.ImportDoneText(6, 0, []);
        Assert.Contains("导入 6 条候选", text);
        Assert.DoesNotContain("：", text.Replace("导入 6 条候选，拒绝 0 条", ""));
    }

    [Fact]
    public void ManualAddedText_PointsToReview()
    {
        Assert.Contains("查看注释候选", DraftEntries.ManualAddedText);
        Assert.Contains("审阅", DraftEntries.ManualAddedText);
    }

    [Fact]
    public void UserVisibleTexts_FreeOfBannedWords()
    {
        var texts = new List<string>
        {
            DraftEntries.ModelConfirmText(2, ["src/A.cs"]),
            DraftEntries.ModelConfirmText(2, []),
            DraftEntries.MaterialExportDoneText(2),
            DraftEntries.ImportDoneText(1, 1, ["原因"]),
            DraftEntries.ImportDoneText(1, 0, []),
            DraftEntries.ManualAddedText,
        };
        foreach (var text in texts)
        {
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }
}
