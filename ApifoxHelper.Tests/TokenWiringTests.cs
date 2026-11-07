using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// G-D2 令牌接线机械检查（设计验收 D1/D4/D6，docs/DESIGN.md §8 挂账项的落地）：
/// 1) UiTokens.cs 的值与 DESIGN.md 完全一致（改令牌必须同次更新文档，两侧都改才算过）；
/// 2) 界面代码无就地发明的颜色/字体/间距值——颜色与字体只能出自 UiTokens，间距只能是文档档位（另允许 0=无间距、1=发丝边）。
/// 扫描对象是源码文本：检查的是"接线口径"，不替代真机观感复核（G-D5 仍需人工）。
/// </summary>
public class TokenWiringTests
{
    private const string AllowedExtraSpacing = "0=无间距、1=发丝边宽度（DESIGN.md §5）";

    // ── UiTokens.cs 与 DESIGN.md 一致 ─────────────────────────────────────

    [Fact]
    public void ColorTokens_MatchDesignDoc()
    {
        var doc = DocColors();
        var code = CodeColors();
        Assert.Equal(doc.Keys.OrderBy(k => k, StringComparer.Ordinal), code.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, hex) in doc)
        {
            Assert.True(code.TryGetValue(name, out var actual),
                $"DESIGN.md 定义了颜色 {name}，UiTokens.cs 没有对应字段。");
            Assert.True(string.Equals(hex, actual, StringComparison.OrdinalIgnoreCase),
                $"颜色 {name} 文档为 #{hex}，代码为 #{actual}。改令牌必须同一次改动更新 DESIGN.md 与 UiTokens.cs。");
        }
    }

    [Fact]
    public void FontPointTiers_MatchDesignDoc()
    {
        var doc = DocPointTiers();
        var code = CodePointTiers();
        Assert.Equal(doc.Keys.OrderBy(k => k, StringComparer.Ordinal), code.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (tier, pt) in doc)
        {
            Assert.True(code.TryGetValue(tier, out var actual) && actual == pt,
                $"字号档 {tier} 文档为 {pt}pt，代码为 {(code.TryGetValue(tier, out var v) ? v.ToString() : "缺失")}。");
        }
    }

    [Fact]
    public void SpacingTiers_MatchDesignDoc()
    {
        var doc = DocSpacingTiers();
        var code = CodeSpacingTiers();
        Assert.Equal(doc.Keys.OrderBy(k => k, StringComparer.Ordinal), code.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, value) in doc)
        {
            Assert.True(code.TryGetValue(name, out var actual) && actual == value,
                $"间距档 {name} 文档为 {value}，代码为 {(code.TryGetValue(name, out var v) ? v.ToString() : "缺失")}。");
        }
    }

    // ── 界面代码无就地发明值 ──────────────────────────────────────────────

    [Fact]
    public void NativeSources_NoColorLiteralsOutsideUiTokens()
    {
        foreach (var source in Repo.NativeSources())
        {
            if (!source.IsUiTokens)
            {
                Assert.False(source.Contains("0x"),
                    $"{source.Name}：出现 0x 颜色字面量。颜色只能定义在 UiTokens.cs（DESIGN.md §2）。");
            }
            if (source.IsUiTokens)
            {
                // UiTokens.cs 是颜色的唯一定义位点：0x 字面量与 FromArgb 都在这里，不做使用侧检查。
                continue;
            }
            Assert.Empty(source.Matches(@"Color\.FromArgb"));
            foreach (var match in source.Matches(@"Color\.([A-Z]\w*)").Cast<Match>())
            {
                var allowed = source.IsUi && match.Groups[1].Value == "White";
                Assert.True(allowed,
                    $"{source.Name}：出现 Color.{match.Groups[1].Value}。允许的只有 UiTokens 令牌；White（主按钮白字）只允许在 Ui.cs。");
            }
        }
    }

    [Fact]
    public void NativeSources_NoFontConstructionOutsideUiTokens()
    {
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUiTokens))
        {
            Assert.False(source.Contains("new Font("),
                $"{source.Name}：出现 new Font(。字号只能取 UiTokens 的封闭四档（DESIGN.md §3）。");
        }
    }

    [Fact]
    public void NativeSources_PaddingsOnlyFromSpacingTiers()
    {
        var tiers = DocSpacingTiers().Values.ToHashSet();
        var allowed = new HashSet<int>(tiers) { 0, 1 };
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUiTokens))
        {
            foreach (var match in source.Matches(@"new Padding\(([^()]*)\)").Cast<Match>())
            {
                foreach (var number in Regex.Matches(match.Groups[1].Value, @"\d+").Select(m => int.Parse(m.Value)))
                {
                    Assert.True(allowed.Contains(number),
                        $"{source.Name}：间距出现档外值 {number}（允许档位 {string.Join('/', tiers.OrderBy(v => v))}；另允许 {AllowedExtraSpacing}）。");
                }
            }
        }
    }

    // ── DESIGN.md 解析 ────────────────────────────────────────────────────

    /// <summary>§2 颜色表：`名` | `#RRGGBB`（Accent 行带粗体标记，一并容许）。</summary>
    private static Dictionary<string, string> DocColors() =>
        Regex.Matches(Repo.DesignSection(2), @"`(\w+)`\s*(?:\*\*)?\s*\|\s*(?:\*\*)?`#([0-9A-Fa-f]{6})`")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    /// <summary>§3 字号表：档名 → 磅值（"12px（9pt）"）。</summary>
    private static Dictionary<string, float> DocPointTiers() =>
        Regex.Matches(Repo.DesignSection(3), @"^\|\s*(\w+)\s*\|\s*\d+px（(\d+(?:\.\d+)?)pt）", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => float.Parse(m.Groups[2].Value));

    /// <summary>§4 间距表：档名 → 值。</summary>
    private static Dictionary<string, int> DocSpacingTiers() =>
        Regex.Matches(Repo.DesignSection(4), @"`(\w+)`\s*\|\s*(\d+)\s*\|")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

    // ── UiTokens.cs 解析 ──────────────────────────────────────────────────

    private static Dictionary<string, string> CodeColors()
    {
        var text = Repo.NativeSources().Single(s => s.IsUiTokens).Text;
        return Regex.Matches(text, @"public static readonly Color (\w+) = FromHex\(0x([0-9A-Fa-f]{6})\)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    private static Dictionary<string, float> CodePointTiers()
    {
        var text = Repo.NativeSources().Single(s => s.IsUiTokens).Text;
        return Regex.Matches(text, @"public const float (\w+) = (\d+(?:\.\d+)?)f;")
            .ToDictionary(m => m.Groups[1].Value[..^2], m => float.Parse(m.Groups[2].Value));
    }

    private static Dictionary<string, int> CodeSpacingTiers()
    {
        var text = Repo.NativeSources().Single(s => s.IsUiTokens).Text;
        return Regex.Matches(text, @"public const int (\w+) = (\d+);")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
    }
}
