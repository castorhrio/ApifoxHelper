using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 说明行的形状由它背的意思决定（第二百七十四轮，<c>docs/CRITIQUE.md</c> 第 4 站「最该改的一处」，
/// 具名 pass：clarify + harden）。
/// <para>
/// <b>要解决的问题</b>：<c>ApplyPlanDialog</c> 的「跳过的位置（原因如下；处理后重新生成计划）」与
/// <c>SyncPreviewDialog</c> 的「未纳入计划（原因如下；处理后重新检查、重新生成同步预览）」是同一个模板
/// 「X（原因如下；处理后重新 Y）」填了两次，而同一个括注形状在 <c>PropagationDialog</c> 的
/// 「XML 文档文件（早于源码修改的可能是旧生成物）」那里背的既不是「这块框是什么」也不是「这一条为什么」，
/// 是<b>判读规则</b>。三处都短、都过 <see cref="CaptionBrevityTests"/> 那两道门（长度 ≤80 字、≤2 句）——
/// 长度门挡不住这件事，与第一百五十六轮那句「模板脸的风险从长度挪到了句式」是同一族，只是这一次不在
/// 任务详情区那五段，在三块底部清单的标题行上。
/// </para>
/// <para>
/// <b>本轮改成了什么</b>：每句只背自己那一屏的意思——注释回写屏说「这块框是什么 + 每条为什么在
/// 「原因」那一列 + 下一步是哪颗按钮」；同步预览屏说「为什么没进这份计划由哪一列逐条交出 + 要重做先点
/// 哪颗按钮」；生成文档核对屏那句本来就是判读规则，于是写成规则话并把后果带上（旧生成物不算注释已生效，
/// 与 <c>docs/功能清单与验收标准.md</c> F06「旧生成物不算已生效」同一口径）。
/// </para>
/// <para>
/// <b>本文件判什么</b>：① 那个模板骨架不得复活（<see cref="TemplateSkeleton_IsNotFilledTwice"/>）；
/// ② 三句两两不共一段 8 字以上的连续文字（<see cref="TheThreeCaptions_DoNotShareOneTemplate"/>）——
/// 这是「同一个模子填两次」在文字上唯一可机械测的痕迹；③ <b>说明行里用「」点出来的名字，必须是这一屏
/// 整串交出的那段界面文字</b>（<see cref="EveryQuotedName_IsTextThatScreenItselfEmits"/>）：列头、按钮文字、
/// 或单元格追加的标记，<b>整串相等</b>才算，嵌在句子里的同名子串不算。这一条是这三句真正的新增判据，
/// 也是它凭什么比旧句更可用——旧句说「原因如下」「处理后重新生成计划」，而这一屏的按钮就叫「重新生成计划」、
/// 那一列就叫「原因」，指名字不指概念，用户才点得着。<b>「整串相等」这一格是变异教出来的</b>：M2 / M3
/// 两发（把那一列的列头改名、把那颗按钮改名）在「含子串」那一版判法下<b>照样绿</b>——同一文件的状态行字面
/// 「…（原因见结果列）。请处理后重新生成计划。」把两个名字都含住了；界面已经找不到那一格，门却不说。
/// ④ 判据自己被抓得住（<see cref="Gate_ReportsTheBreaksItMustCatch"/>，六发注入各报一处、合规写法不报）。
/// </para>
/// <para>
/// <b>范围如实限定</b>：②只判这三处（第 4 站点名的那三句）。全原生界面 40 多处说明行不在这里——两句
/// 不同屏的说明行共用一个八字短语未必是模板脸（「重新检查后生效」这类跨屏事实本来就同一句话），
/// 把②铺到全部说明行会变成一条要求人改词的门，而不是判据。①与③倒是全原生界面判：一个骨架、一个引用不落空。
/// <b>本文件不判长度与句数</b>——那是 <see cref="CaptionBrevityTests"/> 的活，重抄一遍就是第二份口径；
/// 也不判「这句话好不好听」，那是判断，写在 <c>docs/CRITIQUE.md</c> 第 4 站与本仓 <c>docs/DESIGN.md</c> §8。
/// </para>
/// </summary>
public class CaptionPromiseTests
{
    /// <summary>两个不同屏的说明行允许共用的最长连续文字（字）。本轮改完实测最长公共串是 4 字（「点「重新」）。</summary>
    private const int MaxSharedRun = 8;

