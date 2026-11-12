using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 行高预算的分档口径（<c>docs/DECISIONS/D2-行高预算按字号档给.md</c>，第二百三十六轮，简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：<c>ProfileFormLayout.MaxRowHeight(正文行高)</c> 从第一百四十八轮起是配置表单行高的
/// 唯一算式，而它<b>只认识一个字号档</b>。第二百三十四轮把超预算的行逐行量了四个数，量出来是两种根因：
/// 甲组是字段名折成两行（标签列不随字号放大，第二百三十五轮已修），乙组是分区标题<b>单行就超</b>——
/// 200% 档那一行用的是 Section 档（58px 的字 + 4+4 留白 = 66px），对只按正文算的预算（41 + 24 = 65px）差 1px。
/// 本文件判的是这一族的定案：<b>预算按行所属的字号档给，一档一条预算线，而算式仍只有一处</b>。
/// </para>
/// <para>
/// <b>为什么不止判「分档之后乙组过了」</b>：放宽一个预算线永远有第二种画法——把正文档那一档直接提到
/// 分区档的高度，乙组同样归零，而甲组那一族（这条预算线量出来的全部价值）会一起被放过。
/// 所以 <see cref="BodyBudget_RaisingItToTheSectionTier_WouldHideTheWrappedLabels"/> 拿两个实测数
/// （125% 档甲组折行 60px、分区档预算线 61px）判住那条路；
/// <see cref="UnrecognisedLineHeight_NeverGetsTheLooserBudget"/> 判住「换一个更大的字就顺手放宽自己那一行」。
/// </para>
/// <para>
/// <b>数从哪来</b>：正文的三档预算线取入库读数 <c>.winecheck/reading.txt</c> 的 <c>formbudget</c>（跑出来的，
/// 不是抄的），其余四个数（三档的字号行高与分区档预算线）取 <c>.winecheck/README.md</c>「修复后的三档
/// overbudget 读数」那一段念的数——出处是第二百三十六轮「重启前缀 + 单档跑」的三份报告原文。
/// 本文件因此同时是<b>算式与文档读数的对账</b>：文档念的预算线与代码算出来的不是同一个数即红。
/// 它<b>不判</b>真实控件的行高（那是冒烟器 ① 那一段在 wine 下量的），也不判真机（真机 0 台，G-D5 仍 <c>[ ]</c>）。
/// </para>
/// </summary>
public class ProfileFormLayoutBudgetTests
{
    /// <summary>三个缩放档位（<c>LogPixels</c>），顺序与两支演示脚本的缺省档位一致。</summary>
    private static readonly int[] TierDpis = [96, 120, 192];

    /// <summary>
    /// 每一档实测到的两个字号行高（像素，<b>当前缩放档上的</b>字体行高，不是 96dpi 的磅值换算）。
    /// 出处：<c>/tmp/ah-demo-&lt;档位&gt;/report-&lt;档位&gt;.txt</c> 里
    /// <c>DPI 上下文（新建配置对话框）：… 正文行高 Npx … 分区档预算线 Npx</c> 那一行（第二百三十六轮三跑）。
    /// </summary>
    private static readonly (int Tier, int Body, int Section)[] MeasuredLineHeights =
    [
        (96, 21, 29),
        (120, 26, 37),
        (192, 41, 58),
    ];

    /// <summary>分区标题那一行在三档实测到的行高（同一份报告的 <c>行高：</c> 那一行，三行分区标题同值）。</summary>
    private static readonly (int Tier, int RowHeight)[] MeasuredSectionRows = [(96, 37), (120, 44), (192, 66)];

    // ── ① 三条正文预算线：与入库读数逐字对账 ──────────────────────────

