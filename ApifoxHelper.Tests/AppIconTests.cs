using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 品牌资产形状门（第三百六十六轮，控制仓简报 P0-2，具名 pass：harden + evidence）。
/// <para>
/// <b>要解决的问题</b>：那颗标记连着两轮改色与底板（第三百六十四轮评审、第三百六十五轮动刀），
/// 而第三百六十四、三百六十五两轮都在账上如实写同一句「<b>三份资产在仓内没有任何一条测试读它们</b>」
/// （现读：<c>ApifoxHelper.Tests/</c> 搜 <c>app-icon</c> / <c>app.ico</c> / <c>ApplicationIcon</c> 零命中）。
/// 每一轮的像素读数都是那一轮自己临时量一次，改坏了没有任何机械检查会红：色值漂走、渐变长回来、
/// <c>app.ico</c> 少一条尺寸档，3300 余条用例一条都不动。这一处不是「放着没人看」——
/// 原生壳与过渡壳都吃它（<c>NativeShellForm</c> / <c>MainForm</c> 取 <c>Assets.app.ico</c> 设窗口图标，
/// <c>Api/WebAssets.cs</c> 把 <c>Assets.app-icon.svg</c> 发在 <c>/app-icon.svg</c>），
/// 而它是界面之外用户看到的第一眼品牌面。
/// </para>
/// <para>
/// <b>按第二百八十二轮五步法判族：对账门</b>。第②步答「不是〈判据判自己〉」——它读的是三份实物
/// （两份 SVG 母版 + 一份 ICO 容器）的实际内容，与被它判的对象不是同一件东西；形状与
/// <c>TokenWiringTests</c> 同一族：那一族拿 <c>UiTokens.cs</c> 对 <c>docs/DESIGN.md</c> §2 那张表，
/// 这一族拿三份资产对<b>同一张表的 <c>Accent</c> 那一行</b>。<c>Accent</c> 的取值不在这里抄第二份字面量
/// （取 <see cref="TokenWiringTests.DocColors" />，§2 ↔ <c>UiTokens.cs</c> 的逐色一致已由
/// <c>TokenWiringTests.ColorTokens_MatchDesignDoc</c> 钉住），否则改了 §2 而这一处不跟着红，
/// 正是本仓反复登记过的那种「两个说话位各自漂移」。
/// </para>
/// <para>
/// <b>它不判什么</b>（留在账上，不许由本门冒充已判）：① 不判「渲染出来的那八张位图与仓内这一份是同一版
/// 像素」——第三百六十六轮这一句写的是「仓内没有可复现的 svg → ico 工具」，第三百六十七轮按简报 P1-1
/// 把工具落进仓（<c>Assets/build-app-icon.py</c>），本文件下面那两条门判的是<b>它自报的打包清单与仓内
/// 那颗实物的容器对得上</b>（条目数、顺序、边长、哪一档是 PNG、每一档取哪一份母版）；至于「照这份清单
/// 真渲染一遍，字节是不是就是这一份」，判它得跑一次 Python + Pillow，产物字节随 Pillow 那一版的 PNG
/// 编码器走，所以这一条留在账上：跑法在工具自己头部（<c>Assets/build-app-icon.py</c> 的 Usage 那两行），
/// 最近一次跑通的读数记在 <c>docs/DESIGN.md</c> §8「品牌资产」那一格。
/// 「渲染出来的对勾少了一笔」本门判不到，但清单取错母版、少一档尺寸、256 那一档不再是 PNG，本门红。
/// ② 不判这颗在任务栏 / 标题栏 / 资源管理器里的观感（真机 0 台，随 G-D5）。③ 不判 <c>.exe</c> 旁边那一颗
/// 由 apphost 重新编码成 Win32 资源的图标（简报 P1-2）。它挡得住的是：<b>色值漂到 §2 里另一个取值、
/// 渐变回来、条目数或位深变、工具的打包清单与实物分家</b>。
/// </para>
/// </summary>
public class AppIconTests
{
    /// <summary>第三百六十五轮登记的八个条目边长，连顺序一起（256 打头、16 收尾）。</summary>
    private static readonly int[] RegisteredSides = [256, 64, 48, 40, 32, 24, 20, 16];