    /// <summary>那个模板骨架的字面：出现一处即说明「一个括注背三种意思」又回来了。</summary>
    private static readonly string[] TemplateFragments = ["原因如下", "（原因如下；处理后重新"];

    /// <summary>
    /// 三处落点：<b>文件</b>、那一屏说明行的<b>开头</b>（用它在这屏全部说明行里认出那一句）、
    /// 那句话必须说到的<b>自己的意思</b>。
    /// </summary>
    private sealed record Site(string File, string Prefix, string[] MustSay);

    private static readonly Site[] Sites =
    [
        new("ApplyPlanDialog.cs", "跳过的位置", ["这些注释位置本次不写回源码", "原因", "重新生成计划"]),
        new("SyncPreviewDialog.cs", "未纳入计划", ["为什么没进这份计划", "未纳入原因", "重新选择接口"]),
        new("PropagationDialog.cs", "XML 文档文件", ["旧生成物", "不算注释已生效"]),
    ];

    // ---------- 取那三句（经既有提取口径，不开第二份提取器） ----------

    private static List<CaptionBrevityTests.Caption> AllCaptions() =>
        CaptionBrevityTests.ExtractCaptions(
            Repo.NativeSources().Select(s => (s.Name, s.Text)),
            CaptionBrevityTests.SharedConstants());

    /// <summary>某一屏的那一句说明行（按开头认；找不到就是提取口径或那一屏的写法变了）。</summary>
    private static CaptionBrevityTests.Caption CaptionOf(Site site)
    {
        var hits = AllCaptions()
            .Where(c => c.File == site.File && c.Text.StartsWith(site.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.True(hits.Count == 1, $"{site.File} 里按「{site.Prefix}」开头认说明行，找到 {hits.Count} 条（应为 1 条）。");
        return hits[0];
    }

    // ---------- 判定本体（纯函数：文本进、问题出，注入自测直接喂夹具） ----------

    /// <summary>① 模板骨架在哪些文件里出现了多少次。</summary>
    internal static List<string> TemplateHits(IEnumerable<(string Name, string Text)> sources)
    {
        var hits = new List<string>();
        foreach (var (name, text) in sources)
        {
            foreach (var fragment in TemplateFragments)
            {
                var count = Regex.Matches(text, Regex.Escape(fragment)).Count;
                if (count > 0) hits.Add($"{name}（「{fragment}」×{count}）");
            }
        }
        return hits;
    }

    /// <summary>② 两句之间最长的公共连续片段（字数）。同一句自己比自己是自己的长度。</summary>
    internal static int LongestCommonRun(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var best = 0;
        var run = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            var diagonal = 0;
            for (var j = 1; j <= right.Length; j++)
            {
                var previous = diagonal;
                diagonal = run[j];
                run[j] = previous + 1;
                if (left[i - 1] != right[j - 1]) run[j] = 0;
                if (run[j] > best) best = run[j];
            }
        }

        return best;
    }

    /// <summary>
    /// ③ 说明行里「」点出的名字，哪些不是这一屏<b>整串交出的</b>界面文字。
    /// <para>
    /// 认法是「同文件里另一处双引号字符串<b>整串等于</b>这个名字（两侧全角括号可有）」——列头
    /// <c>Columns.Add("原因", …)</c>、按钮文字 <c>Ui.Ghost("重新生成计划")</c>、单元格追加的标记
    /// <c>"（早于源码修改）"</c> 三种实物都满足；一句里嵌着同名子串不算（<c>"…（原因见结果列）…"</c>
    /// 那种状态行文字满足不了，见下）。注释里提一句也不算：注释不显示给用户。
    /// </para>
    /// <para>
    /// <b>为什么是整串相等而不是「含这个名字」</b>：M2 / M3 两发变异就是照着旧写法跑红的——旧写法只要求
    /// 含子串，而 <c>ApplyPlanDialog.cs</c> 的状态行字面「回写未完成，没有文件被写入（原因见结果列）。
    /// 请处理后重新生成计划。」同时含「原因」与「重新生成计划」两个子串，于是把那一列的列头改名、
    /// 把那颗按钮改名，说明行指的东西在界面上已经不存在了，这条门<b>照样绿</b>。用户照着那句话去找
    /// 「原因」那一列而它已改叫别的，正是这条门存在的理由，所以它必须钉住「那一格 / 那颗按钮上显示的就是这个名字」。
    /// </para>
    /// <para>
    /// <b>「不许自证」这一步为什么在本体里</b>：句子自己就是双引号字符串，不先把这一句抹掉再找，
    /// <c>「原因」</c> 会被它自己所在的说明行满足——那一屏把列头删掉、把按钮改名，这条照样绿。
    /// 第五发注入（<see cref="Gate_ReportsTheBreaksItMustCatch"/>）第一次跑就把这一步教在了调用方身上，
    /// 那是判点选错，故搬进来。
    /// </para>
    /// </summary>
    /// <param name="caption">那一屏的说明行文字。</param>
    /// <param name="source">那一屏的整份源码（本函数自己把 <paramref name="caption"/> 那一句抹掉）。</param>
    internal static List<string> UnresolvedQuotes(string caption, string source)
    {
        var text = source.Replace("\"" + caption + "\"", "\"\"", StringComparison.Ordinal);

        // 这一屏整串交出的界面文字：每条字面量去掉两侧全角括号后剩下的那个名字。
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match literal in Regex.Matches(text, "\"([^\"]*)\""))
        {
            emitted.Add(literal.Groups[1].Value.Trim('（', '）'));
        }

        var problems = new List<string>();
        foreach (Match match in Regex.Matches(caption, "「([^」]+)」"))
        {
            if (!emitted.Contains(match.Groups[1].Value))
            {
                problems.Add(match.Groups[1].Value);
            }
        }

        return problems;
    }