    /// <summary>
    /// 正文档那一档的预算线在三个档位上各是多少，且<b>等于读数记录里跑出来的那三个数</b>。
    /// </summary>
    [Fact]
    public void BodyBudget_LinesAreTheThreeRecordedReadings()
    {
        var recorded = RecordedBudgets();
        foreach (var (tier, body, _) in MeasuredLineHeights)
        {
            var budget = ProfileFormLayout.MaxRowHeight(body);
            Assert.True(recorded.TryGetValue(tier, out var reading),
                $"{tier} 档在 .winecheck/reading.txt 里没有 overbudget= 那一行：正文预算线没有跑出来的出处可对账。");
            Assert.True(reading == budget,
                $"{tier} 档读数记录里的 formbudget 是 {reading}px，而按该档实测正文行高 {body}px 算出来是 {budget}px："
                + "文档与读数念的预算与算式对不上（要么预算改了没复跑，要么这一档的行高读数换了来源）。");
        }

        // 三个档位各自一条预算线，不是「一个数套三档」：那一跑里 200% 档的预算比 100% 档宽 20px。
        var lines = TierDpis.Select(dpi => ProfileFormLayout.MaxRowHeight(MeasuredLineHeights.First(m => m.Tier == dpi).Body)).ToList();
        Assert.True(lines[0] < lines[1] && lines[1] < lines[2],
            $"三档的正文预算线不是递增的（{string.Join(" / ", lines)}）：字号随缩放档放大而预算不跟着走，就是乙组那个 1px 的成因。");
    }

    // ── ② 分区标题那一档：另有一条预算线，且量到的那一行落在里面 ──────

    /// <summary>
    /// 分区档的三条预算线<b>等于 README 那一段念的三个数</b>，且乙组那一行在三档的实测行高都在这条线内。
    /// </summary>
    [Fact]
    public void SectionBudget_IsALineOfItsOwn_AndHoldsEveryMeasuredTitleRow()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        var statedBody = ReadTriple(readme, @"正文 `(?<a>\d+)px / (?<b>\d+)px / (?<c>\d+)px`", "正文预算线");
        var statedSection = ReadTriple(readme, @"分区标题 `(?<a>\d+)px / (?<b>\d+)px / (?<c>\d+)px`", "分区档预算线");
        var statedRows = ReadTriple(readme, @"分区标题那一行实测 `(?<a>\d+)px / (?<b>\d+)px / (?<c>\d+)px`", "分区标题实测行高");

