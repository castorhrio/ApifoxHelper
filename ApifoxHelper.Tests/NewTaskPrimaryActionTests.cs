using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「新建任务」对话框里那两颗创建按钮（「创建并扫描」/「仅创建」）此刻该不该可点（第四百一十五轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这个对话框此前那两颗按钮<b>永远可点</b>——一份配置都没有时点下去，
/// <c>Confirm</c> 读到 <c>_profile.SelectedItem</c> 不是 <c>ProfileItem</c>，往状态行写一句
/// 「请选择项目配置。」就返回了。屏上是一个空下拉、一个空模块清单、一句「先选一份配置」，
/// 而两颗创建按钮照样亮着——<b>它们指向一个此刻不可能成功的动作</b>。
/// 这与第四十六轮「文字不指向不存在的按钮」、第五十轮「提示所指引的恢复路径不可达」是同一族。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 判定 <c>NewTaskPrimaryAction.CanCreate</c> 两个方向（选中了配置 / 没选中）；
/// ② 对话框读的是这一处判定，不自己写 <c>Enabled = _profile.Items.Count &gt; 0</c>；
/// ③ 两颗按钮都是字段（不是构造里的局部变量）——局部变量在 <c>LoadModules</c> 里取不到，启停就落不了地；
/// ④ 启停与说明句<b>同一处</b>（<c>LoadModules</c> 是唯一赋值点），下拉一换两件事一起走；
/// ⑤ 构造期一份配置都没有时也推导一次（否则两颗按钮停在出厂那一态上）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上这两颗按钮停用之后键盘落在哪（随 G-D5 真机复核）；
/// 也不判「取消」那颗（它永远可点，这一轮不动它）。
/// </para>
/// </summary>
public class NewTaskPrimaryActionTests
{
    private static string TasksPageSource => Repo.Source("Desktop", "Native", "TasksPage.cs");

    /// <summary>
    /// ① 两个方向：选中了一份配置时可点；没选中时停用。
    /// <para>
    /// 这一条是这一族的全部意义：没选中时点下去只会得到一句「请选择项目配置」，
    /// 而屏上根本没有可选的配置——那颗按钮该停用，而不是让人点一次再被拒。
    /// </para>
    /// </summary>
    [Fact]
    public void CanCreate_OnlyWhenAProfileIsSelected()
    {
        Assert.True(NewTaskPrimaryAction.CanCreate(true));
        Assert.False(NewTaskPrimaryAction.CanCreate(false));
    }

    /// <summary>
    /// ② 对话框读的是这一处判定：不自己写 <c>Enabled = _profile.Items.Count &gt; 0</c>，
    /// 也不自己判「清单空不空」——判定收在 <c>NewTaskPrimaryAction</c> 一处。
    /// </summary>
    [Fact]
    public void TheDialog_ReadsTheJudgementInsteadOfWritingItsOwnGate()
    {
        // 判定收两个入参：选没选中配置（与说明句同一件事）。
        Assert.Contains("var canCreate = NewTaskPrimaryAction.CanCreate(selected is not null);",
            TasksPageSource, StringComparison.Ordinal);
        // 两颗按钮各读一次同一个判定：不各写一份。
        Assert.Contains("_scanButton.Enabled = canCreate;", TasksPageSource, StringComparison.Ordinal);
        Assert.Contains("_createButton.Enabled = canCreate;", TasksPageSource, StringComparison.Ordinal);
        // 判定不在这里再写一遍：对话框里不出现「清单空不空」这类第二份判据。
        Assert.DoesNotContain("_profile.Items.Count > 0", TasksPageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_profile.Items.Count == 0", TasksPageSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// ③ 两颗按钮都是字段：构造里的局部变量在 <c>LoadModules</c> 里取不到，启停就落不了地
    /// （第四百一十二轮说明句那一格正是这样：出厂一次、之后换不了）。
    /// </summary>
    [Fact]
    public void BothButtons_AreFieldsSoTheGateCanReachThem()
    {
        Assert.Contains("private readonly Button _scanButton;", TasksPageSource, StringComparison.Ordinal);
        Assert.Contains("private readonly Button _createButton;", TasksPageSource, StringComparison.Ordinal);
        // 构造里把两颗按钮交给字段（不是只留局部变量）。
        Assert.Contains("_scanButton = scanButton;", TasksPageSource, StringComparison.Ordinal);
        Assert.Contains("_createButton = createButton;", TasksPageSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ 启停与说明句<b>同一处</b>：<c>LoadModules</c> 是 <c>_scanButton.Enabled</c> 的唯一赋值点，
    /// 下拉一换两件事一起走——只在出厂时定一次的话，换到另一份配置之后按钮说的还是上一份的情况。
    /// </summary>
    [Fact]
    public void TheGateFollowsTheProfileSelection_InTheSamePlaceAsTheHint()
    {
        var assignments = Regex.Matches(TasksPageSource, @"_scanButton\.Enabled\s*=").Count;
        Assert.Equal(1, assignments);

        var body = MethodBody(TasksPageSource, "private void LoadModules()");
        Assert.Contains("_scanButton.Enabled = canCreate;", body, StringComparison.Ordinal);
        Assert.Contains("_createButton.Enabled = canCreate;", body, StringComparison.Ordinal);
        // 与说明句同一处：两件事分开写，换配置之后就会各说一套。
        Assert.Contains("_moduleHint.Text = NewTaskModuleHint.For(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑤ 构造期一份配置都没有时也推导一次：<c>else LoadModules();</c> 那一支让两颗按钮停用，
    /// 否则它们停在出厂那一态上（可点），而屏上连一份配置都没有。
    /// </summary>
    [Fact]
    public void TheConstructor_DerivesTheGateWhenThereAreNoProfiles()
    {
        var constructor = MethodBody(TasksPageSource, "public NewTaskDialog(ProfileStore profiles, string? preferredProfileId = null)");
        Assert.Contains("else LoadModules();", constructor, StringComparison.Ordinal);
        // 字段在构造里先赋值、再走到 LoadModules：否则第一次推导会撞上一个还没赋值的按钮。
        var scanAssign = constructor.IndexOf("_scanButton = scanButton;", StringComparison.Ordinal);
        var loadCall = constructor.IndexOf("else LoadModules();", StringComparison.Ordinal);
        Assert.True(scanAssign >= 0, "构造里找不到「把创建按钮交给字段」那一处。");
        Assert.True(loadCall >= 0, "构造里找不到「一份配置都没有时推导一次」那一处。");
        Assert.True(scanAssign < loadCall,
            "创建按钮的字段赋值排在 LoadModules 之后：第一次推导会撞上一个还没赋值的按钮（构造期空引用）。");
    }

    /// <summary>按「下一个顶格 <c>private</c> 声明」切方法体（与 <c>NewTaskModuleHintTests</c> 同一取法）。</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"源码里找不到 {signature}。");
        var rest = source[(start + signature.Length)..];
        var end = Regex.Match(rest, @"(?m)^    private ").Index;
        return end > 0 ? rest[..end] : rest;
    }
}
