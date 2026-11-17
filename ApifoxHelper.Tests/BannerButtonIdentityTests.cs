using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 引导横幅那两颗按钮「出厂一次、刷新只换字与显隐」（第三百六十八轮，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>TasksPage.RefreshNextBanner</c> 此前每次刷新都 <c>Controls.Clear()</c> 再新建两颗
/// 按钮，而 <c>RefreshDetail</c> 在每一次动作之后都会走到这里（<c>StartScan</c> 自己就调一次）——键盘用户
/// Tab 到横幅那颗按钮、按空格之后，按钮当场被从父面摘掉，焦点跳到别处，下一次刷新再摘一次。同一页另四行
/// 动作按钮（<c>_stepper</c> / <c>_scanActions</c> / <c>_generatedActions</c> / <c>_remoteActions</c>）都是
/// 出厂一次、刷新只改 <c>Enabled</c> / <c>Visible</c>，这一行是唯一的例外。
/// </para>
/// <para>
/// <b>为什么要有这一条源码门</b>：改回「<c>Controls.Clear()</c> 再新建」时，<c>dotnet test</c> 一个字节都不会红
/// ——判「焦点还在不在」要真的把窗体显示出来、真的把焦点放上去，那是冒烟段 <c>RunBannerFocus</c> 的事
/// （wine 里跑，本环境不是真机）。而冒烟段只在有人跑那一支链时才说话；<b>形状</b>这一半必须由
/// <c>dotnet test</c> 当场钉住，否则下一次重构把这两颗按钮改回「每次新建」，要等到下一次重烧才有人发现。
/// </para>
/// <para>
/// <b>本文件判什么</b>（只看代码行，注释里引用同一句是在说理由——去注释口径只有 <see cref="CodeScan"/> 一份）：
/// ① <c>RefreshNextBanner</c> 的方法体里不得出现 <c>Controls.Clear()</c> 或往 <c>_nextActions</c> 里加控件；
/// ② 同一个方法体里必须给两颗按钮各换 <c>Text</c> 与 <c>Visible</c>（「藏起来而不是摘掉」那一半）；
/// ③ 两颗按钮是<b>字段</b>（<c>private readonly Button</c>），且往 <c>_nextActions</c> 里各加一次、只加一次；
/// ④ 点击只读「这一次的动作」那一笔（<c>RunNextAction(_nextPrimaryAction)</c>），不把动作捕获进闭包；
/// ⑤ 那一行的标记 <c>Ui.BannerRowMarker</c> 在产品源码里恰好赋值一处，冒烟器按标记认行、不抄用户可见文案；
/// ⑥ 冒烟段 <c>RunBannerFocus</c> 注册在案，且段体里真的把焦点放上去、真的走一次刷新、真的读「同一颗实例」
/// 与「焦点还在它身上」。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「焦点在真机上真的还在不在」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判横幅该不该有次动作、文案该写什么（那是 <c>NextStep</c> 的口径，另有门）。
/// </para>
/// </summary>
public class BannerButtonIdentityTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string Smoke => Repo.SmokeProgram;

    /// <summary>
    /// 仓里当前那一份：逐条对账，把每一处「形状被改回去了」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void BannerButtons_AreCreatedOnceAndRefreshedInPlace()
    {
        var problems = Problems(TasksPage, UiSource, Smoke);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：五发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、第二百四十三轮
    /// M4 那一发假绿）。五发分别是：① 刷新里改回 <c>Controls.Clear()</c>；② 刷新里不再换 <c>Visible</c>
    /// （「藏起来」退化成「留着上一次的字」）；③ 点击把动作捕获进闭包；④ 冒烟器抄一份标记字面量；
    /// ⑤ 合规写法（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ⑤ 合规写法：仓里这一份一处都不报。
        Assert.Empty(Problems(TasksPage, UiSource, Smoke));

        // ① 刷新里改回「清空再新建」：焦点会跟着丢，这一发是本门最想钉住的那一发。
        var cleared = TasksPage.Replace(
            "        _nextPrimaryAction = next.PrimaryAction;",
            "        _nextActions.Controls.Clear();\n        _nextPrimaryAction = next.PrimaryAction;",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, cleared);
        Assert.Contains(Problems(cleared, UiSource, Smoke),
            p => p.Contains("Controls.Clear", StringComparison.Ordinal));

        // ② 不再换 Visible：没有那一半时按钮留着上一次的字亮在屏上。
        var noVisible = TasksPage.Replace(
            "        _nextSecondary.Visible = next.SecondaryLabel is not null;",
            "        _ = next.SecondaryLabel;",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, noVisible);
        Assert.Contains(Problems(noVisible, UiSource, Smoke),
            p => p.Contains("_nextSecondary.Visible", StringComparison.Ordinal));

        // ③ 点击把动作捕获进闭包：换字之后点下去做的还是上一次那一件事。
        var captured = TasksPage.Replace(
            "_nextPrimary.Click += (_, _) => RunNextAction(_nextPrimaryAction);",
            "_nextPrimary.Click += (_, _) => RunNextAction(\"startScan\");",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, captured);
        Assert.Contains(Problems(captured, UiSource, Smoke),
            p => p.Contains("RunNextAction(_nextPrimaryAction)", StringComparison.Ordinal));

        // ④ 冒烟器抄一份标记字面量：标记一改，冒烟器认的还是旧那一行（或认不到）。
        var copied = Smoke.Replace(
            "(row.Tag as string) == Ui.BannerRowMarker",
            "(row.Tag as string) == \"next-actions\"",
            StringComparison.Ordinal);
        Assert.NotEqual(Smoke, copied);
        Assert.Contains(Problems(TasksPage, UiSource, copied),
            p => p.Contains("BannerRowMarker", StringComparison.Ordinal));
    }

    /// <summary>
    /// 把三份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>源码喂它——与
    /// <c>MeasuredScreensTests.Problems</c>、<c>WiringAssertionShapeTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(string tasksPage, string ui, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);

        // ① 刷新里不得清空 / 往那一行里加控件。
        var refresh = Body(code, "private void RefreshNextBanner(");
        if (refresh is null)
        {
            problems.Add("TasksPage 里找不到 RefreshNextBanner 的方法体：本门按它判「刷新只换字与显隐」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            if (refresh.Contains("Controls.Clear()", StringComparison.Ordinal))
            {
                problems.Add("RefreshNextBanner 里出现了 Controls.Clear()：横幅那两颗按钮会被从父面摘掉，"
                    + "键盘焦点跟着丢（Tab 到那颗按钮、按空格之后紧接着的那一次刷新就摘一次）。"
                    + "刷新只该换字与显隐。");
            }

            if (refresh.Contains("_nextActions.Controls.Add(", StringComparison.Ordinal))
            {
                problems.Add("RefreshNextBanner 里往 _nextActions 加控件：那两颗按钮该在 BuildDetail 里出厂一次，"
                    + "刷新时新建一颗就是换了一个实例，焦点与 Tab 位置都跟着丢。");
            }

            // ② 两颗按钮各换 Text 与 Visible。
            foreach (var (field, label) in new[]
                     {
                         ("_nextPrimary", "PrimaryLabel"),
                         ("_nextSecondary", "SecondaryLabel"),
                     })
            {
                if (!refresh.Contains($"{field}.Text = next.{label} ?? \"\"", StringComparison.Ordinal))
                {
                    problems.Add($"RefreshNextBanner 里没有给 {field} 换字（应为 {field}.Text = next.{label} ?? \"\"）："
                        + "刷新之后屏上还是上一屏的话。");
                }

                if (!refresh.Contains($"{field}.Visible = next.{label} is not null", StringComparison.Ordinal))
                {
                    problems.Add($"RefreshNextBanner 里没有给 {field}.Visible 赋值（应为 {field}.Visible = next.{label} is not null）："
                        + "没有那一半时该藏起来而不是留着上一次的字亮在屏上。");
                }
            }
        }

        // ③ 两颗按钮是字段，且往那一行里各加一次、只加一次。
        foreach (var field in new[] { "_nextPrimary", "_nextSecondary" })
        {
            if (!code.Contains($"private readonly Button {field} = Ui.Ghost(\"\");", StringComparison.Ordinal))
            {
                problems.Add($"TasksPage 里没有 {field} 这个字段（应为 private readonly Button {field} = Ui.Ghost(\"\");）："
                    + "它不是出厂一次的那一颗，刷新时就会新建一个实例。");
            }

            var adds = Count(code, $"_nextActions.Controls.Add({field});");
            if (adds != 1)
            {
                problems.Add($"TasksPage 里往 _nextActions 加 {field} 出现 {adds} 处（应为 1 处，在 BuildDetail 里）："
                    + "0 处 = 那一行没有按钮；多于 1 处 = 有第二处也在往这一行里塞控件。");
            }
        }

        // ④ 点击只读「这一次的动作」那一笔。
        foreach (var (field, action) in new[]
                 {
                     ("_nextPrimary", "_nextPrimaryAction"),
                     ("_nextSecondary", "_nextSecondaryAction"),
                 })
        {
            if (!code.Contains($"{field}.Click += (_, _) => RunNextAction({action});", StringComparison.Ordinal))
            {
                problems.Add($"{field} 的点击没有读 {action}（应为 {field}.Click += (_, _) => RunNextAction({action});）："
                    + "把动作捕获进闭包之后，换字时点下去做的还是上一次那一件事。");
            }
        }

        // ⑤ 标记在产品源码里恰好赋值一处，冒烟器按标记认行、不抄字面量。
        var marker = MarkerValue(ui);
        if (marker is null)
        {
            problems.Add("Ui.cs 里找不到 BannerRowMarker 这个常量：冒烟器按它认「横幅那一行」，"
                + "常量没了那一段就认不到行。");
        }
        else
        {
            var assignments = Count(code, "= Ui.BannerRowMarker;");
            if (assignments != 1)
            {
                problems.Add($"产品源码里给 Ui.BannerRowMarker 赋值 {assignments} 处（应为 1 处，在 BuildDetail 里）："
                    + "0 处 = 冒烟器认不到那一行；多于 1 处 = 有第二行也叫这个名字，认行会认错。");
            }

            if (smoke.Contains($"\"{marker}\"", StringComparison.Ordinal))
            {
                problems.Add($"冒烟器里抄了一份标记字面量 \"{marker}\"：标记一改，冒烟器认的还是旧那一行。"
                    + "认行该读 Ui.BannerRowMarker，不抄第二份。");
            }
        }

        // ⑥ 冒烟段注册在案，且段体真的量了那三件事。
        if (!smoke.Contains("nameof(RunBannerFocus)", StringComparison.Ordinal))
        {
            problems.Add("冒烟器里没有注册 RunBannerFocus 这一段：横幅那两颗按钮的「同一颗」与键盘焦点"
                + "在 dotnet test 里判不到（要真的显示窗体、真的放焦点），这一段是它唯一的量测处。");
        }

        var body = Body(CodeScan.Text(smoke), "private static void RunBannerFocus(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunBannerFocus 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("primary.Focus();", "把键盘焦点真的放到横幅那颗按钮上"),
                         ("page.RefreshData();", "真的走一次刷新（RefreshData → LoadDetail → RefreshNextBanner）"),
                         ("row.Controls.Contains(primary)", "读「刷新之后那一颗还在父面里」"),
                         ("primary.Focused", "读「刷新之后焦点还在它身上」"),
                         ("ReferenceEquals(before[i], after[i])", "读「刷新四次之后还是同一颗实例」"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunBannerFocus 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「刷新之后还是不是同一颗、焦点还在不在」，少了这一句就量不到那件事。");
                }
            }
        }

        return problems;
    }

    /// <summary><c>Ui.cs</c> 里 <c>BannerRowMarker</c> 那个常量的值（读不到返回 <c>null</c>）。</summary>
    private static string? MarkerValue(string ui)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            CodeScan.Text(ui),
            @"public const string BannerRowMarker = ""(?<value>[^""]*)"";");
        return match.Success ? match.Groups["value"].Value : null;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> / <c>foreach</c> 的花括号，
    /// 按行首关键字切会把方法体截短，而截短之后「里面没有 <c>Controls.Clear()</c>」会假绿。
    /// </para>
    /// </summary>
    private static string? Body(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var open = code.IndexOf('{', start);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return code[open..(i + 1)];
            }
        }

        return null;
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
