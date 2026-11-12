using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 一屏多个关键词框时「一次按键只有一个落点」的判定（<see cref="FilterGesturePlan"/>）的<b>行为</b>断言。
/// <para>
/// <b>为什么要有这一份</b>：第二百二十六轮把三个双框对话框里各写一遍的「按当前可见页 / 焦点挑一个框」
/// 收成一处判定，但那一轮只改了实现与各屏的接线断言——<see cref="FilterGesturePlan"/> 本身
/// 一条用例都没有（<c>FilterEscapeTests</c> 的注释已经指向本文件，本文件当时并不存在）。
/// 于是「两框共存时按一次键落在哪」这件事，全仓只有源码形状在管：把 <c>Resolve</c> 里
/// <c>boxes[0].Enabled</c> 改成 <c>true</c>、把 Esc 的 <c>HasKeyword</c> 去掉、把 Ctrl+F 的落点
/// 从第 0 个框改成「第一个可用的框」，十一张长清单的接线断言<b>一条都不会红</b>——
/// 它们判的是「对话框有没有经分发器接线」，不是「分发器判得对不对」。
/// </para>
/// <para>
/// <b>这一份判什么</b>：① 简报点名的六种多框共存情形逐条落成真值表（人读的那一份）；
/// ② 两框 × 五种框状态 × 八种手势组合的全组合，按三条规则逐条核（机器穷举的那一份）；
/// ③ 登记表核对——每一屏登记了哪几个框、主框是哪一个、本屏的关键词框有没有漏登记
/// （<c>FilterEscapeTests</c> 指向的「登记表核对」就是这一条）。
/// </para>
/// <para>
/// <b>不动点</b>：不改 <see cref="FilterGesturePlan"/> 的对外接口、不改 <c>Ui.TryFocusFilter</c> /
/// <c>Ui.TryEnterFilter</c> / <c>Ui.TryEscapeFilter</c> 的签名、不改十一张长清单的登记方式。
/// 本文件只加断言。真机上的按键路由与「两个框都在时按一次键只动一个框」的观感随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </para>
/// </summary>
public class FilterGesturePlanTests
{
    /// <summary>十一张长清单（与 <c>FindGestureTests</c> / <c>FilterEnterTests</c> / <c>FilterEscapeTests</c> 同一份名单）。</summary>
    private static readonly string[] Dialogs =
    [
        "CheckResultsDialog.cs",
        "DecisionsDialog.cs",
        "ScanResultsDialog.cs",
        "CandidatesDialog.cs",
        "ContractsDialog.cs",
        "SyncPreviewDialog.cs",
        "EndpointTraceDialog.cs",
        "SupplementsDialog.cs",
        "PropagationDialog.cs",
        "ApplyPlanDialog.cs",
        "JournalDialog.cs",
    ];

    // ---------- ① 多框共存真值表（简报点名的六种情形 + 边界） ----------

