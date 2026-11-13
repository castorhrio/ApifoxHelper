using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 三个配置子对话框的字段行只经 <c>Ui.FieldRow</c> 一处出厂（docs/DESIGN.md §6「字段行」；
/// 出处：<c>docs/CRITIQUE.md</c> 第 1 站「最该改的一处」里剩下的那一半，具名 pass：distill）。
/// <para>
/// <b>补的是哪个缺口</b>：「模块与模板 / 序列化与分析 / 模型服务」各带一份<b>逐字相同</b>的私有
/// <c>AddRow</c>——与第二百零六轮收掉的那三份私有 <c>AddHint</c> 是同一个形状（同一件事三种写法，
/// 「没有一处是选过的」那种脚手架感）。收成一处之后，这一族的行内留白只有这一个落点可改。
/// </para>
/// <para>
/// <b>顺带并进来的那一支守卫，以及它抓到的两处</b>：配置页与新建任务页那两份 <c>AddRow</c> 都有
/// 「行容器自己排布行内留白，外层不再加」这一支（第二百二十二轮在 wine 量过），三份副本没有。
/// 于是 <c>Row(…)</c> 出厂时把 <c>Margin</c> 置 0 的两个按钮行，被外层又套了一次 4+4：
/// wine 96 档实测「从既有接口提取」与「服务密钥」两行各 48px，而容器自己只有 40px；
/// ≈200% 档 68px 对 60px。守卫并进来之后两行各回到容器自身的高度（读数写在 DESIGN.md §8 那一格）。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 工厂只交出形状（加一行、AutoSize、标签列一句 Body、字段列那个控件、
/// 行容器不被外层加留白），不碰文字；② 三十一处字段行全部经工厂出厂，三份私有 <c>AddRow</c> 不再存在；
/// ③ 登记与源码双向对账且分方向点名（新增一屏要说「登记跟上」，删掉一屏要说「登记没跟上」）；
/// ④ 三十一句标签逐字与收成工厂之前相同（distill 收的是写法不是话）；⑤ 字段行不许出现空标签
/// （工厂没有那一支，提示行另有 <c>Ui.HintRow</c>）；⑥ 工厂依赖的前提还在——两个行容器自己把
/// <c>Margin</c> 置 0，守卫才不是「什么都不做」；⑦ 两个主页面各自的字段行按登记留在它们自己那一份
/// 行内留白上（要折进工厂是另一次带读数的决定，不该顺手发生）；⑧ 设计基线 §6 / §8 记下这条口径。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判任何一行的真实像素（「这一行到底多高」只有真实控件答得出，
/// 本轮的改前 / 改后对照由冒烟器同款构造在 wine 下读到，出处 <c>docs/CRITIQUE.md</c> 本轮那一节与
/// DESIGN.md §8；真机观感随 G-D5）；不判 <c>SupplementsDialog</c> 那份
/// <c>AddRow(grid, control)</c>——它只有一个参数、放的是补充说明的编辑位，不是「标签 + 字段」这一族；
/// 也不判两个主页面该不该换成同一套行内留白（那是决定，写在上面⑦的理由里，不由门替人做）。
/// </para>
/// </summary>
public class FieldRowTests
{
    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    /// <summary>
    /// 收成一处工厂的那三个文件，值 = 该文件里经 <c>Ui.FieldRow</c> 出厂的字段行条数。
    /// 总数不单独写死一份：31 这个数只从这张登记表算出来。
    /// </summary>
    private static readonly (string File, int Calls)[] CallSites =
    [
        ("ProfileAnalysisDialog.cs", 16),
        ("ProfileAdvancedDialog.cs", 9),
        ("GenerationDialog.cs", 6),
    ];

    /// <summary>三十一句字段名，按文件分组；逐字与收成工厂之前相同。</summary>
    private static readonly (string File, string[] Labels)[] FieldLabels =
    [
        ("GenerationDialog.cs",
        [
            "生成方式", "服务地址", "模型", "超时秒数", "每批数量", "服务密钥",
        ]),
        ("ProfileAdvancedDialog.cs",
        [
            "模块", "目录 ID", "状态", "服务 ID", "标签", "鉴权 auth", "安全方案", "公共 Header", "从既有接口提取",
        ]),
        ("ProfileAnalysisDialog.cs",
        [
            "JSON 命名", "枚举输出为字符串", "非空引用类型", "通用响应封装", "分析配置名", "编译常量",
            "调用深度", "方法预算", "XML 文档路径", "去掉 Async 后缀", "通用 data 属性名", "参数说明必填",
            "字段说明必填", "读取并发", "node 路径", "cli.js 路径",
        ]),
    ];