    /// <summary>两份母版（文件名 → 全文）。读文件只在这一处，下面两条判定共用同一份文本。</summary>
    private static IReadOnlyList<(string Name, string Text)> Masters() =>
    [
        ("Assets/app-icon.svg", Repo.Source("Assets", "app-icon.svg")),
        ("Assets/app-icon-small.svg", Repo.Source("Assets", "app-icon-small.svg")),
    ];

    /// <summary>
    /// §1 原则 1 那句「无渐变」（第三百六十五轮刚删掉的那条竖向 <c>#3B82F6 → #1D4ED8</c>）。
    /// <para>
    /// 判字样而不是判「有没有 <c>url(#…)</c> 引用」：渐变回来只有两种写法——写回 <c>&lt;linearGradient&gt;</c> /
    /// <c>&lt;radialGradient&gt;</c> 本身，或写回指向它的引用。前一种这一条当场红；后一种就算把元素改了名，
    /// 也活不过 <see cref="Masters_KeepExactlyOneChromaticColor_TheAccentToken" />，因为渐变的两档蓝会同时
    /// 成为彩度取值。两条各拦一头，合起来才把「无渐变」落在实物上。
    /// </para>
    /// </summary>
    [Fact]
    public void Masters_UseNoGradient()
    {
        foreach (var (name, text) in Masters())
        {
            var hits = Regex.Matches(text, "gradient", RegexOptions.IgnoreCase);
            Assert.True(hits.Count == 0,
                $"{name}：出现 {hits.Count} 处 gradient 字样。DESIGN.md §1 原则 1 立的是「无渐变」，而 §2 那一个 "
                + "Accent 平涂已经足够把底板与笔画分开——改前那一处渐变自己两端只差 1.82:1，那两档蓝之间"
                + "不携带信息（读数见 §8「品牌资产」那一格）。");
        }
    }

