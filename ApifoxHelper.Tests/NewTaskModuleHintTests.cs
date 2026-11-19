using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「新建任务」对话框里「模块」那一行下面的说明句（第四百一十二轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这一句此前是一句写死的字面——「勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。」
/// 而它下面那个清单是按<b>选中那一份配置的模块清单</b>填的。配置里一个模块都没配时清单是空的，
/// 说明句却仍在教人「勾选要检查的模块」——<b>它指着一个屏上不存在的东西</b>：用户看到的是一个空框
/// 加一句「勾选它」，既不知道「为什么没有可勾的」，也不知道「那这一次会检查什么」。
/// 这与第四十六轮「文字不指向不存在的按钮」、第五十轮「提示所指引的恢复路径不可达」是同一族。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 判定 <c>NewTaskModuleHint.For</c> 三个方向（0 / 正数 / 认不出）各答哪一句；
/// ② 两句都说出「那会怎样」（检查该 API 项目的全部接口），而「没有模块」那一句<b>不再教人去勾</b>；
/// ③ 有模块那一句<b>逐字</b>是改动之前那一句（这一轮改的是「什么时候说哪一句」，不是话本身）；
/// ④ 两句都是单句、长度在说明行的上限之内（<c>CaptionBrevityTests</c> 那一族的口径）；
/// ⑤ 对话框读的是这一处判定，不自己拼字面、也不自己判「清单空不空」；
/// ⑥ 说明句与清单<b>同一轮推导</b>（<c>LoadModules</c> 是唯一赋值点），且下拉的订阅在说明行出厂之后
/// （否则第一次选中事件会撞上一个还没赋值的标签）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上这一句在 200% 档折不折行（随 G-D5 真机复核）；
/// 也不判「模块清单该按什么排」（那是配置里 <c>Modules</c> 的顺序，这一轮不动它）。
/// </para>
/// </summary>
public class NewTaskModuleHintTests
{
    private static string TasksPageSource => Repo.Source("Desktop", "Native", "TasksPage.cs");

    /// <summary>说明行那一句的长度上限：与 <c>CaptionBrevityTests</c> 同一口径（一句说明不写成段落）。</summary>
    private const int MaxHintLength = 40;

    /// <summary>
    /// ① 三个方向：清单空（且确实选中了一份配置）说「没有模块」那一句；有模块说原来那一句；
    /// 认不出（一份配置都没选中）按「有模块」算——不把「没有对象」说成「对象是空的」。
    /// </summary>
    [Fact]
    public void For_AnswersTheSentenceThatMatchesTheList()
    {
        Assert.Equal(NewTaskModuleHint.NoModules, NewTaskModuleHint.For(0));
        Assert.Equal(NewTaskModuleHint.WithModules, NewTaskModuleHint.For(1));
        Assert.Equal(NewTaskModuleHint.WithModules, NewTaskModuleHint.For(3));
        // 认不出：一份配置都没选中时屏上没有「这份配置」，说成「没有模块」是把没有对象说成对象是空的。
        Assert.Equal(NewTaskModuleHint.WithModules, NewTaskModuleHint.For(null));
    }

    /// <summary>
    /// ② 两句都说出「那会怎样」，而「没有模块」那一句不再教人去勾一个不存在的东西。
    /// <para>
    /// 这一条是这一轮的全部意义：空清单 + 「勾选要检查的模块」= 指着一个屏上不存在的对象。
    /// </para>
    /// </summary>
    [Fact]
    public void NoModules_SaysWhyTheBoxIsEmptyAndWhatHappensInstead()
    {
        Assert.NotEqual(NewTaskModuleHint.WithModules, NewTaskModuleHint.NoModules);
        // 两句都答「那这一次检查什么」——用户真正要知道的那件事。
        Assert.Contains("该 API 项目的全部接口", NewTaskModuleHint.WithModules, StringComparison.Ordinal);
        Assert.Contains("该 API 项目的全部接口", NewTaskModuleHint.NoModules, StringComparison.Ordinal);
        // 空清单那一句不再出现「勾选」：框里一个可勾的都没有，教人勾就是指向不存在的对象。
        Assert.DoesNotContain("勾选", NewTaskModuleHint.NoModules, StringComparison.Ordinal);
        // 它先说「没有」：用户看到的空框因此有了解释。
        Assert.Contains("没有模块", NewTaskModuleHint.NoModules, StringComparison.Ordinal);
    }

    /// <summary>
    /// ③ 有模块那一句逐字是改动之前那一句：这一轮改的是「什么时候说哪一句」，不是话本身
    /// （与 <c>HintRowTests.TheHintTexts_AreWordForWordUnchanged</c> 同一纪律）。
    /// </summary>
    [Fact]
    public void WithModules_IsWordForWordTheSentenceThatWasThereBefore()
    {
        Assert.Equal("勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。", NewTaskModuleHint.WithModules);
    }

