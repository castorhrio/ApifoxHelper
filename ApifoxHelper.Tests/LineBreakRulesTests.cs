using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 行首行尾禁则的<b>源码侧那一半</b>（DESIGN.md §3「行首行尾禁则的字表」、§6「中文排版」第 ② 条）。
/// <para>
/// <b>补的是哪个缺口</b>：清单 A 的「行首禁则」此前整条挂在「渲染期属性、只有真机字体度量给得出」上，
/// 于是源码侧完全裸奔。可这条承诺有两半：<b>折行造出来的行首</b>（渲染期，导出 HTML 那一份由
/// <c>HtmlWrapTests</c> 交给真浏览器量、样式声明由 <c>ReportTypographyTests</c> 钉住）与
/// <b>一段话自己的第一个字</b>（源码期，与折行无关）。后一半判得了，而且判的正是最难看的那种缺陷：
/// 一句交付给用户的文案以「，」「。」「」」起头，或以「（」「「」收尾——那不是折行折坏的，
/// 是写的时候就把标点写在了句子的边界上。
/// </para>
/// <para>
/// <b>判什么（「整句字面量」的起止字）</b>：只判<b>整句</b>——一个字面量的前一个非空白字符属于起句上下文
/// （<c>=</c> <c>(</c> <c>,</c> <c>:</c> 或行首）、后一个非空白字符属于收句上下文（<c>)</c> <c>;</c> <c>,</c>
/// 或行尾）时，它才是一段话的第一个字与最后一个字。拼在 <c>+</c> 两侧的片段不判：那些字不是句子边界，
/// 把它们判成违例等于把仪器调成瞎报（DESIGN.md §3 那句「一段话的第一个字与最后一个字不是折出来的」同一读法）。
/// 这条取法不是拍脑袋定的：本仓真实源码里 <c>ApifoxCli.ExitMeaning</c> 的 <c>1 =&gt; "，操作/API 失败"</c>
/// 与 <c>DraftService</c> 三元分支里的 <c>$"，异常分支 …"</c> 都是<b>拼在别处</b>的片段，
/// 按「字面量首字」判会当场假红六处——那六处正是这条门第一版被自己打回的地方（见
/// <see cref="Detector_ReportsTheBreaksItMustCatch"/> 的第 ④ ⑤ 发）。
/// </para>
/// <para>
/// <b>两侧对账</b>（形状照 <c>FontStackTests</c> / <c>ServiceCopyTests</c>）：口径侧是
/// <see cref="CopyRules.LineStartForbidden"/> / <see cref="CopyRules.LineEndForbidden"/> 那两张字表
/// （DESIGN.md §3 点名它们是唯一落点、渲染期那一半也取同一份），实物侧是两处交付面的整句字面量——
/// 界面层 <c>Desktop/Native</c> 与服务层用户可见文案（<c>UserException</c> 消息、<c>Describe()</c> 的失败描述、
/// 被界面引用的文案常量，与 <c>ServiceCopyTests</c> 同一份扫描面）。字表被抄成第二份、或哪一处交付面
/// 悄悄不再被判，都在 <see cref="ForbiddenSets_AreDeclaredOnceAndTheDesignDocNamesThem"/> 与
/// <see cref="Scan_IsNotSilentlyEmpty"/> 上红。
/// </para>
/// <para>
/// <b>不判什么</b>：不判折行（那一半在 <c>HtmlWrapTests</c> / <c>ReportTypographyTests</c>，随 G-D5 真机复核）；
/// 不判全角标点与中英文空隙（<c>TypographyTests</c> / <c>ServiceCopyTests</c> 已有，不在这里叠第二套）；
/// 不判正文行长（渲染期属性）。按第二百八十二轮那五步判：① 断言里出现的名字是
/// <c>CopyRules.LineStartForbidden</c> / <c>LineEndForbidden</c>（口径文件）与两处交付面的源码文本
/// （在口径文件之外）；② 后者不在被它判的那个对象里，答「不是」；③ 按第 ③ 步当<b>对账门</b>处理，
/// 不属第二百五十六轮收口那一族，照常随实物一起长。
/// </para>
/// </summary>
public class LineBreakRulesTests
{
    /// <summary>起句上下文：字面量前面是这些字符时，它的第一个字就是一段话的第一个字。</summary>
    private const string StartContexts = "=(,:";

