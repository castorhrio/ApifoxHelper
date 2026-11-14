using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 勾不动的那一行说一句为什么（第二百九十八轮，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：同步预览勾选页的接口清单里，不可同步的行此前<b>一句话都没有</b>——
/// <c>SyncPreviewDialog</c> 的 <c>ItemCheck</c> 把 <c>e.NewValue</c> 改回 <c>Unchecked</c>，
/// 勾选框闪一下就回去了，状态行、提示行、计数行一个字都不变。用户看到的与「勾上了又被谁取消」分不开，
/// 而这一页的说明行只写了<b>整页的规则</b>（「无变化、阻断与仅源码不可同步」）——他点到的那一行是哪一种、
/// 为什么勾不上，得在现场说出来；离线模式那一种说明行里根本没提。
/// 第四十轮与第二百八十七、二百九十五轮判的是「<b>禁用</b>的控件看起来按得动」，
/// 这一处是同一族判据的另一半：<b>看起来按得动、按下去什么也没有</b>。
/// </para>
/// <para>
/// <b>判据只有一份</b>：能不能勾由 <see cref="SyncSelection.Selectable"/> 判，为什么勾不上由
/// <see cref="SyncSelection.RejectMessage"/> 说——后者第一句就是前者，不另列一份状态表。
/// 本文件前半段在 Linux 上真执行那一份判定（含「两处判据逐状态同源」），后半段按源码切片钉住
/// 「界面只在一处落地、冒烟段真的按过」。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上那一句的观感（字色对比度、状态行放不放得下）——那随 G-D5 复核；
/// 也不判「计划阶段为什么没进计划」的逐条原因，那一份仍只在 <c>SyncPlanService</c> 一处。
/// </para>
/// </summary>
public class RejectMessageTests
{
    private const string DialogFile = "SyncPreviewDialog.cs";

    /// <summary>清单里会出现的全部检查状态（与 <see cref="TaskDisplay.CheckStatusText"/> 的映射同一份）。</summary>
    private static readonly string[] AllStatuses =
        ["new", "structure", "docs", "unchanged", "blocked", "sourceOnly", "mystery"];

    // ---------- 真执行：那一句话与「能不能勾」同源 ----------

    /// <summary>
    /// 可勾的行<b>没有</b>那一句话，勾不上的行<b>有</b>——逐状态、在线与离线两个方向都判。
    /// <para>
    /// 这一条是「两处判据同源」的正面判据：<see cref="SyncSelection.RejectMessage"/> 若自己列一份
    /// 「哪些状态可同步」，迟早一处改了另一处没改，界面上就会出现「勾得上却说勾不上」或反过来。
    /// </para>
    /// </summary>
    [Fact]
    public void RejectMessage_IsNullExactlyWhenSelectable()
    {
        foreach (var offline in new[] { false, true })
        {
            foreach (var status in AllStatuses)
            {
                var selectable = SyncSelection.Selectable(offline, status);
                var message = SyncSelection.RejectMessage("GET /api/x", offline, status);
                Assert.True(selectable == (message is null),
                    $"离线={offline}、状态={status}：Selectable 说 {(selectable ? "可勾" : "不可勾")}，"
                    + $"RejectMessage 却{(message is null ? "没给" : "给了")}那一句话——两处判据漂了。");
            }
        }
    }