    /// <summary>
    /// ④ 两句都是单句、长度在说明行上限之内：说明行是注脚，不是段落（<c>CaptionBrevityTests</c> 同一口径）。
    /// </summary>
    [Fact]
    public void BothSentences_AreOneShortSentence()
    {
        foreach (var text in new[] { NewTaskModuleHint.WithModules, NewTaskModuleHint.NoModules })
        {
            Assert.EndsWith("。", text, StringComparison.Ordinal);
            // 句号只出现在末尾：中间再断一句，这一行就成了两句话的段落。
            Assert.Equal(1, text.Count(c => c == '。'));
            Assert.True(text.Length <= MaxHintLength, $"说明句 {text.Length} 字，超过说明行上限 {MaxHintLength} 字：{text}");
            // 全角标点（中文排版口径）：这一族不出现半角逗号与句号。
            Assert.DoesNotContain(",", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ⑤ 对话框读的是这一处判定：说明行取 <c>NewTaskModuleHint.WithModules</c> 出厂，
    /// 之后由 <c>NewTaskModuleHint.For</c> 换——不自己拼字面、也不自己判「清单空不空」。
    /// </summary>
    [Fact]
    public void TheDialog_ReadsTheJudgementInsteadOfWritingItsOwnSentence()
    {
        // 出厂那一句仍由 Ui.HintRow 那一行（裸语句）写下，且与 WithModules 逐字相同——
        // 两处各写一份字面，靠这一条判住它们不许漂开。
        Assert.Contains("Ui.HintRow(grid, \"勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。\", HintRowForm.Field);",
            TasksPageSource, StringComparison.Ordinal);
        // 那一格按工厂标记 + 出厂字面取回（同一屏另有四行说明，标记相同、文字不同）。
        Assert.Contains("_moduleHint = ModuleHintLabel(grid);", TasksPageSource, StringComparison.Ordinal);
        Assert.Contains("_moduleHint.Text = NewTaskModuleHint.For(selected is null ? null : _modules.Items.Count);",
            TasksPageSource, StringComparison.Ordinal);
        // 取不到那一格时不重写、也不抛：屏上留着出厂那一句（不变差）。
        Assert.Contains("if (_moduleHint is not null)", TasksPageSource, StringComparison.Ordinal);
        // 说明句不再只在出厂时定一次：换配置之后必须由 LoadModules 重写（否则它说的还是上一份的情况）。
        Assert.Contains("_moduleHint.Text = NewTaskModuleHint.For(", TasksPageSource, StringComparison.Ordinal);
        // 判定不在这里再写一遍：对话框里不出现「模块数 == 0」这类第二份判据。
        Assert.DoesNotContain("Modules.Count == 0", TasksPageSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑥ 说明句与清单<b>同一轮推导</b>：<c>LoadModules</c> 是 <c>_moduleHint.Text</c> 的唯一赋值点，
    /// 且下拉的订阅在说明行出厂<b>之后</b>——订阅早于赋值的话，第一次选中事件会撞上一个还没赋值的标签。
    /// </summary>
    [Fact]
    public void TheSentenceFollowsTheProfileSelection_AndTheSubscriptionComesAfterTheHintRow()
    {
        var assignments = Regex.Matches(TasksPageSource, @"_moduleHint\.Text\s*=").Count;
        Assert.Equal(1, assignments);

        var body = MethodBody(TasksPageSource, "private void LoadModules()");
        Assert.Contains("_moduleHint.Text = NewTaskModuleHint.For(", body, StringComparison.Ordinal);
        // 清单重填与说明句在同一处：两件事分开写，换配置之后就会各说一套。
        Assert.Contains("_modules.Items.Clear();", body, StringComparison.Ordinal);

        var hintRow = TasksPageSource.IndexOf("_moduleHint = ModuleHintLabel(grid);", StringComparison.Ordinal);
        var subscription = TasksPageSource.IndexOf("_profile.SelectedIndexChanged += (_, _) => LoadModules();", StringComparison.Ordinal);
        Assert.True(hintRow >= 0, "对话框里找不到说明行的出厂那一句。");
        Assert.True(subscription >= 0, "对话框里找不到「下拉一换就重填模块清单」那一处订阅。");
        Assert.True(hintRow < subscription,
            "下拉的订阅排在说明行出厂之前：第一次选中事件会撞上一个还没赋值的 _moduleHint（构造期空引用）。");
    }

    /// <summary>按「下一个顶格 <c>private</c> 声明」切方法体（与 <c>HintRowTests</c> 同一取法）。</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"源码里找不到 {signature}。");
        var rest = source[(start + signature.Length)..];
        var end = Regex.Match(rest, @"(?m)^    private ").Index;
        return end > 0 ? rest[..end] : rest;
    }
}