    /// <summary>
    /// §1 原则 1 那句「灰阶铬色 + 恰一个强调色」，落在两份母版上＝彩度取值恰一个且等于 §2 <c>Accent</c>。
    /// <para>
    /// 「恰一个」判的是两头：漂成两个（第三百六十五轮之前的两档蓝 + 青绿对勾）即红，一个都不剩
    /// （底板也改成灰阶，这颗标记就没有强调色了）同样红。取的是上色的那一层
    /// （<c>fill</c> / <c>stroke</c> / <c>stop-color</c>），不取整份文件里的每一个 <c>#RRGGBB</c>——
    /// 母版注释里引用一个历史色值是写给读的人找实物的路，不是这颗标记上多出来的一个色。
    /// </para>
    /// </summary>
    [Fact]
    public void Masters_KeepExactlyOneChromaticColor_TheAccentToken()
    {
        var accent = AccentHex();
        foreach (var (name, text) in Masters())
        {
            var painted = Regex.Matches(text, @"(?:fill|stroke|stop-color)\s*=\s*""#([0-9A-Fa-f]{6})""")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.True(painted.Count > 0,
                $"{name}：一个上色取值都没读到——这份母版已经不是这一门认识的那一颗标记。");

            var chromatic = painted.Where(v => !IsAchromatic(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(chromatic.Count == 1 && chromatic[0].Equals(accent, StringComparison.OrdinalIgnoreCase),
                $"{name}：彩度取值应为恰一个且等于 DESIGN.md §2 的 Accent（#{accent}），实际量到 "
                + (chromatic.Count == 0 ? "零个（只剩灰阶）" : string.Join(" / ", chromatic.Select(v => $"#{v}")))
                + "。灰阶（R = G = B）不在此列——§1 原则 1 要的是「灰阶铬色 + 恰一个强调色」，多一个彩度色"
                + "就是另起一套视觉语言；§2 表内的其他取值（AccentHover / OkText / WarnText / BadText）"
                + "在这颗标记上同样是第二个色。");
        }
    }

    /// <summary>
    /// <c>app.ico</c> 的条目结构照旧：八条、顺序与位深都落在第三百六十五轮登记的那一组数上。
    /// <para>
    /// 判的是容器与表头，不是像素。少一条目 = 某一档尺寸在某个 Windows 版本上落到系统默认图标；
    /// <c>wType</c> 不是 1 = 那不再是一颗图标；位深或 <c>biHeight</c> 变了 = 打包口径换了而仓内没有一处说明。
    /// 七条 BMP 的字节数是按结构<b>算出来</b>的（40 字节表头 + 边长² × 4 + 每行补齐到 32 位的 1bpp AND 掩码），
    /// 不是抄下来的常数——抄长度会让一次「同样正确的重新打包」也红，而这一条红说的是结构真的变了。
    /// </para>
    /// </summary>
    [Fact]
    public void Icon_KeepsTheRegisteredEntryStructure()
    {
        const string name = IconName;
        var icon = ReadIcon();
        var bytes = icon.Bytes;
        var span = bytes.AsSpan();

        Assert.Equal((ushort)0, icon.Reserved);
        Assert.Equal((ushort)1, icon.Type);
        Assert.Equal((ushort)RegisteredSides.Length, icon.Count);

        for (var i = 0; i < icon.Entries.Count; i++)
        {
            var e = icon.Entries[i];
            Assert.Equal(e.Side, ReadSide(e.HeightByte));                    // 方图：宽高同值
            Assert.Equal(RegisteredSides[i], e.Side);                          // 连顺序一起
            Assert.Equal((byte)0, e.ColorCount);                               // 无调色板
            Assert.Equal((byte)0, e.Reserved2);                                // 保留位
            Assert.Equal((ushort)1, e.Planes);
            Assert.Equal((ushort)32, e.Bits);
        }

        var cursor = 6 + 16 * icon.Count;
        for (var i = 0; i < icon.Entries.Count; i++)
        {
            var e = icon.Entries[i];
            var (side, size, offset, isPng) = (e.Side, e.Size, e.Offset, e.IsPng);
            var where = $"{name} 第 {i + 1} 条（{side}px）";
            Assert.True(offset == cursor,
                $"{where}：图像数据起始于 {offset}，而表头之后应紧邻在 {cursor}——条目之间有缝或有重叠。");
            Assert.True(offset + size <= bytes.Length, $"{where}：越出文件末尾（{offset} + {size} > {bytes.Length}）。");

            if (isPng)
            {
                // 256 那一档是 PNG：它的内部结构（chunk）不由这一门管，这一门判的是容器与表头。
                Assert.Equal(256, side);
            }
            else
            {
                Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]));
                Assert.Equal(side, BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]));
                Assert.Equal(side * 2, BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 8)..]));
                Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 12)..]));
                Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 14)..]));
                Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 16)..]));

                // biHeight = 2× 边长的那一半就是 1bpp AND 掩码：每行补齐到 32 位。
                var expected = 40 + side * side * 4 + MaskRowBytes(side) * side;
                Assert.True(size == expected,
                    $"{where}：{size} 字节，而 32bpp 彩色位图 + 1bpp AND 掩码应是 {expected} 字节"
                    + "（40 表头 + 边长² × 4 + 每行补齐到 32 位的掩码）——打包口径变了（少了掩码？位深不是 32？）。");
            }

            cursor += size;
        }

        Assert.True(cursor == bytes.Length,
            $"{name}：八条图像到 {cursor} 就结束了，文件却有 {bytes.Length} 字节——尾部多出来的东西没有归属。");
    }

    /// <summary>
    /// 第三百六十七轮落仓的那支工具（<c>Assets/build-app-icon.py</c>，简报 P1-1）自报的打包清单，
    /// 与仓内那颗实物<b>对得上</b>：条目数、顺序、边长、哪一档是 PNG、引用的是哪两份母版。
    /// <para>
    /// <b>补的是哪个缺口</b>：上一条门把容器的形状钉住了，判的是「实物自己长什么样」；判不到
    /// <b>再做一遍的那套步骤</b>与实物是不是同一件事——清单活在工具的 <c>PLAN</c>，容器活在 <c>app.ico</c>，
    /// 两边各有一份而谁都不对着谁。有人删掉 <c>40</c> 那一档或把 <c>256</c> 改成 BMP，实物还是那一份，
    /// 下一次真打包才露出来，而那时红的是产物不是改动。
    /// </para>
    /// <para>
    /// <b>按第二百八十二轮五步法判族：对账门</b>。第②步答「不是〈判据判自己〉」——<c>PLAN</c> 的定义在
    /// <c>Assets/build-app-icon.py</c>，被对照的是另一件实物（<c>Assets/app.ico</c> 的目录），与
    /// <c>ProfileJumpTests.LandingButtonTable_CoversEveryButtonOnTheConfigPage</c> 同一族：
    /// 那一族判「表里的每个按钮在页上都有实物，反之亦然」，这一条判「清单里的每一档在容器里都有实物，反之亦然」。
    /// </para>
    /// </summary>
    [Fact]
    public void IconTool_Plan_MatchesTheShippedContainer()
    {
        var plan = ToolPlan();
        var icon = ReadIcon();

        Assert.True(plan.Count == icon.Count,
            $"{ToolName}：清单里 {plan.Count} 条，而 {IconName} 的目录有 {icon.Count} 条——"
            + "改了清单没重新打包（或反之），这两份要到下一次打包才对不上。");

        var masters = Masters().Select(m => Path.GetFileName(m.Name)).ToList();
        for (var i = 0; i < plan.Count; i++)
        {
            var p = plan[i];
            var e = icon.Entries[i];
            var where = $"第 {i + 1} 条（{p.Side}px ← {p.Master}，{p.Kind}）";
            Assert.True(p.Side == e.Side,
                $"{ToolName}：{where} 与 {IconName} 第 {i + 1} 条（{e.Side}px）不是同一档——清单与实物的顺序分家了。");
            Assert.True((p.Kind == "png") == e.IsPng,
                $"{ToolName}：{where} 声明的是 {(p.Kind == "png" ? "PNG" : "BMP")}，实物里这一档"
                + $"{(e.IsPng ? "是 PNG" : "不是 PNG")}。「七条 BMP + 256 一条 PNG」是第三百六十五轮定下的打包口径，"
                + "改它得两侧一起改。");
            Assert.True(masters.Contains(p.Master, StringComparer.Ordinal),
                $"{ToolName}：{where} 指的母版 {p.Master} 不是这一族对账的那两份（{string.Join(" / ", masters)}）。");
        }
    }

    /// <summary>
    /// 清单里每一档取的母版，等于那一档<b>在母版自己的注释里声明</b>的范围——「32 px 及以上取大母版」不是散文。
    /// <para>
    /// 上一条门比的是档数、顺序、容器种类，比不到「<c>24</c> 那一档改吃大母版」：BMP 条目的字节数只由边长
    /// 算出（上面那条公式），取错母版时容器全绿。这一条判的就是那一发。阈值<b>不抄第二份</b>：范围取两份
    /// 母版头部各自那一句（大母版「（32 px 及以上）」、小母版「（16–24 px）」），改了母版那句而这里不跟着红，
    /// 就是本仓登记过的那种「两个说话位各自漂移」。
    /// </para>
    /// </summary>
    [Fact]
    public void IconTool_TakesTheMasterEachOneDeclaresForItself()
    {
        var declared = new Dictionary<string, (int Low, int High)>(StringComparer.Ordinal);
        foreach (var master in Masters())
        {
            var name = Path.GetFileName(master.Name);
            var above = Regex.Match(master.Text, @"（(\d+)\s*px\s*及以上）");
            var band = Regex.Match(master.Text, @"（(\d+)\s*[–—-]\s*(\d+)\s*px\s*）");
            Assert.True(above.Success || band.Success,
                $"{name}：头部注释里读不到它自己声明的尺寸范围。这一条门取的就是那一句——那句换了形状，"
                + "这里就得跟着改，否则「这一档该用哪份母版」重新变成只有人眼看。");
            declared[name] = above.Success
                ? (int.Parse(above.Groups[1].Value), int.MaxValue)
                : (int.Parse(band.Groups[1].Value), int.Parse(band.Groups[2].Value));
        }

        foreach (var p in ToolPlan())
        {
            var fits = declared.Where(kv => p.Side >= kv.Value.Low && p.Side <= kv.Value.High).ToList();
            Assert.True(fits.Count == 1,
                $"{ToolName}：{p.Side}px 落在 {fits.Count} 份母版自报的范围里（"
                + string.Join(" / ", declared.Select(kv => $"{kv.Key}：{Describe(kv.Value)}"))
                + "）——两份母版的声明重叠或有缝，「这一档取哪一份」不再由实物回答。");
            Assert.True(fits[0].Key == p.Master,
                $"{ToolName}：{p.Side}px 取的母版是 {p.Master}，而自报覆盖 {p.Side}px 的那一份是 {fits[0].Key}"
                + $"（{Describe(fits[0].Value)}）。");
        }
    }

    // ── 取法 ────────────────────────────────────────────────────────────

    /// <summary><c>Assets/app.ico</c>：两条判定都要点名它，写一份。</summary>
    private const string IconName = "Assets/app.ico";

    /// <summary><c>Assets/build-app-icon.py</c>：第三百六十七轮落仓的那支重打包工具（简报 P1-1）。</summary>
    private const string ToolName = "Assets/build-app-icon.py";

    /// <summary>目录里的一条：边长（0 已换算成 256）、字节数、起点、是不是 PNG，以及四个原始字段。</summary>
    private sealed record IconEntry(int Side, int Size, int Offset, bool IsPng,
                                    byte HeightByte, byte ColorCount, byte Reserved2, ushort Planes, ushort Bits);

    /// <summary>工具清单里的一条：边长、母版文件名（<c>BIG</c> / <c>SMALL</c> 已换成实际文件名）、容器种类。</summary>
    private sealed record ToolEntry(int Side, string Master, string Kind);

    /// <summary>
    /// <c>app.ico</c> 表头与目录的<b>唯一取法</b>（第三百六十七轮从两条判定里抽出来共用）。
    /// 只读不判——判的是各自那两条门要判的东西。取法在两处各写一遍的后果本仓登记过四次
    /// （<see cref="CodeScan" /> 那句「去注释口径只有一份」是同一件事）。
    /// </summary>
    private static (ushort Reserved, ushort Type, ushort Count, List<IconEntry> Entries, byte[] Bytes) ReadIcon()
    {
        var path = Path.Combine(Repo.Root, "Assets", "app.ico");
        Assert.True(File.Exists(path),
            $"{IconName} 不在仓里——它是 ApifoxHelper.csproj 的 ApplicationIcon 与两条 EmbeddedResource 的实物。");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 6 + 16 * RegisteredSides.Length,
            $"{IconName}：{bytes.Length} 字节，连 {RegisteredSides.Length} 条表头都装不下。");
        var span = bytes.AsSpan();

        var count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        var entries = new List<IconEntry>();
        for (var i = 0; i < count; i++)
        {
            var at = 6 + 16 * i;
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 8)..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 12)..]);
            entries.Add(new IconEntry(
                ReadSide(span[at]), size, offset,
                offset >= 0 && offset + 8 <= bytes.Length && IsPng(span[offset..]),
                span[at + 1], span[at + 2], span[at + 3],
                BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 4)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 6)..])));
        }
        return (BinaryPrimitives.ReadUInt16LittleEndian(span), BinaryPrimitives.ReadUInt16LittleEndian(span[2..]),
            count, entries, bytes);
    }

    /// <summary>
    /// 现读工具自报的打包清单（<c>PLAN</c> 那几行 + <c>BIG</c> / <c>SMALL</c> 指的哪两份母版）。
    /// 工具不在仓里即红并点名——那正是第三百六十六轮登记的状态，不该由这两条门冒充成别的。
    /// </summary>
    private static List<ToolEntry> ToolPlan()
    {
        var path = Path.Combine(Repo.Root, "Assets", "build-app-icon.py");
        Assert.True(File.Exists(path),
            $"{ToolName} 不在仓里——第三百六十六轮那句「仓内没有可复现的 svg → ico 工具」于是又是当前事实。"
            + "要么把工具落回来，要么把这两条门一起删掉并在账上写回去，别留一条读不到对象的门。");
        var text = File.ReadAllText(path);

        var names = Regex.Matches(text, @"(?<key>BIG|SMALL)\s*=\s*os\.path\.join\(HERE,\s*""(?<file>[^""]+)""\)")
            .Cast<Match>().ToDictionary(m => m.Groups["key"].Value, m => m.Groups["file"].Value, StringComparer.Ordinal);
        Assert.True(names.Count == 2,
            $"{ToolName}：读到 {names.Count} 个母版名，而这里要的是 BIG / SMALL 各指一份 .svg——"
            + "这份工具引用的母版不再是这一族对账的那两份。");

        var plan = Regex.Matches(text, @"\(\s*(?<side>\d+)\s*,\s*(?<master>BIG|SMALL)\s*,\s*""(?<kind>png|bmp)""\s*\)")
            .Cast<Match>()
            .Select(m => new ToolEntry(int.Parse(m.Groups["side"].Value), names[m.Groups["master"].Value], m.Groups["kind"].Value))
            .ToList();
        Assert.True(plan.Count > 0,
            $"{ToolName}：读不到 PLAN 里的任何一条（边长 + 母版 + 容器）。这一份工具已经不是这一族对账的那一份。");
        return plan;
    }

    /// <summary>把母版自报的范围念回读的人听得的那一句（<c>int.MaxValue</c> 那一档就是「及以上」）。</summary>
    private static string Describe((int Low, int High) band) =>
        band.High == int.MaxValue ? $"{band.Low}px 及以上" : $"{band.Low}–{band.High}px";

    /// <summary>§2 那张表的 <c>Accent</c> 取值（表本身取 <see cref="TokenWiringTests.DocColors" />，不抄第二份）。</summary>
    private static string AccentHex()
    {
        var colors = TokenWiringTests.DocColors();
        Assert.True(colors.TryGetValue("Accent", out var hex),
            "DESIGN.md §2 那张颜色表里读不出 Accent 那一行——表的形状变了，这一门没有对账对象。");
        return hex!;
    }

    /// <summary>灰阶：R = G = B（含纯白与纯黑）。§1 原则 1 容许灰阶，所以这些不算彩度。</summary>
    private static bool IsAchromatic(string hex)
    {
        var r = int.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber);
        var g = int.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber);
        var b = int.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber);
        return r == g && g == b;
    }

    /// <summary>ICO 的宽高字节：0 是 256 的写法（那一个字节放不下 256）。</summary>
    private static int ReadSide(byte value) => value == 0 ? 256 : value;

    /// <summary>1bpp 掩码每一行占的字节数（每行边长个位，补齐到 32 位）。</summary>
    private static int MaskRowBytes(int side) => (side + 31) / 32 * 4;

    /// <summary>PNG 的八个魔数（ico 里 256 那一档是 PNG，其余七档是 BMP）。</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool IsPng(ReadOnlySpan<byte> image) =>
        image.Length >= 8 && image[..8].SequenceEqual(PngSignature);
}