        for (var i = 0; i < TierDpis.Length; i++)
        {
            var (_, body, section) = MeasuredLineHeights[i];
            Assert.True(statedBody[i] == ProfileFormLayout.MaxRowHeight(body),
                $"{TierDpis[i]} 档 README 念的正文预算线是 {statedBody[i]}px，算式给的是 {ProfileFormLayout.MaxRowHeight(body)}px。");
            Assert.True(statedSection[i] == ProfileFormLayout.MaxRowHeight(section),
                $"{TierDpis[i]} 档 README 念的分区档预算线是 {statedSection[i]}px，算式给的是 {ProfileFormLayout.MaxRowHeight(section)}px："
                + "分档预算改了而文档没跟着复跑，或反过来。");

            // 乙组那一行（第二百三十五轮还判不过的那三行）这一档必须在自己的预算线内。
            var row = statedRows[i];
            Assert.True(row <= statedSection[i],
                $"{TierDpis[i]} 档分区标题那一行实测 {row}px，分区档预算线 {statedSection[i]}px：分档之后仍然判不过，"
                + "说明那一行不是「字号高一档」这一族，得回报告看是哪一格撑高的。");
            // 但它<b>不</b>在正文预算线内（200% 档），否则本轮就没有要收口的东西了——只在最高的那一档成立。
            if (TierDpis[i] == 192)
            {
                Assert.True(row > statedBody[i],
                    $"192 档分区标题那一行是 {row}px，正文预算线 {statedBody[i]}px：这一档已经量不到乙组那一族，"
                    + "上面那句「分档是为了救差 1px 的那一行」就没有读数支撑，本文件的引言需要改写。");
            }
        }
    }

    // ── ③ 为什么不能把正文档预算一提了之 ─────────────────────────────

    /// <summary>
    /// 把正文档预算提到分区档那一档，会<b>放过甲组那一族真正的长行</b>——那两个数正是量出根因的数。
    /// </summary>
    [Fact]
    public void BodyBudget_RaisingItToTheSectionTier_WouldHideTheWrappedLabels()
    {
        // 第二百三十四轮逐行量到的甲组折行：125% 档四行 60px（2 × 26 + 4 + 4）、200% 档七行 88px（2 × 41）。
        const int wrappedAt125 = 60;
        var body125 = ProfileFormLayout.MaxRowHeight(MeasuredLineHeights[1].Body);
        var section125 = ProfileFormLayout.MaxRowHeight(MeasuredLineHeights[1].Section);

        Assert.True(wrappedAt125 > body125,
            $"125% 档那四行折成 {wrappedAt125}px 而正文预算线是 {body125}px：今天的预算判不出甲组，本条判据的前提就没了。");
        Assert.True(wrappedAt125 <= section125,
            $"分区档在 125% 档的预算线是 {section125}px，而甲组折行是 {wrappedAt125}px：一提档就判不出——"
            + "「统一把正文预算提到分区档那一档」这条路会把量出甲组根因的那把尺子废掉。");

        // 200% 档不是同一个形状：88px 折行连分区档的预算线也超（如实记，不写成「提档只在 125% 有害」）。
        var section200 = ProfileFormLayout.MaxRowHeight(MeasuredLineHeights[2].Section);
        Assert.True(88 > section200,
            $"200% 档的折行 88px 对分区档预算线 {section200}px：这一档提档也会放过，本条断言的那一句要改。");
    }

    // ── ④ 认档的口径：放宽必须由登记驱动 ─────────────────────────────

    /// <summary>
    /// 认不出来的字号一律按正文档算：<b>换一个更大的字不能顺手把自己那一行的预算放宽</b>。
    /// </summary>
    [Fact]
    public void UnrecognisedLineHeight_NeverGetsTheLooserBudget()
    {
        const int body = 41;
        const int section = 58;

        // 每一行都点名「哪一个字号认成了哪一档、那样会发生什么」：裸的 Values differ 在失败那天念不出少了什么。
        void AssertTier(int lineHeight, ProfileFormLayout.RowFontTier want, string why)
        {
            var got = ProfileFormLayout.TierOf(lineHeight, body, section);
            Assert.True(got == want,
                $"{lineHeight}px 的字号行高认档认成了 {got}，应为 {want}：{why}"
                + $"（正文 {body}px、分区标题 {section}px）");
        }

        AssertTier(section, ProfileFormLayout.RowFontTier.Section, "登记在册的那一档认不出，乙组那一族就回到差 1px 判不过");
        AssertTier(body, ProfileFormLayout.RowFontTier.Body, "正文档被认成了更宽的一档，等于本轮把整张表的预算提了档");
        AssertTier(35, ProfileFormLayout.RowFontTier.Body, "提示行那一族（Caption 档）跟着放宽，D2 §4 第三条那笔代价就从读数里消失了");
        AssertTier(49, ProfileFormLayout.RowFontTier.Body, "介于两档之间的字号拿到分区预算，放宽就不再由登记驱动");
        AssertTier(72, ProfileFormLayout.RowFontTier.Body, "比两档都高的字号拿到更宽的预算，加一个更大的字就能绕过行高判定");
        AssertTier(0, ProfileFormLayout.RowFontTier.Body, "量不到字号的空行拿到了放宽的预算");
        AssertTier(-section, ProfileFormLayout.RowFontTier.Body, "负数行高被当成登记过的档");

        // 认不出的一律拿正文预算，与 MaxRowHeightFor 同一个口径。
        var forUnknown = ProfileFormLayout.MaxRowHeightFor(72, body, section);
        Assert.True(forUnknown == ProfileFormLayout.MaxRowHeight(body),
            $"认不出的字号（72px）走 MaxRowHeightFor 得到 {forUnknown}px，正文预算是 {ProfileFormLayout.MaxRowHeight(body)}px："
            + "取档与取预算两处各说了一套。");
        var forSection = ProfileFormLayout.MaxRowHeightFor(section, body, section);
        Assert.True(forSection == ProfileFormLayout.MaxRowHeight(section),
            $"分区标题档（{section}px 的字）走 MaxRowHeightFor 得到 {forSection}px，应为 {ProfileFormLayout.MaxRowHeight(section)}px："
            + "本轮落地的就是这一档另给一条预算线，它不生效等于修法没做。");

        // 两档被改成同一个行高时，分区档不再提供任何放宽：放宽是一次显式登记，不是两档相等的后果。
        var equalTiers = ProfileFormLayout.TierOf(58, 58, 58);
        Assert.True(equalTiers == ProfileFormLayout.RowFontTier.Body,
            $"两档行高被改成同一个数（58px）时认档认成了 {equalTiers}：分区档一旦不比正文高就不再是「高一档」，"
            + "此时还给它更宽的预算，就是拿一个不存在的档位给整张表放行高。");
        Assert.True(ProfileFormLayout.MaxRowHeightFor(58, 58, 58) == ProfileFormLayout.MaxRowHeight(58),
            "两档相等时 MaxRowHeightFor 与 MaxRowHeight 不再是同一个数：放宽的入口多了一处。");
    }

    /// <summary>
    /// 登记在册的字号档是<b>封闭的两档</b>，提示行那一族（Caption）故意不在里面。
    /// </summary>
    [Fact]
    public void RegisteredTiers_AreExactlyTwo_AndTheCaptionTierIsDeliberatelyOutside()
    {
        var tiers = (ProfileFormLayout.RowFontTier[])Enum.GetValues(typeof(ProfileFormLayout.RowFontTier));
        var registered = tiers.OrderBy(t => (int)t).ToArray();
        var want = new[] { ProfileFormLayout.RowFontTier.Body, ProfileFormLayout.RowFontTier.Section };
        Assert.True(registered.SequenceEqual(want),
            $"登记在册的字号档现在是「{string.Join("、", registered)}」，应为「{string.Join("、", want)}」："
            + "多一档就是又一处放宽（每加一档，行高判定就多一个可以躲进去的高度），少一档就是乙组那一族回到 1px 判不过。"
            + "要改这一行请先改 docs/DECISIONS/D2——放宽的入口只许由决定项开。");

        // Caption 档不进预算表的那句理由必须在口径文件里留着（删掉它，下一轮就会有人「顺手补上第三档」）。
        var layout = Repo.Source("Desktop", "Native", "ProfileFormLayout.cs");
        Assert.Contains("Caption", layout, StringComparison.Ordinal);
        Assert.Contains("1086", layout, StringComparison.Ordinal);
        Assert.Contains("966", layout, StringComparison.Ordinal);
    }

    // ── ⑤ 算式只有一处，冒烟器逐行取档 ───────────────────────────────

    /// <summary>
    /// 行高预算的算式在 <c>ProfileFormLayout.cs</c> 里<b>只写一次</b>，而冒烟器逐行按档取它。
    /// </summary>
    [Fact]
    public void BudgetFormula_IsWrittenOnce_AndTheSmokeReadsItPerRow()
    {
        var layout = Repo.Source("Desktop", "Native", "ProfileFormLayout.cs");
        void AssertJustOne(string pattern, string what, string consequence)
        {
            var hits = Regex.Matches(layout, pattern).Count;
            Assert.True(hits == 1,
                $"{what} 在 ProfileFormLayout.cs 里出现 {hits} 处（样式 {pattern}），应为恰一处：{consequence}");
        }

        AssertJustOne(@"\+ RowPadding", "行高预算那条算式",
            "抄成第二份之后，改留白会只改到一半，而读数记录里那两个预算数会自己打架（ReadingConsistencyTests 判的就是它俩相等）");
        AssertJustOne(@"public static int MaxRowHeight\(", "预算算式的声明",
            "答辩口径「算式只有一处 MaxRowHeight」指着这一个签名，多一个签名那句话就悬空");
        AssertJustOne(@"lineHeight == sectionLineHeight", "认档的那一句",
            "MaxRowHeightFor 是 TierOf 的调用方、不是第二处认档；两处各认一套，两档的边界就会随改的人漂");

        var smoke = Repo.SmokeProgram;
        Assert.Contains("ProfileFormLayout.MaxRowHeightFor(measured,", smoke, StringComparison.Ordinal);
        Assert.Contains("var measured = TallestControlInRow(grid, row)?.Font.Height ?? 0;", smoke, StringComparison.Ordinal);

        // ① 那一段里不许退回「整张表比一个 limit」：那是本轮改掉的旧判据，也是乙组那 1px 的成因。
        // （⑧ 提示行那一段仍比 limit，那是正文档那一条预算线，本轮不动——见下面那一条断言。）
        var formRows = ActionSlice(smoke, "配置表单：新建对话框的行高没有死空白");
        Assert.True(!formRows.Contains("heights[row] > limit", StringComparison.Ordinal),
            "冒烟器 ① 段又拿整张表共用的 limit 比每一行了：乙组那一族（分区标题单行 66px）会重新判不过。");
        Assert.Contains("heights[row] > rowLimit", formRows, StringComparison.Ordinal);

        // 提示行那一段的阈值仍取正文档那一条预算线（本轮不放宽 Caption 档，D2 §4 第三条）。
        Assert.Contains("ProfileFormLayout.MaxRowHeight(UiTokens.Body.Value.Height)", smoke, StringComparison.Ordinal);
        Assert.Contains("heights[row] > limit", ActionSlice(smoke, "配置表单：四个子对话框的提示行没有死空白"), StringComparison.Ordinal);
    }

    // ── 取数：入库读数 + README 那一段 ───────────────────────────────

    /// <summary>
    /// 冒烟器里某一个 <c>RunAction</c> 段的正文（从它的标题到下一个 <c>RunAction("</c> 之前）。
    /// 本文件两条反向断言必须<b>只作用在自己那一段</b>：<c>heights[row] &gt; limit</c> 在 ① 段是旧的错形状、
    /// 在 ⑧ 段是本轮明确不动的对的形状，整份源码扫描会把两者混成一件事。
    /// </summary>
    private static string ActionSlice(string smoke, string actionName)
    {
        var start = smoke.IndexOf($"RunAction(\"{actionName}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ApifoxHelper.Smoke/Program.cs 里找不到段「{actionName}」：本轮改的那一族判据没有落点了。");
        var end = smoke.IndexOf("RunAction(\"", start + 10, StringComparison.Ordinal);
        Assert.True(end > start, $"段「{actionName}」之后切不出下一个 RunAction 的边界。");
        return smoke[start..end];
    }

    /// <summary>
    /// 入库读数里每一档的 <c>formbudget</c>（跑出来的那一个数，与 <c>hintbudget</c> 的相等关系由
    /// <c>ReadingConsistencyTests</c> 判，本文件只取正文那一个）。
    /// </summary>
    private static Dictionary<int, int> RecordedBudgets()
    {
        var text = Repo.Source(".winecheck", "reading.txt");
        var budgets = new Dictionary<int, int>();
        foreach (Match match in Regex.Matches(text, @"^overbudget=tier=(?<tier>\d+) form=\d+ formbudget=(?<px>\d+)px", RegexOptions.Multiline))
        {
            budgets[int.Parse(match.Groups["tier"].Value)] = int.Parse(match.Groups["px"].Value);
        }

        Assert.True(budgets.Count == TierDpis.Length,
            $".winecheck/reading.txt 里的 overbudget= 有 {budgets.Count} 档（{string.Join(" / ", budgets.Keys)}），"
            + $"应为 {string.Join(" / ", TierDpis)} 三档：正文预算线没有可对的数，先跑一次 bash .winecheck/demo.sh。");
        return budgets;
    }

    /// <summary>从 README 那一段取「A / B / C」三个数（按 96 / 120 / 192 的顺序）。</summary>
    private static List<int> ReadTriple(string readme, string pattern, string what)
    {
        var match = Regex.Match(readme, pattern);
        Assert.True(match.Success,
            $".winecheck/README.md「修复后的三档 overbudget 读数」那一段里没有{what}的三个数（样式 {pattern}）："
            + "本文件与文档共用的那一组数没有落点了。");
        return new[] { "a", "b", "c" }.Select(key => int.Parse(match.Groups[key].Value)).ToList();
    }
}