    // ---------- 三处落点各判一遍 ----------

    [Fact]
    public void TheThreeCaptions_AreStillThereAndSayTheirOwnMeaning()
    {
        foreach (var site in Sites)
        {
            var caption = CaptionOf(site);
            foreach (var phrase in site.MustSay)
            {
                Assert.True(
                    caption.Text.Contains(phrase, StringComparison.Ordinal),
                    $"{site.File}:{caption.Line} 那句说明行不再说自己该说的事（缺「{phrase}」）：{caption.Text}");
            }
        }
    }

    /// <summary>
    /// ① 模板骨架不得复活。注释也在扫描面内：注释里逐字引着旧句，下一轮就会照着它把旧句写回界面。
    /// </summary>
    [Fact]
    public void TemplateSkeleton_IsNotFilledTwice()
    {
        var hits = TemplateHits(Repo.NativeSources().Select(s => (s.Name, s.Text)));
        Assert.True(
            hits.Count == 0,
            "说明行又用回那个共用的括注骨架（一个形状背三种意思）：" + string.Join("、", hits));
    }

    /// <summary>
    /// ② 三句两两不共用一段 8 字以上的连续文字。
    /// 变异验证（本轮实测）：把 <c>SyncPreviewDialog</c> 那句改回旧句「未纳入计划（原因如下；处理后重新检查、
    /// 重新生成同步预览）」并把 <c>ApplyPlanDialog</c> 那句也改回旧句，这一条即红并报出共用那一段。
    /// </summary>
    [Fact]
    public void TheThreeCaptions_DoNotShareOneTemplate()
    {
        var captions = Sites.Select(CaptionOf).Select(c => (c.File, c.Text)).ToList();
        var shared = new List<string>();
        var measured = new List<string>();
        for (var i = 0; i < captions.Count; i++)
        {
            for (var j = i + 1; j < captions.Count; j++)
            {
                var run = LongestCommonRun(captions[i].Text, captions[j].Text);
                measured.Add($"{captions[i].File} ↔ {captions[j].File} = {run} 字");
                if (run >= MaxSharedRun)
                {
                    shared.Add($"{captions[i].File} ↔ {captions[j].File} 共用 {run} 字（阈值 {MaxSharedRun}）");
                }
            }
        }

        Assert.True(
            shared.Count == 0,
            "这几句说明行又是同一个模板填出来的：" + string.Join("、", shared) + "；实测 " + string.Join("，", measured));
    }