    /// <summary>
    /// 那一句话里必须有<b>用户眼睛看得到的那三样</b>：接口身份、状态文字（与清单「状态」列同一份映射）、
    /// 以及「勾不上」这个结论。少一样，用户就得自己去猜是哪一行、为什么。
    /// </summary>
    [Fact]
    public void RejectMessage_NamesTheRowAndTheStatusTheUserSees()
    {
        foreach (var status in new[] { "unchanged", "blocked", "sourceOnly", "mystery" })
        {
            var message = SyncSelection.RejectMessage("GET /api/orders", offlineMode: false, status);
            Assert.NotNull(message);
            Assert.Contains("GET /api/orders", message, StringComparison.Ordinal);
            // 状态那一段取清单「状态」列同一份映射：状态行上说的那两个字与用户看到的那一格逐字相同。
            Assert.Contains(TaskDisplay.CheckStatusText(status), message, StringComparison.Ordinal);
            Assert.Contains("勾不上", message, StringComparison.Ordinal);
            Assert.Contains("不进计划", message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 离线模式那一种<b>说离线</b>，不说成「这一行是「新增」」——离线时整份检查结果都不可同步，
    /// 与那一行自己是什么状态无关；说成状态会让人以为换个状态就能勾。
    /// </summary>
    [Fact]
    public void RejectMessage_OfflineMode_SaysTheWholeResultIsOffline()
    {
        foreach (var status in AllStatuses)
        {
            var message = SyncSelection.RejectMessage("GET /api/orders", offlineMode: true, status);
            Assert.NotNull(message);
            Assert.Contains("离线", message, StringComparison.Ordinal);
            Assert.DoesNotContain(TaskDisplay.CheckStatusText(status) + "」", message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 接口身份<b>原样写进那一句</b>：空 key 仍说清为什么，超长 key 不被截断（第二百九十九轮，简报 P1-2）。
    /// <para>
    /// <b>为什么补这一条</b>：<see cref="SyncSelection.RejectMessage"/> 的 <c>endpointKey</c> 此前只有
    /// 「正常 key」这一种输入被真执行过，两种边界（空 / 超长）的<b>当前行为</b>没有任何东西钉住——
    /// 下一轮有人顺手加一句 <c>endpointKey.Trim()</c> 或 <c>Truncate(40)</c>，界面上「是哪一行」这半句
    /// 就悄悄变了，而所有断言照旧全绿。这一条钉的是<b>已经承诺的行为</b>（那一句里必须有那一行），
    /// 不是新造一条口径：空 key 与超长 key 在真实数据里一次都没出现过（<c>EndpointCheck.Key</c> 由扫描器
    /// 按「方法 + 空格 + 路径」拼出来），所以<b>不为它们另写一句话</b>，只把当前行为钉住。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判「空 key 时该不该换一句话」、不判「超长 key 该不该截断」——那两条只有人读得出来，
    /// 登记在 <see cref="SyncSelection.RejectMessage"/> 的「边界登记」那一段里。
    /// </para>
    /// </summary>
    [Fact]
    public void RejectMessage_KeyIsWrittenVerbatim_EmptyAndLongKeysIncluded()
    {
        // 空 key：括号里空着，但「为什么勾不上」那半句照旧完整——用户仍读得到结论。
        var empty = SyncSelection.RejectMessage("", offlineMode: false, checkStatus: "unchanged");
        Assert.NotNull(empty);
        Assert.Contains("「」勾不上", empty, StringComparison.Ordinal);
        Assert.Contains("不进计划", empty, StringComparison.Ordinal);

        // 超长 key：原样写进去、不截断（截断处正是区分两个接口的那一段）。
        var longKey = "GET /api/" + new string('x', 400);
        var longMessage = SyncSelection.RejectMessage(longKey, offlineMode: false, checkStatus: "blocked");
        Assert.NotNull(longMessage);
        Assert.Contains(longKey, longMessage, StringComparison.Ordinal);
        Assert.EndsWith("勾选不生效。", longMessage, StringComparison.Ordinal);
    }

    // ---------- 源码形状：界面只在一处落地 ----------

    /// <summary>
    /// 对话框<b>不自己拼那一句话</b>：弹回那一支里取的是 <see cref="SyncSelection.RejectMessage"/>，
    /// 且整个文件里没有第二份「勾不上」的字面量。
    /// <para>
    /// 判据是「界面若自己拼一句就是第二份文案」：两处文案今天相等，口径一改就分叉，
    /// 而分叉的那一刻两边都还是绿的。
    /// </para>
    /// </summary>
    [Fact]
    public void Dialog_TakesTheSentenceFromTheSharedSource()
    {
        var source = Repo.Source("Desktop", "Native", DialogFile);
        var body = Slice(source, "_selectList.ItemCheck += (_, e) =>", "_selectList.ItemChecked += (_, e) =>");
        Assert.Contains("SyncSelection.RejectMessage(", body, StringComparison.Ordinal);
        // 弹回仍在：不可同步的行必须回到未勾（这一句是这一屏的写入安全底线，不许被这一轮改掉）。
        Assert.Contains("e.NewValue = CheckState.Unchecked;", body, StringComparison.Ordinal);
        // 那一句话交给状态行，且用警示色（与「作业失败」同一档语义色，不新造颜色）。
        Assert.Contains("SetStatus(_rejectMessage, UiTokens.WarnText);", body, StringComparison.Ordinal);
        // 界面里没有第二份「勾不上」文案：那一句只在 SyncSelection 一处。
        Assert.DoesNotContain("勾不上", CodeScan.Text(source), StringComparison.Ordinal);
    }

    /// <summary>
    /// 那一句话<b>只在勾选真的变了时收掉</b>，且只收自己写的那一句。
    /// <para>
    /// 判据是「这一行真的勾上了」而不是「<c>ItemChecked</c> 来过一次」：勾不动的那一行自己也会走一次
    /// <c>ItemChecked</c>（<c>Checked</c> 仍是 false），在那里收掉等于刚写上去就抹掉，用户一个字都看不到。
    /// 收的时候还要认「当前文字逐字等于我写的那一句」——状态行上别人的消息（作业进度、失败原因）一个字不碰。
    /// </para>
    /// <para>
    /// <b>前提登记：状态行文字原样写入、不做规范化（第二百九十九轮，简报 P1-1）</b>。
    /// 上面那句「当前文字逐字等于我写的那一句」是<b>字面相等</b>判据，它成立的前提是
    /// <c>StatusLineHost.SetText</c> 把传进来的文字<b>原样</b>写进标签（<c>_label.Text = text ?? ""</c>：
    /// 不 Trim、不折叠空白、不换全角半角、不截断），于是「我写进去的那一句」与「读回来的那一句」
    /// 逐字相同。若哪天有人在写状态行那一处加了规范化（例如顺手 Trim 一下、把连续空格折成一个），
    /// 这一句判据会<b>静默失效</b>——收不掉那一句，用户看到的是上一次点的那一行留下的旧话，
    /// 而所有断言照旧全绿（它们判的是「这一行代码在不在」，判不到「写进去的文字有没有被改过」）。
    /// </para>
    /// <para>
    /// <b>这一条只登记、不加门</b>：能机械判的只有「<c>SetText</c> 里那一句赋值在不在」，
    /// 而「有没有人在别处又加了一层规范化」判不到——加一条门只会把「门绿」当成「前提成立」。
    /// 前提的落点是 <c>Desktop/Native/StatusLineHost.cs</c> 的 <c>SetText</c>（全仓状态行唯一的写入点，
    /// 由 <c>StatusLineTextTests.EveryStatusLine_OnDisk_IsWrittenThroughTheHost</c> 判住「都经它写」）。
    /// </para>
    /// </summary>
    [Fact]
    public void Dialog_ClearsTheSentenceOnlyWhenTheSelectionReallyChanged()
    {
        var source = Repo.Source("Desktop", "Native", DialogFile);
        var checkedBody = Slice(source, "_selectList.ItemChecked += (_, e) =>", "_selectAllButton = Ui.Ghost(");
        Assert.Contains("if (e.Item.Checked) ClearRejectMessage();", checkedBody, StringComparison.Ordinal);

        var helper = Slice(source, "private void ClearRejectMessage()", "private void SelectAllSyncable()");
        // 只收自己写的那一句：别人的消息一个字不碰。
        Assert.Contains("if (_status.Text == _rejectMessage) SetStatus(\"\", UiTokens.Text);", helper, StringComparison.Ordinal);
        // 两个批量动作也收：勾选整体变了，那一句讲的是上一次点的那一行，已经过期。
        Assert.Contains("ClearRejectMessage();", Slice(source, "private void ClearSelection()", "private void SelectAllSyncable()"), StringComparison.Ordinal);
        Assert.Contains("ClearRejectMessage();", Slice(source, "private void SelectAllSyncable()", "private void RefreshSelectionCount()"), StringComparison.Ordinal);
    }

    // ---------- 冒烟段：三件事真的按过 ----------

    /// <summary>冒烟段里这一族必须有的四句能读的话（每一句对应一件事）。</summary>
    private static readonly string[] SmokeStates =
    [
        "这一行勾上了：不可同步的行必须弹回",   // ① 那一行勾不上
        "勾不动的那一行必须说一句为什么",       // ② 状态行上真的出现了那一句
        "勾上了就该收掉",                       // ③ 勾上一行可同步的，那一句自己收掉
        "计数没跟着勾选走",                     // ③ 计数行跟着勾选走
    ];

    /// <summary>
    /// 冒烟段真的按过这三件事：勾一次不可同步的行（弹回 + 状态行那一句）、再勾一次可同步的行
    /// （那一句收掉 + 计数行跟着走），期望值取产品口径而不是界面自己拼一句。
    /// </summary>
    [Fact]
    public void TheSmokeSegment_ReallyPressesTheRejectedRow()
    {
        var body = Slice(
            Repo.SmokeProgram,
            "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)",
            "/// <summary>\n    /// 勾选页的关键词框");
        Assert.Empty(SmokeProblems(body));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：三发注入各报一处，合规段体不报。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。
    /// </para>
    /// </summary>
    [Fact]
    public void SmokeDetector_ReportsTheBreaksItMustCatch()
    {
        var body = Slice(
            Repo.SmokeProgram,
            "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)",
            "/// <summary>\n    /// 勾选页的关键词框");
        Assert.Empty(SmokeProblems(body));

        // ① 期望值改成界面自己拼一句（不再取产品口径）。
        var ownCopy = body.Replace("SyncSelection.RejectMessage(", "OwnCopy(", StringComparison.Ordinal);
        Assert.Contains(SmokeProblems(ownCopy), p => p.Contains("SyncSelection.RejectMessage", StringComparison.Ordinal));

        // ② 删掉「状态行上真的出现了那一句」那一态。
        var noStatus = body.Replace("勾不动的那一行必须说一句为什么", "", StringComparison.Ordinal);
        Assert.Contains(SmokeProblems(noStatus), p => p.Contains("勾不动的那一行必须说一句为什么", StringComparison.Ordinal));

        // ③ 删掉「那一句自己收掉」那一态。
        var noClear = body.Replace("勾上了就该收掉", "", StringComparison.Ordinal);
        Assert.Contains(SmokeProblems(noClear), p => p.Contains("勾上了就该收掉", StringComparison.Ordinal));
    }

    /// <summary>
    /// 冒烟段体的问题清单（纯函数：一段源码进、问题出）。判据不读文件、不跑 wine，注入当场可验。
    /// </summary>
    private static List<string> SmokeProblems(string body)
    {
        var problems = new List<string>();
        // 期望值取产品口径：界面若自己拼一句，两处文案迟早分叉。
        if (!body.Contains("SyncSelection.RejectMessage(", StringComparison.Ordinal))
        {
            problems.Add("冒烟段没有取产品口径 SyncSelection.RejectMessage：期望值若由界面自己拼，两处文案迟早分叉。");
        }
        foreach (var state in SmokeStates)
        {
            if (!body.Contains(state, StringComparison.Ordinal))
            {
                problems.Add("冒烟段缺少这一态：" + state);
            }
        }
        // 真的勾了两次：一次不可同步（弹回）、一次可同步（收掉那一句）。
        if (!body.Contains("rejectedRow.Checked = true;", StringComparison.Ordinal))
        {
            problems.Add("冒烟段没有真的勾一次不可同步的行：这一族量的是「按下去什么也没有」，不按就量不到。");
        }
        if (!body.Contains("acceptedRow.Checked = true;", StringComparison.Ordinal))
        {
            problems.Add("冒烟段没有真的勾一次可同步的行：那一句「勾不上」收不收得掉，只有勾上别的行才量得到。");
        }
        return problems;
    }

    // ---------- 辅助 ----------

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
