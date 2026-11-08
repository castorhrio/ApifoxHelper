using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「模型绑定下拉不把已保存的绑定显示成未确认」断言（DESIGN.md §6「下拉框」、§8 本轮条款）。
/// 缺口：已保存的绑定若不在本次检查的候选里（远端模型被删或改名后重新检查、候选集变化），
/// 旧实现 FindIndex 落空后一律 SelectedIndex = 0——下拉显示「未确认」，同一屏的「状态」列却显示
/// 「绑定远端 42」，两处对同一件事说两种话；保存按钮按「选中值 ≠ 已保存值」启用，于是
/// 「未确认」≠「42」使按钮亮着，一次点击静默删掉用户没打算改的绑定。
/// 同壳三个配置对话框的同类下拉（接口状态、生成方式、JSON 命名）早就按「未知取值按原值追加一项显示，
/// 不静默改值」处理，这一处是第四个同类下拉、也是唯一会静默改值的。
/// 口径收在 <see cref="BindingChoices"/>（不依赖 WinForms、与测试工程共享编译，下列真值表真执行）。
/// 真机上下拉的实际显示随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class BindingChoicesTests
{
    // ---------- 选项清单 ----------

    [Fact]
    public void NoSavedBinding_StartsAtUnconfirmed()
    {
        var options = BindingChoices.Build([12, 34], null);
        Assert.Equal(["", "12", "34", "new"], options.Choices.Select(c => c.Value));
        Assert.Equal(0, options.SelectedIndex);
        Assert.Equal("未确认", options.Choices[0].Text);
    }

    [Theory]
    [InlineData("12", 1)]
    [InlineData("34", 2)]
    [InlineData("new", 3)]
    public void SavedBindingInCandidates_IsSelected(string saved, int expected)
    {
        var options = BindingChoices.Build([12, 34], saved);
        Assert.Equal(expected, options.SelectedIndex);
        Assert.Equal(saved, options.Choices[options.SelectedIndex].Value);
    }

    [Fact]
    public void SavedBindingOutsideCandidates_IsAppendedAndSelected()
    {
        // 远端模型被删或改名后重新检查：候选里没有 42，但 decisions.json 里存着 42。
        var options = BindingChoices.Build([12, 34], "42");
        var last = options.Choices[^1];
        Assert.Equal("42", last.Value);
        Assert.Equal(options.Choices.Count - 1, options.SelectedIndex);
        // 不显示成「未确认」——那正是本轮要修的谎。
        Assert.NotEqual("未确认", last.Text);
        Assert.Equal("绑定远端 42" + BindingChoices.NotCandidateSuffix, last.Text);
    }

    [Fact]
    public void EmptyCandidates_StillOffersUnconfirmedAndNewModel()
    {
        var options = BindingChoices.Build([], "42");
        Assert.Equal(["", "new", "42"], options.Choices.Select(c => c.Value));
        Assert.Equal(2, options.SelectedIndex);
    }

    [Fact]
    public void DuplicateCandidates_AppearOnce()
    {
        var options = BindingChoices.Build([12, 12, 34], null);
        Assert.Equal(["", "12", "34", "new"], options.Choices.Select(c => c.Value));
    }

    [Fact]
    public void SelectedIndex_IsAlwaysInsideTheList()
    {
        // 构造保证选中项落在清单之内，调用方不再自己兜底（旧实现的 `index < 0 ? 0` 就是那个兜底）。
        long[][] candidateSets = [[], [7], [7, 8], [7, 8, 9, 10]];
        string?[] savedValues = [null, "", "7", "8", "9", "10", "42", "new"];
        foreach (var candidates in candidateSets)
        {
            foreach (var saved in savedValues)
            {
                var options = BindingChoices.Build(candidates, saved);
                Assert.InRange(options.SelectedIndex, 0, options.Choices.Count - 1);
                Assert.Equal(saved ?? "", options.Choices[options.SelectedIndex].Value);
            }
        }
    }

    [Fact]
    public void Labels_ComeFromTheSamePlaceAsTheStatusColumn()
    {
        // 下拉与「状态」列不能对同一件事说两种话：显示文字一律取 TaskDisplay.ModelBindingText。
        var options = BindingChoices.Build([12], "42");
        foreach (var choice in options.Choices)
        {
            var expected = TaskDisplay.ModelBindingText(choice.Value);
            if (choice.Value == "42") expected += BindingChoices.NotCandidateSuffix;
            Assert.Equal(expected, choice.Text);
        }
    }

    // ---------- 保存按钮 ----------

    [Theory]
    [InlineData("12", "34", true)]
    [InlineData("12", "12", false)]
    [InlineData("", "12", true)]
    [InlineData("new", "", true)]
    [InlineData("", "", false)]
    [InlineData("new", "new", false)]
    public void CanSave_OnlyWhenSelectionDiffersFromSaved(string selected, string saved, bool expected)
    {
        Assert.Equal(expected, BindingChoices.CanSave(selected, saved));
    }

    [Fact]
    public void CanSave_NoSelection_IsNotSavable()
    {
        // 下拉被清空、没有选中：不猜「没有选中」等于「未确认」。
        Assert.False(BindingChoices.CanSave(null, "42"));
        Assert.False(BindingChoices.CanSave(null, ""));
    }

    [Fact]
    public void CanSave_SavedBindingOutsideCandidates_StaysDisabled()
    {
        // 本轮的核心回归：下拉选中项就是已保存的 42，保存按钮因此不可用——
        // 一次点击不会把用户没打算改的绑定静默删掉。
        var options = BindingChoices.Build([12, 34], "42");
        var selected = options.Choices[options.SelectedIndex].Value;
        Assert.False(BindingChoices.CanSave(selected, "42"));
    }

    // ---------- 口径文件本身 ----------

    [Fact]
    public void Caliber_DoesNotReferenceWinForms()
    {
        var source = Repo.Source("Desktop", "Native", "BindingChoices.cs");
        Assert.DoesNotContain("System.Windows.Forms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Caliber_IsSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\BindingChoices.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 接线 ----------

    [Fact]
    public void Dialog_BuildsOptionsThroughTheSharedCaliber()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        Assert.Matches(@"(?m)^\s*var options = BindingChoices\.Build\(entry\.Model\?\.Candidates \?\? \[\], SavedBinding\(entry\)\);$", source);
        Assert.Matches(@"(?m)^\s*_bindingCombo\.SelectedIndex = options\.SelectedIndex;$", source);
        Assert.Matches(@"(?m)^\s*_bindingSaveButton\.Enabled = BindingChoices\.CanSave\(SelectedBindingValue\(\), SavedBinding\(entry\)\);$", source);
        Assert.Matches(@"(?m)^\s*private string\? SelectedBindingValue\(\) => _bindingCombo\.SelectedItem is BindingChoice choice \? choice\.Value : null;$", source);
    }

    [Fact]
    public void Dialog_NoLongerFallsBackToUnconfirmed()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        // 旧形状：FindIndex 落空后一律选中第 0 项（「未确认」），以及自己那份 BindingOption 类型。
        Assert.DoesNotContain("FindIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingOption", source, StringComparison.Ordinal);
        Assert.DoesNotContain("index < 0 ? 0", source, StringComparison.Ordinal);
        // 选项不在对话框里拼：清单只由共享口径给出。
        Assert.DoesNotContain("_bindingCombo.Items.Add(new", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheDropdownClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("下拉框", section, StringComparison.Ordinal);
        Assert.Contains("不静默改值", section, StringComparison.Ordinal);
        Assert.Contains("BindingChoices", section, StringComparison.Ordinal);
        Assert.Contains("不在本次候选", section, StringComparison.Ordinal);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("| 模型绑定下拉（§6） |", ledger, StringComparison.Ordinal);
        Assert.Contains("第七十四轮", ledger, StringComparison.Ordinal);
    }
}
