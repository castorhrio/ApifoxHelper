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
/// ② 登记表里每两句两两不共一段 8 字以上的连续文字（<see cref="TheRegisteredCaptions_DoNotShareOneTemplate"/>）——
/// 这是「同一个模子填两次」在文字上唯一可机械测的痕迹；③ <b>说明行里用「」点出来的名字，必须是这一屏
/// 整串交出的那段界面文字</b>（<see cref="EveryQuotedName_IsTextThatScreenItselfEmits"/>）：列头、按钮文字、
/// 或单元格追加的标记，<b>整串相等</b>才算，嵌在句子里的同名子串不算。这一条是这几句真正的新增判据，
/// 也是它凭什么比旧句更可用——旧句说「原因如下」「处理后重新生成计划」，而这一屏的按钮就叫「重新生成计划」、
/// 那一列就叫「原因」，指名字不指概念，用户才点得着。<b>「整串相等」这一格是变异教出来的</b>：M2 / M3
/// 两发（把那一列的列头改名、把那颗按钮改名）在「含子串」那一版判法下<b>照样绿</b>——同一文件的状态行字面
/// 「…（原因见结果列）。请处理后重新生成计划。」把两个名字都含住了；界面已经找不到那一格，门却不说。
/// ④ 判据自己被抓得住（<see cref="Gate_ReportsTheBreaksItMustCatch"/>，注入各报一处、合规写法不报）；
/// ⑤ 登记表与源码两向对账（<see cref="Sites_CoverEveryScreenThatEmitsTheResourceHeader"/>）：谁在交出
/// 「资源」那个名称格表头、谁的一句讲到它，两侧各数一次，第四张清单或那一句讲的不是那一格都当场红。
/// </para>
/// <para>
/// <b>范围如实限定</b>（第二百七十七轮把这一段改对，简报 P0-1 的 1 号支：<b>纯注释，一条断言未改</b>）：
/// <b>①是全原生界面判</b>——它扫 <see cref="Repo.NativeSources"/>，注释也在扫描面里。
/// <b>②与③都只判 <see cref="Sites"/> 里登记的那几处</b>（第 4 站点名的那三句 + 第 7 站 ① 支落地的
/// 那两句，第二百七十八轮起），都不是全原生界面；
/// 本注释此前写着「①与③倒是全原生界面判」，那半句③与
/// <see cref="EveryQuotedName_IsTextThatScreenItselfEmits"/> 的实际扫描面对不上——这一处是
/// <c>docs/CRITIQUE.md</c> 第 7 站「去向」段在第二百七十六轮现读到、本轮收口的。
/// 全原生界面 40 多处说明行不在这里，两句各自为什么：
/// <b>②</b>——两句不同屏的说明行共用一个八字短语未必是模板脸（「重新检查后生效」这类跨屏事实本来就
/// 同一句话），把②铺到全部说明行会变成一条要求人改词的门，而不是判据。
/// <b>③</b>——它判的是「那句话点出的名字，这一屏整串交得出」，而<b>哪一句在指界面上的某个东西</b>是
/// 每一屏自己的决定：<see cref="Sites"/> 那一行登记的正是这件事（文件 / 那句的开头 / 它必须说到的意思）。
/// 其余那些只讲事实、不指着某一列或某颗按钮的说明行没有可交出的名字，把③铺过去等于替它们编造判据，
/// 还会逼人在句子里塞「」好让门有东西可判——与②铺到全部说明行是同一个坑。
/// <b>③长出新落点的那一次已经做了</b>（第二百七十八轮，<c>docs/CRITIQUE.md</c> 第 7 站「去向」段 ① 支）：
/// 装「资源」名称格的那几张清单各有了一句「这一格交出的是什么身份」，<b>补说明行与把它按本门自己的形状
/// 登记进 <see cref="Sites"/> 是同一次做的</b>——第二百五十六轮那一族停的是「为已有判据找恒等式」，
/// 而这一族的对账判的是被承诺的实物，所以它照常随实物一起长：<b>登记表 ↔ 源码表头两个方向由
/// <see cref="Sites_CoverEveryScreenThatEmitsTheResourceHeader"/> 对账</b>，第四张「资源」清单出现而
/// 本表没跟上、或本表登着一句而那一屏早已不交出那个列头，都当场红。登记不在本表里而只在界面上出现
/// 的说明行，本门的③照旧判不到——这正是第二百七十七轮那两发对照变异量出来的形状。
/// <b>那两发是量出来的，不是推的</b>：同一个假名字「那一列名字」注进
/// <c>SyncPreviewDialog.cs</c> 里当时<b>没登记</b>的那句「写入清单…」，本门与 <see cref="CaptionBrevityTests"/>
/// 十三条照旧全绿；注进同一屏<b>已登记</b>的那句「未纳入计划…」，当场红在本条上并点名文件与行号
/// （两发之后源文件以备份覆盖还原，<c>sha1sum</c> 逐字节相同）。<b>那一句「没登记」的落点从第二百七十八轮
/// 起已经登记进本表</b>，同一发注入今天会红——这条历史读数说的是当时那一版登记表。
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
    /// 落点登记表：<b>文件</b>、那一屏说明行的<b>开头</b>（用它在这屏全部说明行里认出那一句）、
    /// 那句话必须说到的<b>自己的意思</b>。
    /// <para>
    /// 前三处是第 4 站点名的那三句（第二百零五轮记、第二百七十四轮改），后两处是第 7 站「去向」段
    /// ① 支落地的那两句（第二百七十八轮）：交出「资源」名称格的那两屏各有一句讲清那一格是什么身份
    /// ——同一屏的第二张清单（未纳入清单）本轮按实测撤回，理由写在 <c>docs/NOTES.md</c> 那一轮的读数里。
    /// 这张表该覆盖到哪些屏不再靠人记得——它与源码表头两向对账，见
    /// <see cref="Sites_CoverEveryScreenThatEmitsTheResourceHeader"/>。
    /// </para>
    /// </summary>
    private sealed record Site(string File, string Prefix, string[] MustSay);

    private static readonly Site[] Sites =
    [
        new("ApplyPlanDialog.cs", "跳过的位置", ["这些注释位置本次不写回源码", "原因", "重新生成计划"]),
        new("SyncPreviewDialog.cs", "未纳入计划", ["为什么没进这份计划", "未纳入原因", "重新选择接口"]),
        new("PropagationDialog.cs", "XML 文档文件", ["旧生成物", "不算注释已生效"]),
        new("SyncPreviewDialog.cs", "写入清单", ["「资源」那格是方法加路径", "model: 名"]),
        new("DecisionsDialog.cs", "「资源」", ["接口那一行是方法与路径", "模型那一行是模型自己的名字"]),
    ];

    // ---------- 取登记表里那几句（经既有提取口径，不开第二份提取器） ----------

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

    /// <summary>
    /// 登记表 ↔ 源码表头的判定本体（纯函数：两侧名单进、问题清单出，注入自测直接喂夹具）。
    /// <para>
    /// <paramref name="emitting"/> = 此刻交出 <c>Columns.Add("资源", …)</c> 的那些文件名；
    /// <paramref name="registered"/> = 登记表里每一句的（所在文件，那句有没有讲到「资源」）。
    /// 正向缺一句 → 报那个文件；反向多一句（那一屏已经不交出那个列头）→ 报那一句。
    /// </para>
    /// </summary>
    internal static List<string> ResourceHeaderBreaks(
        IReadOnlyList<string> emitting,
        IReadOnlyList<(string File, bool MentionsResource)> registered)
    {
        var problems = new List<string>();

        foreach (var name in emitting)
        {
            if (!registered.Any(r => r.File == name && r.MentionsResource))
            {
                problems.Add($"{name}：那一屏交出「资源」名称格，登记表里却没有一句讲到它");
            }
        }

        foreach (var (file, mentions) in registered)
        {
            if (mentions && !emitting.Contains(file, StringComparer.Ordinal))
            {
                problems.Add($"{file}：登记的那句讲到「资源」，可这一屏已经不交出那个列头");
            }
        }

        return problems;
    }

    // ---------- 登记表里的落点各判一遍 ----------

    [Fact]
    public void TheRegisteredCaptions_AreStillThereAndSayTheirOwnMeaning()
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
    public void TheRegisteredCaptions_DoNotShareOneTemplate()
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

    /// <summary>
    /// 登记表 ↔ 源码表头<b>双向对账</b>（第二百七十八轮，第 7 站 ① 支落地时同次立）。
    /// <para>
    /// <b>补的是哪个缺口</b>：本门的③只看 <see cref="Sites"/>，而第 7 站那一句「界面上没有任何一处说明
    /// 『资源』就是那一串身份」之所以能挂六十八轮，一半原因是没有人、也没有门说得清<b>哪几张清单在交出
    /// 那个列头</b>。这一条把它变成从源码读出来的事实：<b>正向</b>——每一个交出
    /// <c>Columns.Add("资源", …)</c> 的文件，本表必须至少登着它的一句，而那一句必须真的讲到「资源」；
    /// <b>反向</b>——本表里凡是讲到「资源」的那几句，它所在的那个文件此刻必须还交出那个列头，否则那条
    /// 登记指着一个已经不存在的表头。第四张「资源」清单出现而本表没跟上，屏幕上那一格照旧没人讲，
    /// 而本门③不会红——这一条补的就是这一发。
    /// </para>
    /// <para>
    /// <b>它凭什么不算第二百五十六轮停住的那一族</b>：那一族停的是「为已有判据找恒等式」（判据判自己）；
    /// 这一条判的是<b>被承诺的实物 ↔ 表里的每一项</b>，与 <c>IdentityHeaderNamesTests</c> 同一形状，
    /// 按那一行的口径「照常随实物一起长」。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那一格在运行时显示的是接口还是模型（要判那个得逐屏追数据流，猜错的方向是
    /// 「绿着说没有」——那一截已由 <c>IdentityHeaderNamesTests</c> 登记为判不到）；也不判别的名称格，
    /// 它只钉「资源」这一族，因为这一族是第 7 站点名欠着的那一族。
    /// </para>
    /// </summary>
    [Fact]
    public void Sites_CoverEveryScreenThatEmitsTheResourceHeader()
    {
        const string HeaderCall = "Columns.Add(\"资源\"";

        var emitting = Repo.NativeSources()
            .Where(s => s.Text.Contains(HeaderCall, StringComparison.Ordinal))
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        // 地板：取法失效时两支都会静默空转（比没有断言更糟）。实物是同步预览那两张清单与
        // 差异确认那一张，落在两个文件里。
        Assert.True(emitting.Count >= 2,
            $"只从原生源码认到 {emitting.Count} 个交出「资源」名称格表头的文件（地板 2）："
            + "取法可能已失效，这一条就成了一扇空门。");

        var captions = AllCaptions();
        var registered = Sites
            .Select(site => (
                site.File,
                MentionsResource: captions.Any(c =>
                    c.File == site.File
                    && c.Text.StartsWith(site.Prefix, StringComparison.Ordinal)
                    && c.Text.Contains("「资源」", StringComparison.Ordinal))))
            .ToList();

        var problems = ResourceHeaderBreaks(emitting, registered);
        Assert.True(problems.Count == 0,
            "登记表与源码的「资源」名称格对不上：" + string.Join("；", problems)
            + "。正向漏的那一屏，用户在那一格里看到的既不是列头名也不是任何解释，而本门的③只看登记表、"
            + "不会替它红；反向多的那一句，要么列头改了名（第 7 站 ② 支落地，登记表得同次跟着改），"
            + "要么这一句本身该跟着撤。");
    }

    // ---------- 判据自己被抓得住 ----------

    /// <summary>
    /// ④ 每一发注入各报一处，再加两发「合规写法不得报」——否则这条门可能只是恰好在当前文本上绿。
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

        // ③ 两句同模板 → 长串判据抓到；本轮改后的每一对都 → 抓不到。
        Assert.True(LongestCommonRun(oldSkipped, oldExcluded) >= MaxSharedRun, "旧那两句本就同模板，这条判据却说它抓不到。");
        var current = Sites.Select(CaptionOf).Select(c => c.Text).ToList();
        for (var i = 0; i < current.Count; i++)
        {
            for (var j = i + 1; j < current.Count; j++)
            {
                Assert.True(
                    LongestCommonRun(current[i], current[j]) < MaxSharedRun,
                    $"登记表里第 {i} 与第 {j} 句被判成同一模板（阈值 {MaxSharedRun}），而界面上它们讲的是两件事。");
            }
        }

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

        // ⑦ 对账判定本体（夹具，不改磁盘）：第四张交出「资源」列头的清单没登记 → 报；
        //    登记表里那一句指着一张已经不交出该列头的屏 → 也报；两侧齐 → 不报。
        var emittingNow = new[] { "SyncPreviewDialog.cs", "DecisionsDialog.cs" };
        var registeredNow = new[]
        {
            ("ApplyPlanDialog.cs", false),
            ("SyncPreviewDialog.cs", true),
            ("PropagationDialog.cs", false),
            ("DecisionsDialog.cs", true),
        };
        Assert.Empty(ResourceHeaderBreaks(emittingNow, registeredNow));

        var fourthScreen = emittingNow.Append("NewResourceListDialog.cs").ToList();
        Assert.Equal(
            ["NewResourceListDialog.cs：那一屏交出「资源」名称格，登记表里却没有一句讲到它"],
            ResourceHeaderBreaks(fourthScreen, registeredNow));

        var headerRenamed = new[] { ("SyncPreviewDialog.cs", true), ("DecisionsDialog.cs", true), ("ApplyPlanDialog.cs", true) };
        Assert.Equal(
            ["ApplyPlanDialog.cs：登记的那句讲到「资源」，可这一屏已经不交出那个列头"],
            ResourceHeaderBreaks(emittingNow, headerRenamed));

        // 登记了那一句、可那句里根本没提那一格 → 正向仍算漏（这一发对着「补了行没登记」之外的那一支：
        // 登记在表里而那句话讲的是别的事）。
        Assert.Equal(
            ["DecisionsDialog.cs：那一屏交出「资源」名称格，登记表里却没有一句讲到它"],
            ResourceHeaderBreaks(emittingNow, [("SyncPreviewDialog.cs", true), ("DecisionsDialog.cs", false)]));
    }
}