    /// <summary>
    /// 框状态串：一个字符一个框，按登记顺序（第 0 个是本屏主框）。
    /// <c>E</c>=可用·无焦点·空、<c>F</c>=可用·有焦点·空、<c>K</c>=可用·无焦点·有字、
    /// <c>B</c>=可用·有焦点·有字、<c>D</c>=停用。串长即框数，空串即「本屏一个框都没登记」。
    /// </summary>
    public static TheoryData<string, bool, bool, bool, int?, FilterGestureAction?> Table => new()
    {
        // 两框都空 → Ctrl+F 到主框。
        { "EE", true, false, false, 0, FilterGestureAction.FocusKeyword },
        // 主框有字、副框有字 → Ctrl+F 仍到主框（不进副框：一个手势只有一个落点）。
        { "KK", true, false, false, 0, FilterGestureAction.FocusKeyword },
        // 主框有焦点、副框有字 → Enter 落主框第一行；Esc 只清主框（副框的关键词不动）。
        { "BK", false, true, false, 0, FilterGestureAction.FocusFirstResult },
        { "BK", false, false, true, 0, FilterGestureAction.ClearKeyword },
        // 副框有焦点、主框有字 → Enter 落副框第一行；Esc 只清副框（主框的关键词不动）。
        { "KB", false, true, false, 1, FilterGestureAction.FocusFirstResult },
        { "KB", false, false, true, 1, FilterGestureAction.ClearKeyword },
        // 主框空、副框有字、焦点在主框 → Esc 不动作（交回基类取消对话框）。
        { "FK", false, false, true, null, null },
        // 焦点在主框、框里没字 → Enter 仍落主框第一行（Enter 不看框里有没有字，与 Esc 不同）。
        { "FK", false, true, false, 0, FilterGestureAction.FocusFirstResult },
        // 两框都空、焦点不在任何框上 → Enter 与 Esc 都不接（一次按键只有一个含义，不猜落点）。
        { "EE", false, true, false, null, null },
        { "EE", false, false, true, null, null },
        // 主框停用 → Ctrl+F 不接，也不落到副框上（查找手势落在这一屏的主清单上）。
        { "DE", true, false, false, null, null },
        { "DD", true, false, false, null, null },
        // 三个框：焦点在第三个 → Enter / Esc 落第三个；Ctrl+F 仍只到第一个。
        { "EKB", false, true, false, 2, FilterGestureAction.FocusFirstResult },
        { "EKB", false, false, true, 2, FilterGestureAction.ClearKeyword },
        { "EKE", true, false, false, 0, FilterGestureAction.FocusKeyword },
        // 三个手势同时成立时按 Ctrl+F → Enter → Esc 的顺序取第一个（与收口前的调用顺序一致）。
        { "BB", true, true, true, 0, FilterGestureAction.FocusKeyword },
        { "BB", false, true, true, 0, FilterGestureAction.FocusFirstResult },
        // 一个框都没登记 → 三个手势一概不接（构造期两页都不在时走的就是这一支）。
        { "", true, false, false, null, null },
        { "", false, true, false, null, null },
        { "", false, false, true, null, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void OneKeyPress_LandsOnExactlyOneBox(
        string boxes, bool find, bool enter, bool escape, int? index, FilterGestureAction? action)
    {
        var step = FilterGesturePlan.Resolve(find, enter, escape, Boxes(boxes));
        if (index is null)
        {
            Assert.Null(step);
            return;
        }

        Assert.NotNull(step);
        Assert.Equal(index.Value, step!.Value.BoxIndex);
        Assert.Equal(action!.Value, step.Value.Action);
    }

    // ---------- ② 全组合：三条规则逐条核（机器穷举的那一份） ----------

    /// <summary>两框的全部框状态组合（5 × 5）与全部手势组合（2 × 2 × 2）。</summary>
    private static readonly char[] BoxStates = ['E', 'F', 'K', 'B', 'D'];

    private static readonly (bool Find, bool Enter, bool Escape)[] Gestures =
    [
        (false, false, false), (true, false, false), (false, true, false), (false, false, true),
        (true, true, false), (true, false, true), (false, true, true), (true, true, true),
    ];

    /// <summary>
    /// 两框共存的全组合（25 × 8 = 200 种）按三条规则逐条核：
    /// ① Ctrl+F 只交给主框，主框停用则这一次按键不接（不落到第二个框上）；
    /// ② Enter 交给持有焦点的那个框（焦点只有一个，故与登记顺序无关）；
    /// ③ Esc 交给「持有焦点且框里有字」的那个框（空框里按 Esc 仍是取消对话框）。
    /// <para>
    /// 这三条是<b>契约</b>，不是把 <c>Resolve</c> 抄一遍：每一条只写「该落在哪」，
    /// 不写「先判哪个手势」——优先级由真值表那几条单独钉住。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCombinationOfTwoBoxes_FollowsTheThreeRules()
    {
        var checkedCount = 0;
        foreach (var first in BoxStates)
        {
            foreach (var second in BoxStates)
            {
                var boxes = Boxes($"{first}{second}");
                foreach (var (find, enter, escape) in Gestures)
                {
                    var step = FilterGesturePlan.Resolve(find, enter, escape, boxes);
                    checkedCount++;

                    if (find)
                    {
                        // 规则 ①：只交给主框；主框停用则这一次按键不接。
                        Assert.Equal(
                            boxes[0].Enabled ? new FilterGestureStep(0, FilterGestureAction.FocusKeyword) : null,
                            step);
                        continue;
                    }

                    if (enter)
                    {
                        // 规则 ②：交给持有焦点的那个框；没有框持有焦点则不接。
                        var focused = Array.FindIndex(boxes, box => box.Focused);
                        Assert.Equal(
                            focused < 0 ? null : new FilterGestureStep(focused, FilterGestureAction.FocusFirstResult),
                            step);
                        continue;
                    }

                    if (escape)
                    {
                        // 规则 ③：交给「持有焦点且框里有字」的那个框；空框里按 Esc 仍是取消对话框。
                        var target = Array.FindIndex(boxes, box => box.Focused && box.HasKeyword);
                        Assert.Equal(
                            target < 0 ? null : new FilterGestureStep(target, FilterGestureAction.ClearKeyword),
                            step);
                        continue;
                    }

                    // 三个手势都不成立：不接这一次按键（交回基类）。
                    Assert.Null(step);
                }
            }
        }

        Assert.Equal(BoxStates.Length * BoxStates.Length * Gestures.Length, checkedCount);
    }

    /// <summary>
    /// 落点永远在登记表之内、动作永远不是 <see cref="FilterGestureAction.None"/>：
    /// 越界的下标会让 <c>FilterGestures</c> 在取 <c>_boxes[step.BoxIndex]</c> 时抛异常，
    /// 而 <c>None</c> 会让分发器把「判定成立」说成「什么都没发生」。
    /// </summary>
    [Fact]
    public void EveryLanding_IsInsideTheRegistrationAndNamesAnAction()
    {
        foreach (var first in BoxStates)
        {
            foreach (var second in BoxStates)
            {
                var boxes = Boxes($"{first}{second}");
                foreach (var (find, enter, escape) in Gestures)
                {
                    if (FilterGesturePlan.Resolve(find, enter, escape, boxes) is not { } landed) continue;
                    Assert.InRange(landed.BoxIndex, 0, boxes.Length - 1);
                    Assert.NotEqual(FilterGestureAction.None, landed.Action);
                }
            }
        }
    }

    // ---------- ③ 登记表核对：本屏有哪几个框、主框是哪一个 ----------

    /// <summary>
    /// 十一张长清单的登记表（封闭清单）：每屏登记了哪几个「关键词框 : 清单」对，按登记顺序。
    /// 主框是每一屏的第一个（Ctrl+F 的落点）；<c>SyncPreviewDialog</c> 两页各一张表，故它有三个框。
    /// </summary>
    [Theory]
    [InlineData("ApplyPlanDialog.cs", "_keyword:_list,_skippedKeyword:_skippedList")]
    [InlineData("CandidatesDialog.cs", "_keyword:_list")]
    [InlineData("CheckResultsDialog.cs", "_keyword:_list,_modelKeyword:_modelList")]
    [InlineData("ContractsDialog.cs", "_keyword:_list")]
    [InlineData("DecisionsDialog.cs", "_keyword:_list")]
    [InlineData("EndpointTraceDialog.cs", "_keyword:_list")]
    [InlineData("JournalDialog.cs", "_keyword:_list")]
    [InlineData("PropagationDialog.cs", "_keyword:_list")]
    [InlineData("ScanResultsDialog.cs", "_keyword:_list")]
    [InlineData("SupplementsDialog.cs", "_keyword:_list")]
    [InlineData("SyncPreviewDialog.cs", "_keyword:_selectList,_actionKeyword:_actionList,_excludedKeyword:_excludedList")]
    public void EachScreen_RegistersItsKeywordBoxesInOrder(string file, string expected)
    {
        var registered = string.Join(',', RegisteredPairs(file).Select(pair => $"{pair.Keyword}:{pair.List}"));
        Assert.Equal(expected, registered);
    }

    /// <summary>
    /// 本屏的每一个关键词框都登记了、且只登记一次：漏登记的那个框在 Enter 与 Esc 上什么都不做
    /// （焦点在它上面按 Enter 会提交对话框），多登记的那个框会让一次按键落到一个不存在的框上。
    /// 判定取「字段声明」与「登记表」两侧对账，任一侧多一个或少一个即红。
    /// </summary>
    [Fact]
    public void EveryKeywordBox_IsRegisteredExactlyOnce()
    {
        foreach (var file in Dialogs)
        {
            var declared = Regex.Matches(CodeScan.Text(Repo.Source("Desktop", "Native", file)),
                    @"private readonly TextBox (_\w*[Kk]eyword)\b")
                .Select(match => match.Groups[1].Value)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var registered = RegisteredPairs(file)
                .Select(pair => pair.Keyword)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.True(declared.Count > 0, $"{file} 里没有关键词框字段——登记表核对的对象不存在。");
            Assert.Equal(declared, registered);
        }
    }

    /// <summary>
    /// 主框先登记：每一屏的第一个登记项就是 Ctrl+F 的落点（<c>FilterGesturePlan</c> 规则 ① 取第 0 个）。
    /// 登记顺序反过来，Ctrl+F 就会落到副框上——而十一张长清单的接线断言看不出来（它们只判「有没有登记」）。
    /// </summary>
    [Fact]
    public void ThePrimaryBox_IsRegisteredFirstOnEveryScreen()
    {
        foreach (var file in Dialogs)
        {
            var first = RegisteredPairs(file)[0].Keyword;
            Assert.Equal("_keyword", first);
        }
    }

    /// <summary>
    /// 判定文件不认键、不碰控件：三个入参是布尔，落点是下标 + 动作。
    /// 一旦它开始认 <c>Keys</c> 或引用 WinForms，它就不再与测试工程共享编译，
    /// 上面那两张真值表跑的也就不是主工程那一份代码了。
    /// </summary>
    [Fact]
    public void ThePlan_DoesNotRecogniseKeysAndDoesNotTouchControls()
    {
        var source = Repo.Source("Desktop", "Native", "FilterGesturePlan.cs");
        var code = CodeScan.Text(source);
        Assert.Empty(CodeScan.LinesContaining(code, "Keys."));
        Assert.Empty(CodeScan.LinesContaining(code, "System.Windows.Forms"));
        Assert.Empty(CodeScan.LinesContaining(code, "TextBox"));
        Assert.Empty(CodeScan.LinesContaining(code, "ListView"));
        // 判定只有一处：Resolve 是唯一对外入口，界面不另写一份「落在哪个框」。
        Assert.Single(Regex.Matches(code, @"public static FilterGestureStep\? Resolve\("));
    }

    /// <summary>判定文件必须与测试工程共享编译，否则上面那些真值表跑的不是主工程那一份代码。</summary>
    [Fact]
    public void SharedCompileList_CarriesThePlan()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\FilterGesturePlan.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 助手 ----------

    /// <summary>框状态串 → 判定入参（见 <see cref="Table"/> 的字符表）。</summary>
    private static FilterBoxState[] Boxes(string spec) => spec.Select(c => c switch
    {
        'E' => new FilterBoxState(Enabled: true, Focused: false, HasKeyword: false),
        'F' => new FilterBoxState(Enabled: true, Focused: true, HasKeyword: false),
        'K' => new FilterBoxState(Enabled: true, Focused: false, HasKeyword: true),
        'B' => new FilterBoxState(Enabled: true, Focused: true, HasKeyword: true),
        'D' => new FilterBoxState(Enabled: false, Focused: false, HasKeyword: false),
        _ => throw new ArgumentOutOfRangeException(nameof(spec), c, "未知的框状态字符。"),
    }).ToArray();

    /// <summary>
    /// 一屏的登记项（按源码顺序）：<c>new FilterGestures(框, 清单)</c> 与随后的 <c>.Then(框, 清单)</c>
    /// 是同一种形状，取法只有这一处——两处各写一个正则，就会有一处改了另一处没跟上。
    /// </summary>
    private static List<(string Keyword, string List)> RegisteredPairs(string file) =>
        Regex.Matches(CodeScan.Text(Repo.Source("Desktop", "Native", file)),
                @"(?:new FilterGestures|\.Then)\((\w+), (\w+)\)")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
            .ToList();
}