    /// <summary>收句上下文：字面量后面是这些字符时，它的最后一个字就是一段话的最后一个字。</summary>
    private const string EndContexts = ");,";

    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"");

    /// <summary>一条被判的整句字面量：出处 + 还原后的文案 + 它是不是句首 / 句尾。</summary>
    internal readonly record struct Sentence(string Source, int Line, string Text, bool StartsSentence, bool EndsSentence);

    // ── 判定本体（纯函数：字面量进，问题出）────────────────────────────

    /// <summary>
    /// 一行源码里的整句字面量（<see cref="CopyRules.LiteralText"/> 还原成「用户会读到的那几个字」，
    /// 剥掉插值空洞并解转义——与 <c>TypographyTests</c> 同一份还原口径，不另写一套）。
    /// </summary>
    internal static IEnumerable<Sentence> Sentences(string source, string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in Literal.Matches(lines[i]))
            {
                var copy = CopyRules.LiteralText(match.Groups[1].Value);
                if (!CopyRules.HasChinese(copy)) continue;

                var before = lines[i][..match.Index].TrimEnd();
                var after = lines[i][(match.Index + match.Length)..].TrimStart();
                var starts = before.Length == 0 || StartContexts.Contains(before[^1]);
                var ends = after.Length == 0 || EndContexts.Contains(after[0]);
                yield return new Sentence(source, i + 1, copy, starts, ends);
            }
        }
    }

    /// <summary>
    /// 判定本体：句首不得是禁则字、句尾不得是开号。返回的每一句都念成人话（出处 + 那一句 + 哪个字）。
    /// </summary>
    internal static List<string> Problems(IEnumerable<Sentence> sentences)
    {
        var problems = new List<string>();
        foreach (var sentence in sentences)
        {
            if (sentence.StartsSentence && CopyRules.LineStartForbidden.Contains(sentence.Text[0]))
            {
                problems.Add($"{sentence.Source}:{sentence.Line}:「{sentence.Text}」以禁则字起句（首字：{sentence.Text[0]}）"
                    + "——一段话的第一个字不是折出来的，这个标点是被写在了句子边界上（DESIGN.md §3「行首行尾禁则的字表」）。");
            }

            if (sentence.EndsSentence && CopyRules.LineEndForbidden.Contains(sentence.Text[^1]))
            {
                problems.Add($"{sentence.Source}:{sentence.Line}:「{sentence.Text}」以开号收句（末字：{sentence.Text[^1]}）"
                    + "——开号后面没有字，读起来像漏了一句（DESIGN.md §3「行首行尾禁则的字表」）。");
            }
        }

        return problems;
    }

    // ── 两处交付面 ───────────────────────────────────────────────────

    private static List<Sentence> InterfaceSentences() =>
        Repo.NativeSources().SelectMany(s => Sentences(s.Name, s.Text)).ToList();

    private static List<Sentence> ServiceSentences() =>
        Repo.ServiceSources().SelectMany(s => Sentences(s.Relative, s.Text)).ToList();

    /// <summary>
    /// 界面层（<c>Desktop/Native</c>）：整句字面量的起止字不得落在禁则字表上。
    /// 这一面是用户每天读的那一屏——状态行、说明行、按钮文字、空态与失败态那几句话。
    /// </summary>
    [Fact]
    public void InterfaceCopy_KeepsForbiddenMarksOffTheSentenceEdges()
    {
        var problems = Problems(InterfaceSentences());
        Assert.True(problems.Count == 0,
            $"界面文案有 {problems.Count} 处行首行尾禁则违例：\n  " + string.Join("\n  ", problems)
            + "\n口径：句首不得是「" + CopyRules.LineStartForbidden + "」里的字，句尾不得是「"
            + CopyRules.LineEndForbidden + "」里的字（DESIGN.md §3、§6「中文排版」第 ② 条）。"
            + "\n拼在 + 两侧的片段不判——那些字不是句子边界。");
    }

    /// <summary>
    /// 服务层用户可见文案：这些字会原样进原生界面的状态行与内联错误（「读取项目列表失败：」那一类），
    /// 错误路径上的那一句恰恰是用户最需要读懂的——只判界面层等于「界面合规、错误路径不合规」。
    /// </summary>
    [Fact]
    public void ServiceCopy_KeepsForbiddenMarksOffTheSentenceEdges()
    {
        var problems = Problems(ServiceSentences());
        Assert.True(problems.Count == 0,
            $"服务层用户可见文案有 {problems.Count} 处行首行尾禁则违例：\n  " + string.Join("\n  ", problems)
            + "\n这些字会原样进界面状态行与内联错误，口径与界面层同一份（DESIGN.md §3「中文排版附加条款」的扫描范围）。");
    }

    /// <summary>
    /// 扫描面不能悄悄变空：两处交付面各自取到的整句字面量掉到地板以下，说明取法坏了，
    /// 而不是文案变干净了（本仓那句老话：空集合对空集合是绿的）。
    /// <para>
    /// 地板取的是本轮实测数往下留余量：界面层 837 句、服务层 334 句（第三百一十一轮现读）。
    /// 地板不是「应该有多少句」，是「取法还活着」的下限——文案增删几十句不该让它红。
    /// </para>
    /// </summary>
    [Fact]
    public void Scan_IsNotSilentlyEmpty()
    {
        var interfaceSentences = InterfaceSentences();
        var serviceSentences = ServiceSentences();

        Assert.True(interfaceSentences.Count >= 700,
            $"界面层只取到 {interfaceSentences.Count} 句整句字面量（地板 700）。取法或 Desktop/Native 的读取可能已失效。");
        Assert.True(serviceSentences.Count >= 250,
            $"服务层只取到 {serviceSentences.Count} 句整句字面量（地板 250）。取法或 Repo.ServiceDirectories 可能已失效。");

        // 两个方向都要有被判的句子：只判句首或只判句尾，另一半就是一句空话。
        Assert.Contains(interfaceSentences, s => s.StartsSentence);
        Assert.Contains(interfaceSentences, s => s.EndsSentence);
        Assert.Contains(serviceSentences, s => s.StartsSentence);
        Assert.Contains(serviceSentences, s => s.EndsSentence);
    }

    // ── 字表只有一份，且文档点名它 ─────────────────────────────────────

    /// <summary>
    /// 两侧对账：DESIGN.md §3 点名这两张字表是唯一落点 ↔ <c>CopyRules</c> 里各声明一次 ↔
    /// 界面层没有第二份字表。
    /// <para>
    /// <b>为什么这一条必要</b>：字表被抄成第二份时，两份会各自漂——渲染期那一半（<c>ChromeProbe.js</c> 由
    /// <c>HtmlWrapTests</c> 喂进去）与源码期这一半从此判的不是同一件事，而两条门都还是绿的。
    /// 本仓在「去注释口径」「字体栈」「重试字面」上都踩过同一个坑，这一处不再开第五次。
    /// </para>
    /// </summary>
    [Fact]
    public void ForbiddenSets_AreDeclaredOnceAndTheDesignDocNamesThem()
    {
        // ① 文档侧：§3 点名两个成员（渲染期那一半也取同一份）。
        var section3 = Repo.DesignSection(3);
        Assert.Contains("CopyRules.LineStartForbidden", section3, StringComparison.Ordinal);
        Assert.Contains("CopyRules.LineEndForbidden", section3, StringComparison.Ordinal);

        // ② 口径侧：各声明一次，且两张表不重叠（一个字不可能既不得起行又不得收尾）。
        var rules = Repo.Source("ApifoxHelper.Tests", "CopyRules.cs");
        Assert.Single(Regex.Matches(rules, @"public const string LineStartForbidden = """));
        Assert.Single(Regex.Matches(rules, @"public const string LineEndForbidden = """));
        Assert.NotEmpty(CopyRules.LineStartForbidden);
        Assert.NotEmpty(CopyRules.LineEndForbidden);
        Assert.Empty(CopyRules.LineStartForbidden.Intersect(CopyRules.LineEndForbidden));

        // ③ 实物侧：界面层不得就地写一份字表（连续四个禁则字出现在同一个字面量里，就是抄了一份）。
        foreach (var source in Repo.NativeSources())
        {
            foreach (Match match in Literal.Matches(source.Text))
            {
                var text = match.Groups[1].Value;
                for (var i = 0; i + 4 <= text.Length; i++)
                {
                    var window = text.Substring(i, 4);
                    Assert.True(
                        !window.All(CopyRules.LineStartForbidden.Contains) && !window.All(CopyRules.LineEndForbidden.Contains),
                        $"{source.Name}:字面量里出现「{window}」——界面层就地写了一份禁则字表。"
                        + "字表只在 CopyRules 一处（DESIGN.md §3），抄第二份之后两份会各自漂。");
                }
            }
        }
    }

    // ── 判据自测（这一族的老毛病：判据写错时门看起来是绿的）──────────────

    /// <summary>
    /// 六发注入各报一处、四种合规形状不报，最后再判一次真实源码是干净的。
    /// <para>
    /// 第 ④ ⑤ 发不是编出来的：它们是本仓真实源码里那两处<b>拼在别处</b>的片段
    /// （<c>ApifoxCli.ExitMeaning</c> 的 <c>1 =&gt; "，操作/API 失败"</c>、<c>DraftService</c> 三元分支里的
    /// <c>$"，异常分支 …"</c>）——按「字面量首字」判会当场假红，按「整句」判不报。
    /// 这两发钉住的正是这条门第一版被自己打回的那一处。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ① 句首是禁则字（逗号起句）。
        var commaStart = Problems(Sentences("Fake.cs", "ShowHint(\"，请重新检查。\", UiTokens.BadText);")).ToList();
        Assert.Single(commaStart);
        Assert.Contains("以禁则字起句（首字：，）", commaStart[0], StringComparison.Ordinal);

        // ② 句尾是开号（左括号收句）。
        var bracketEnd = Problems(Sentences("Fake.cs", "_status.Text = \"已停用（\";")).ToList();
        Assert.Single(bracketEnd);
        Assert.Contains("以开号收句（末字：（）", bracketEnd[0], StringComparison.Ordinal);

        // ③ 句首是闭号、句尾是句号：只报句首那一处（句尾那个句号不在开号表里）。
        var closingStart = Problems(Sentences("Fake.cs", "Ui.Caption(\"」页点「开始检查」。\");")).ToList();
        Assert.Single(closingStart);
        Assert.Contains("以禁则字起句（首字：」）", closingStart[0], StringComparison.Ordinal);

        // ④ 拼在 + 后面的片段：不报（本仓 ApifoxCli.ExitMeaning 的真实形状）。
        Assert.Empty(Problems(Sentences("Fake.cs", "1 => \"，操作/API 失败\",")));
        // ⑤ 三元分支里的片段：不报（本仓 DraftService 的真实形状）。
        Assert.Empty(Problems(Sentences("Fake.cs", "? $\"，异常分支 {name} 为空。\" : \"。\"")));
        // ⑥ 拼在 + 前面的片段：不报（本仓 LoadFailureState 的真实形状）。
        Assert.Empty(Problems(Sentences("Fake.cs", "ShowHint(\"尚未检查。先在「\" + PageNames.Tasks + \"」页点「开始检查」。\");")));

        // 合规形状：整句以汉字起、以句号收，一个字都不该报。
        Assert.Empty(Problems(Sentences("Fake.cs", "ShowHint(\"读取任务失败：磁盘满。\", UiTokens.BadText);")));
        // 没有中文的字面量不判（路径、标识符、格式串不是中文句子）。
        Assert.Empty(Problems(Sentences("Fake.cs", "var path = \"（tmp）\";")));
        // 空集合念成「没有违例」，不是「判不动」。
        Assert.Empty(Problems([]));

        // 真实源码上必须是零违例（否则上面那些「不报」只是因为它一直在报）。
        Assert.Empty(Problems(InterfaceSentences()));
        Assert.Empty(Problems(ServiceSentences()));
    }
}