    /// <summary>
    /// 留在自己那一份行内留白上的两个主页面：标签列上留白与字段列下留白各不相同，
    /// 各自行高由别的段在 wine 量过（配置表单那一族 45 / 50 / 65px 的预算线、任务详情那一屏的分区行）。
    /// 折进工厂要带一次新的三档读数，不顺手发生——这一条红就是那次决定要发生的时刻。
    /// </summary>
    private static readonly (string File, string LabelPadding, string FieldPadding)[] MainPagesKeepTheirOwn =
    [
        ("ProjectsPage.cs", "new Padding(0, UiTokens.Xs, UiTokens.Sm, UiTokens.Xs)", "new Padding(0, UiTokens.Xs, 0, UiTokens.Xs)"),
        ("TasksPage.cs", "new Padding(0, UiTokens.Md, UiTokens.Sm, UiTokens.Xs)", "new Padding(0, UiTokens.Sm, 0, UiTokens.Xs)"),
    ];

    /// <summary>工厂只交出形状：加一行、AutoSize、标签列一句 Body、字段列那个控件、行容器不被外层加留白。</summary>
    [Fact]
    public void FieldRow_IsTheOnlyFactoryAndLeavesRowContainersAlone()
    {
        Assert.Single(Regex.Matches(UiSource, @"static void FieldRow\(TableLayoutPanel grid, string label, Control field\)"));
        var body = FactoryBody();
        Assert.Contains("grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));", body, StringComparison.Ordinal);
        Assert.Contains("var caption = Ui.Body(label);", body, StringComparison.Ordinal);
        Assert.Contains("caption.Margin = new Padding(0, UiTokens.Sm, UiTokens.Sm, UiTokens.Xs);", body, StringComparison.Ordinal);
        Assert.Contains("grid.Controls.Add(caption, 0, row);", body, StringComparison.Ordinal);
        Assert.Contains("grid.Controls.Add(field, 1, row);", body, StringComparison.Ordinal);
        // 行容器自己排布行内留白：外层再套一次就把那一行撑高（wine 96 档 48px 对容器自身 40px）。
        Assert.Contains("if (field is not FlowLayoutPanel) field.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"field\.Margin"));
        // 占位控件不得回来：默认尺寸的 Panel（200×100）会把这一行撑到 100px 高（同一根因见 ProfileFormLayout）。
        Assert.False(body.Contains("new Panel", StringComparison.Ordinal),
            "Ui.FieldRow 往标签列放了占位控件——提示行那一族的死空白正是这么来的。");
        // 文字是调用方交进来的：工厂不加工标签，否则各屏那个字段名会被悄悄改写。
        Assert.False(Regex.IsMatch(body, @"Trim\(|Replace\(|Substring|\+ """),
            "Ui.FieldRow 改写了传入的字段名——这一处只管形状，文字在各屏自己那一份。");
    }

    /// <summary>三份私有 <c>AddRow</c> 不得复活——只管「是否经工厂出厂」，不看条数。</summary>
    [Fact]
    public void EveryFieldRow_GoesThroughTheFactory()
    {
        foreach (var source in Repo.NativeSources())
        {
            if (MainPagesKeepTheirOwn.Any(p => p.File == source.Name)) continue;
            Assert.False(Regex.IsMatch(source.Text, @"(?m)^\s*(private|internal)\s+(static\s+)?void AddRow\(TableLayoutPanel grid, string label"),
                $"{source.Name} 又写回一份私有 AddRow——「标签 + 字段」这一行的形状就是从这里长回来的（一律走 Ui.FieldRow）。");
        }
    }

    /// <summary>
    /// 工厂调用点的<b>登记</b>要与源码对得上，且对不上时分方向点名。
    /// 新增一屏字段行时说「该登记一处」，删掉或改回就地拼形状时说「少的那几处去哪了」——
    /// 同一条红话不能把下一位同时引向两个相反的方向（第二百零七轮那一课的同一形状）。
    /// </summary>
    [Fact]
    public void CallSites_RegistryMatchesTheSource_AndSaysWhichDirection()
    {
        var discovered = Repo.NativeSources()
            .Select(s => (Name: s.Name, Calls: Regex.Matches(s.Text, @"(?m)^\s*Ui\.FieldRow\(grid, ").Count))
            .Where(s => s.Calls > 0)
            .ToDictionary(s => s.Name, s => s.Calls, StringComparer.Ordinal);
        var registered = CallSites.ToDictionary(s => s.File, s => s.Calls, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (file, calls) in registered)
        {
            var found = discovered.TryGetValue(file, out var n) ? n : 0;
            if (found == calls) continue;
            problems.Add(found > calls
                ? $"{file} 有 {found} 处经 Ui.FieldRow 出厂，登记表 {nameof(CallSites)} 只记了 {calls} 处——新增的这一处确实经工厂出厂的话，把登记改到 {found}（这条门不许把工厂调用删掉来变绿）"
                : $"{file} 只剩 {found} 处经 Ui.FieldRow 出厂，登记的是 {calls} 处——少的那几处要么退回了就地拼形状，要么那一屏的字段行被删了而登记没跟上");
        }

        foreach (var file in discovered.Keys.Except(registered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{file} 有 {discovered[file]} 处 Ui.FieldRow 却没在 {nameof(CallSites)} 里登记——字段行的口径没进这本账，下一轮就没人知道它是第几族的写法");
        }

        foreach (var file in registered.Keys.Except(discovered.Keys, StringComparer.Ordinal))
        {
            problems.Add($"{nameof(CallSites)} 登记了 {file}，可它现在已经没有一处 Ui.FieldRow——登记指向一屏不再存在的写法，这本账就空转了");
        }

        Assert.True(problems.Count == 0,
            "字段行的工厂调用点登记与源码对不上：" + string.Join("；", problems) + "。");
    }

    /// <summary>
    /// 三十一句字段名逐字与收成工厂之前相同：distill 收的是形状，不是话。
    /// 按文件分组各比一次（换屏、多一屏、少一屏都不会被另一屏的数字补上）。
    /// </summary>
    [Fact]
    public void TheFieldLabels_AreWordForWordUnchanged()
    {
        foreach (var (file, labels) in FieldLabels)
        {
            var found = Regex.Matches(NativeFile(file), @"Ui\.FieldRow\(grid, ""([^""]*)""").Cast<Match>()
                .Select(m => m.Groups[1].Value).ToList();
            Assert.True(labels.Length == found.Count,
                $"{file} 的字段名有 {found.Count} 句，登记的是 {labels.Length} 句：{string.Join("、", found)}");
            Assert.Equal(labels.OrderBy(t => t, StringComparer.Ordinal), found.OrderBy(t => t, StringComparer.Ordinal));
        }
    }

    /// <summary>字段行不得有空标签：那一支属于提示行，走 <c>Ui.HintRow</c>，工厂里没有「标签留空」这种形状。</summary>
    [Fact]
    public void FieldRows_NeverPassAnEmptyLabel()
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.DoesNotContain("Ui.FieldRow(grid, \"\"", source.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 守卫依赖的前提：两个行容器出厂时自己把 <c>Margin</c> 置 0。
    /// 工厂不加外层留白不等于「这一行没有留白」——前提被改掉时，那一行会安静地贴到上一行下面。
    /// </summary>
    [Fact]
    public void RowContainers_StillZeroTheirOwnMargin()
    {
        foreach (var file in new[] { "GenerationDialog.cs", "ProfileAdvancedDialog.cs" })
        {
            var hits = Regex.Matches(NativeFile(file), @"new FlowLayoutPanel \{ AutoSize = true, WrapContents = false, Margin = new Padding\(0\) \}");
            Assert.NotEmpty(hits);
        }
    }

    /// <summary>
    /// 两个主页面仍用自己那一份行内留白：登记里的两行留白逐字在源码里，
    /// 谁把它们折进工厂（或把工厂的留白抄过去）而没带一次三档读数，这一条先红。
    /// </summary>
    [Fact]
    public void MainPages_KeepTheirOwnRegisteredRowPadding()
    {
        foreach (var (file, labelPadding, fieldPadding) in MainPagesKeepTheirOwn)
        {
            var source = NativeFile(file);
            Assert.Contains("caption.Margin = " + labelPadding + ";", source, StringComparison.Ordinal);
            Assert.Contains("field.Margin = " + fieldPadding, source, StringComparison.Ordinal);
            Assert.DoesNotContain("Ui.FieldRow(grid, ", source, StringComparison.Ordinal);
        }
    }

    /// <summary>设计基线 §6 记下这一处工厂与它的形状，§8 记下改前 / 改后的读数与归属。</summary>
    [Fact]
    public void DesignDoc_DeclaresTheFieldRowClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("Ui.FieldRow", section, StringComparison.Ordinal);
        Assert.Contains("字段行", section, StringComparison.Ordinal);
        var gap = Repo.DesignSection(8);
        Assert.Contains("Ui.FieldRow", gap, StringComparison.Ordinal);
        Assert.Contains("行容器", gap, StringComparison.Ordinal);
    }

    private static string FactoryBody()
    {
        var start = UiSource.IndexOf("static void FieldRow(TableLayoutPanel grid, string label, Control field)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 里没有 FieldRow。");
        var end = UiSource.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.FieldRow 切不出方法体边界。");
        return UiSource[start..end];
    }
}
