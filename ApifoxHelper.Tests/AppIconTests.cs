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
/// <b>它不判什么</b>（留在账上，不许由本门冒充已判）：① 不判「两份母版与 <c>app.ico</c> 同源渲染」——
/// 那要把 SVG 逐尺寸渲染成位图再比像素，而仓内没有可复现的 svg → ico 工具（第三百六十五轮那次重打包
/// 用的是落在 <c>/tmp/iconwork/</c> 的一次性脚本，简报 P1-1 把它登记为再动这颗的前置条件）；所以
/// 「渲染出来的对勾少了一笔」「32px 以上那一档取错了母版」本门判不到。② 不判这颗在任务栏 / 标题栏 /
/// 资源管理器里的观感（真机 0 台，随 G-D5）。③ 不判 <c>.exe</c> 旁边那一颗由 apphost 重新编码成
/// Win32 资源的图标（简报 P1-2）。它挡得住的是：<b>色值漂到 §2 里另一个取值、渐变回来、条目数或位深变</b>。
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
        const string name = "Assets/app.ico";
        var path = Path.Combine(Repo.Root, "Assets", "app.ico");
        Assert.True(File.Exists(path),
            $"{name} 不在仓里——它是 ApifoxHelper.csproj 的 ApplicationIcon 与两条 EmbeddedResource 的实物。");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 6 + 16 * RegisteredSides.Length,
            $"{name}：{bytes.Length} 字节，连 {RegisteredSides.Length} 条表头都装不下。");
        var span = bytes.AsSpan();

        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(span));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(span[2..]));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
        Assert.Equal((ushort)RegisteredSides.Length, count);

        var entries = new List<(int Side, int Size, int Offset)>();
        for (var i = 0; i < count; i++)
        {
            var at = 6 + 16 * i;
            var side = ReadSide(span[at]);
            Assert.Equal(side, ReadSide(span[at + 1]));                       // 方图：宽高同值
            Assert.Equal(RegisteredSides[i], side);                            // 连顺序一起
            Assert.Equal((byte)0, span[at + 2]);                               // 无调色板
            Assert.Equal((byte)0, span[at + 3]);                               // 保留位
            Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 4)..]));
            Assert.Equal((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 6)..]));
            entries.Add((side,
                (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 8)..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 12)..])));
        }

        var cursor = 6 + 16 * count;
        for (var i = 0; i < entries.Count; i++)
        {
            var (side, size, offset) = entries[i];
            var where = $"{name} 第 {i + 1} 条（{side}px）";
            Assert.True(offset == cursor,
                $"{where}：图像数据起始于 {offset}，而表头之后应紧邻在 {cursor}——条目之间有缝或有重叠。");
            Assert.True(offset + size <= bytes.Length, $"{where}：越出文件末尾（{offset} + {size} > {bytes.Length}）。");

            if (IsPng(span[offset..]))
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

    // ── 取法 ────────────────────────────────────────────────────────────

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