    /// <summary>
    /// ③ 每句里用「」点出来的名字，都得是那一屏自己写进界面的文字。
    /// <para>
    /// 这是「下一步在界面上真实存在」这条老纪律（第五十轮：提示语改指界面上真实存在的那个按钮）
    /// 在说明行上的落法：名字与列头 / 按钮 / 单元格标记同源，改了列头或按钮而说明行没跟上，这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryQuotedName_IsTextThatScreenItselfEmits()
    {
        foreach (var site in Sites)
        {
            var caption = CaptionOf(site);
            var unresolved = UnresolvedQuotes(caption.Text, Repo.Source("Desktop", "Native", site.File));
            Assert.True(
                unresolved.Count == 0,
                $"{site.File}:{caption.Line} 的说明行点了界面上没有交出的名字：" + string.Join("、", unresolved) +
                $"——「」里的名字必须与这一屏自己的某条列头 / 按钮 / 单元格文字整串相同（嵌在句子里的同名子串不算），否则用户在那一格里找不到它：{caption.Text}");
        }
    }

    // ---------- 判据自己被抓得住 ----------

    /// <summary>
    /// ④ 六发注入各报一处，再加两发「合规写法不得报」——否则这条门可能只是恰好在当前文本上绿。
    /// 全用夹具，不改磁盘。
    /// </summary>
    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        const string oldSkipped = "跳过的位置（原因如下；处理后重新生成计划）";
        const string oldExcluded = "未纳入计划（原因如下；处理后重新检查、重新生成同步预览）";
        const string newSkipped = "跳过的位置：这些注释位置本次不写回源码，每条为什么写在「原因」列；改完点「重新生成计划」。";

        // ① 骨架复活（两处各一处，逐文件点名）。
        var revived = TemplateHits([(nameof(oldSkipped), oldSkipped), (nameof(oldExcluded), oldExcluded)]);
        Assert.Equal(4, revived.Count);   // 两个片段各命中两句
        Assert.Contains(revived, h => h.Contains("oldExcluded", StringComparison.Ordinal));

        // ② 合规写法不报（本轮改后的那一句喂进去）。
        Assert.Empty(TemplateHits([("ApplyPlanDialog.cs", newSkipped)]));

        // ③ 两句同模板 → 长串判据抓到；本轮改后的两句 → 抓不到。
        Assert.True(LongestCommonRun(oldSkipped, oldExcluded) >= MaxSharedRun, "旧那两句本就同模板，这条判据却说它抓不到。");
        var current = Sites.Select(CaptionOf).Select(c => c.Text).ToList();
        Assert.True(LongestCommonRun(current[0], current[1]) < MaxSharedRun, $"改后的两句被判成同一模板（阈值 {MaxSharedRun}）。");

        // ④ 名字在这屏没交出 → 报出；这一屏自己整串交出的名字 → 不报。
        Assert.Equal(
            ["那一列名字"],
            UnresolvedQuotes(
                "跳过的位置：每条为什么写在「那一列名字」",
                "list.Columns.Add(\"原因\", 560); var b = Ui.Ghost(\"重新生成计划\");"));
        Assert.Empty(UnresolvedQuotes(
            "跳过的位置：每条为什么写在「原因」列；改完点「重新生成计划」。",
            "list.Columns.Add(\"原因\", 560); var b = Ui.Ghost(\"重新生成计划\");"));

        // ⑤ 不许自证：这一屏除了这一句自己，再没有第二处交出那个名字 → 一样算没交出。
        const string selfProving = "跳过的位置：每条为什么写在「原因」列";
        Assert.Equal(
            ["原因"],
            UnresolvedQuotes(selfProving, $"var caption = Ui.Caption(\"{selfProving}\");"));

        // ⑥ 名字只嵌在一句更长的话中间 → 一样算没交出。这一发是 M2 / M3 两发磁盘变异在旧判法
        //    （「含子串即可」）下漏掉的那两处：那一列改名、那颗按钮改名之后，界面上已经没有
        //    「原因」这一格与「重新生成计划」这一颗，可整个文件里两个词都还在——只是都在句子里。
        Assert.Equal(
            ["原因", "重新生成计划"],
            UnresolvedQuotes(
                newSkipped,
                "SetStatus(\"回写未完成，没有文件被写入（原因见结果列）。请处理后重新生成计划。\", UiTokens.BadText);"));
    }
}
