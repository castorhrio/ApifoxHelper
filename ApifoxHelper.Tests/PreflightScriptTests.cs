using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 答辩当天预检脚本（<c>.winecheck/preflight.sh</c>，第二百二十三轮，简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：答辩当天的执行顺序（<c>docs/NOTES.md</c>「答辩当天执行顺序」7 步）与降级路径
/// 都演练过，但<b>临场才会暴露的失败点</b>一直没人提前核：<c>pwsh</c> 在不在（第二百零七轮的教训——
/// 判据成立、结论不成立）、当前源码与读数记录里的 <c>source=</c> 是不是同一版、三档演示到底跑过没有、
/// 九个归档目录还在不在。这四件事任何一件不对，现场念出来的数就不是当前源码的数，而现场没有时间查。
/// </para>
/// <para>
/// <b>本文件判两层</b>：① <b>源码形状</b>——四个检查点都在、每个检查点的失败分支<b>自己点名到哪一步</b>、
/// 结尾那一支真的非零退出；② <b>真的跑</b>——在临时假树上把四条检查点的<b>通过与不通过两个方向</b>都跑活，
/// 证明它判的就是它说在判的那四件事（不是「文件里有这几句话」）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机跑没跑（本机没有 Windows 图形子系统），不判归档内容对不对，
/// 也不判「在真机上这四条一定过」——它判的是<b>这一支脚本自己会不会当场翻车</b>。
/// 判据不写死具体路径（<c>~/.dotnet/tools/pwsh</c> 这类位置只判「先找它、再退 PATH」这个形状），
/// 只判步骤形状与点名。
/// </para>
/// </summary>
public class PreflightScriptTests
{
    private const string ScriptRelative = ".winecheck/preflight.sh";

    private static string ScriptPath => Path.Combine(Repo.Root, ".winecheck", "preflight.sh");

    private static string ScriptText
    {
        get
        {
            Assert.True(File.Exists(ScriptPath),
                $"缺 {ScriptRelative}：答辩当天唯一会当场翻车的那条路径没有可跑的一支。");
            return File.ReadAllText(ScriptPath);
        }
    }

    /// <summary>四个检查点的序号（封闭清单：这一支判的就是这四件事，多一件少一件都要改文档）。</summary>
    private static readonly string[] Checkpoints = ["①", "②", "③", "④"];

    /// <summary>段头形状：<c>echo "-- ① …"</c>。切块只按段头，不按空行——空行会随排版漂。</summary>
    private static int HeaderIndex(string[] lines, string marker) =>
        Array.FindIndex(lines, l => Regex.IsMatch(l, "^echo \"-- " + marker + " "));

    [Fact]
    public void Script_DeclaresItsFourCheckpointsAsSections()
    {
        var lines = ScriptText.ReplaceLineEndings("\n").Split('\n');
        Assert.StartsWith("#!/bin/bash", lines[0]);
        Assert.Contains("set -u", ScriptText, StringComparison.Ordinal);

        foreach (var marker in Checkpoints)
        {
            Assert.True(HeaderIndex(lines, marker) >= 0,
                $"{ScriptRelative} 里找不到第 {marker} 个检查点的段头（`echo \"-- {marker} …\"`）："
                + "四个检查点的边界判不出来，「点名到哪一步」也就无从核对。");
        }

        // 段头必须按 ①②③④ 的顺序出现：顺序错了，现场按输出从上往下读会读串。
        var order = Checkpoints.Select(m => HeaderIndex(lines, m)).ToList();
        Assert.Equal(order.OrderBy(i => i), order);
    }

    /// <summary>
    /// 每个检查点<b>两个方向都在</b>：一句 <c>fail "①"</c>（不通过时点名）与一句 <c>echo "   ① …"</c>
    /// （通过时如实说过了什么），且点名挂在条件分支里。
    /// <para>
    /// 判三件事：① 每个序号至少有一句 <c>fail "①"</c>；② 每个序号至少有一句带序号的通过行——
    /// 只有失败行没有通过行，现场分不清「过了」与「这一段没跑到」；③ 每一句点名都在条件分支里
    /// （<c>if</c> / <c>elif</c> / <c>else</c> / <c>case</c> 的 <c>*)</c>）——无条件打印的「未通过」
    /// 会在成功时也打出来，那句话就成了自述（与 <c>WineCheckScriptsTests.PrepareRealMachineScript_EveryStepNamesItsOwnFailure</c>
    /// 同一族纪律）。④ 点名里带着「怎么解」——现场没有时间查源码，只报「未通过」等于没报。
    /// </para>
    /// <para>
    /// <b>它判到哪一层</b>：这一条只判源码形状；「点名真的只在失败时打」由本文件下面那五条
    /// <b>真的跑</b>的用例判（两个方向都跑活）。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryCheckpoint_NamesItsOwnFailureAndItsOwnPass()
    {
        var lines = ScriptText.ReplaceLineEndings("\n").Split('\n');

        foreach (var marker in Checkpoints)
        {
            var calls = lines
                .Select((line, index) => (Line: line, Index: index))
                .Where(x => Regex.IsMatch(x.Line, "^\\s*fail \"" + marker + "\" "))
                .ToList();
            Assert.True(calls.Count > 0,
                $"{ScriptRelative} 第 {marker} 个检查点没有一句 `fail \"{marker}\" …`："
                + "这一步不过时脚本会静默往下走，现场看不出卡在哪一步——而这一支存在的理由就是点名。");

            Assert.Contains(lines, l => Regex.IsMatch(l, "^\\s*echo \"   " + marker + " "));
            Assert.Contains(lines, l => Regex.IsMatch(l, "^\\s*echo \"   " + marker + " "));

            foreach (var (line, index) in calls)
            {
                var branch = NearestBranch(lines, index);
                Assert.True(branch is "if" or "elif" or "else" or "*)",
                    $"第 {marker} 个检查点的点名不在条件分支里（最近的分支开头是「{branch}」）："
                    + "无条件打印的「未通过」会在成功时也打出来，那句话就成了自述。");
                Assert.True(line.Length > 40,
                    $"第 {marker} 个检查点的点名只有「{line.Trim()}」这么短：现场读到的只有「未通过」，"
                    + "没有「怎么解」——答辩当天没有时间查源码。");
            }
        }
    }

    /// <summary>往上找最近一个分支开头（<c>if</c> / <c>elif</c> / <c>else</c> / <c>case</c> 的 <c>*)</c>）。</summary>
    private static string NearestBranch(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("if ", StringComparison.Ordinal)) return "if";
            if (line.StartsWith("elif ", StringComparison.Ordinal)) return "elif";
            if (line == "else" || line.StartsWith("else ", StringComparison.Ordinal)) return "else";
            if (line.StartsWith("*)", StringComparison.Ordinal)) return "*)";
            if (line.StartsWith("case ", StringComparison.Ordinal)) return "case";
        }

        return "（文件开头）";
    }

    /// <summary>
    /// 结尾那一支：<c>if [ -n "$FAILED" ]</c> 里既点名了没过的步骤，又真的 <c>exit 1</c>。
    /// <para>
    /// 两件事必须<b>在同一个块里</b>：把 <c>exit 1</c> 挪到块外，成功时也非零退出（现场白紧张）；
    /// 把点名挪到块外，失败时只退不说是哪一步（现场白跑）。这一条与
    /// <c>WineCheckScriptsTests.DemoPs1_ExitsNonZeroWhenAScreenIsMissing</c> 判的是同一族形状。
    /// </para>
    /// </summary>
    [Fact]
    public void Script_ExitsNonZeroAndNamesTheFailedStepsInTheSameBlock()
    {
        var lines = ScriptText.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim() == "if [ -n \"$FAILED\" ]; then");
        Assert.True(start >= 0,
            $"{ScriptRelative} 里找不到结尾那一支 `if [ -n \"$FAILED\" ]; then`："
            + "四个检查点的结果没有汇成一句话，也没有一个统一的退出点。");

        var end = Array.FindIndex(lines, start + 1, l => l.Trim() == "fi");
        Assert.True(end > start, $"{ScriptRelative} 结尾那一支没有闭合的 `fi`。");
        var block = lines[start..end];

        Assert.Contains(block, l => l.Contains("FAILED", StringComparison.Ordinal) && l.Contains("预检未通过", StringComparison.Ordinal));
        Assert.Contains(block, l => l.Trim() == "exit 1");
        Assert.Contains(lines, l => l.Trim() == "exit 0");
    }

    /// <summary>
    /// 源指纹与 <c>demo.sh</c> <b>同一取法</b>：两支各算一套，同一份源码就会有两个指纹，
    /// 于是预检把演示刚写下的读数判成过期（第二百零七轮 <c>demo.ps1</c> 撞的正是这一族）。
    /// 判的是「取法逐字相同」而不是「结果相同」——结果相同可能只是这一次巧合。
    /// </summary>
    [Fact]
    public void SourceFingerprint_IsTheSamePipelineAsDemoSh()
    {
        static string Pipeline(string text)
        {
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            var start = Array.FindIndex(lines, l => l.StartsWith("source_fingerprint() {", StringComparison.Ordinal));
            Assert.True(start >= 0, "找不到 source_fingerprint() 的定义（指纹取法没有单一落点）。");
            var end = Array.FindIndex(lines, start + 1, l => l.Trim() == "}");
            Assert.True(end > start, "source_fingerprint() 没有闭合的 `}`。");
            return string.Join('\n', lines[(start + 1)..end].Select(l => l.Trim()));
        }

        var preflight = Pipeline(ScriptText);
        var demo = Pipeline(Repo.Source(".winecheck", "demo.sh"));
        Assert.Equal(demo, preflight);
    }

    // ── 真的跑：四条检查点的两个方向 ────────────────────────────────

    /// <summary>找 bash（PATH，再退三个常规位置）。找不到返回 null，调用方走「这台机器判不到」那一支。</summary>
    private static string? LocateBash()
    {
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path)) dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        dirs.AddRange(["/bin", "/usr/bin", "/usr/local/bin"]);

        foreach (var dir in dirs.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var candidate = Path.Combine(dir, "bash");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // 这一台上不合法的目录写法跳过就行。
            }
        }

        return null;
    }

    /// <summary>跑一次预检；<paramref name="env"/> 里的键值覆盖到子进程环境上。</summary>
    private static (int Exit, string Output) RunPreflight(params (string Key, string Value)[] env)
    {
        var psi = new ProcessStartInfo(LocateBash()!, ScriptPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Repo.Root,
        };
        psi.Environment["LANG"] = "C.UTF-8";
        foreach (var (key, value) in env) psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "preflight.sh 跑了 120 秒还没退出——它只该读几个文件与一个目录树。");
        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// 临时假树：一个源码文件、一份读数记录、一个冒烟器目录、一棵归档树。
    /// <para>
    /// 假树是这一条能判「两个方向」的前提：在真仓上跑，四条检查点的结果取决于这台机器上有没有
    /// <c>/tmp/ah-smoke-pub</c> 与 <c>pwsh</c>——那样的断言在换一台机器时判的就不是脚本了。
    /// </para>
    /// </summary>
    private sealed class FakeTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ah-preflight-" + Guid.NewGuid().ToString("N")[..12]);

        public string Reading => Path.Combine(Root, "reading.txt");

        public string SmokeDir => Path.Combine(Root, "smoke");

        public string Evidence => Path.Combine(Root, "evidence");

        public string FakePwsh => Path.Combine(Root, "pwsh");

        public FakeTree()
        {
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            File.WriteAllText(Path.Combine(Root, "src", "A.cs"), "class A { }\n");
            Directory.CreateDirectory(SmokeDir);
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(FakePwsh, "#!/bin/bash\nexit 0\n");
            // 假 pwsh 只需要「存在且可执行」：Windows 上没有 Unix 权限位，那一支由 PATH 上的真 pwsh 兜。
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FakePwsh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public void WriteReading(string source, params string[] tierLines)
        {
            var lines = new List<string> { "script=demo.sh", "tool=wine + Xvfb", "date=2026-11-12", "source=" + source };
            lines.AddRange(tierLines);
            File.WriteAllText(Reading, string.Join('\n', lines) + "\n");
        }

        public void WriteStamp(string fingerprint) =>
            File.WriteAllText(Path.Combine(SmokeDir, "smoke-source.sha"), fingerprint + "\n");

        /// <summary>把仓里那份产出物清单与九个归档目录搬进假树（只装占位，等于「在位但还空着」）。</summary>
        public void SeedArchive()
        {
            File.Copy(Path.Combine(Repo.Root, "docs", "evidence", "real-machine", "README.md"),
                Path.Combine(Evidence, "README.md"));
            // 目录名不在这里抄第二份：照仓里那棵归档树现取（清单与磁盘的对齐由
            // RealMachineEvidenceTests 判，这一条只借它的形状造一棵假树）。
            var real = Path.Combine(Repo.Root, "docs", "evidence", "real-machine");
            foreach (var dir in Directory.GetDirectories(real).Select(Path.GetFileName))
            {
                var full = Path.Combine(Evidence, dir!);
                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, ".gitkeep"), string.Empty);
            }
        }

        public (string Key, string Value)[] Env(string? pwsh = null) =>
        [
            ("REPO", Root),
            ("READING", Reading),
            ("SMOKE_EXE", Path.Combine(SmokeDir, "ApifoxHelper.Smoke.exe")),
            ("EVIDENCE", Evidence),
            ("README", Path.Combine(Evidence, "README.md")),
            ("PWSH", pwsh ?? FakePwsh),
        ];

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响判据。
            }
        }
    }

    /// <summary>
    /// 第 ② 条两个方向：源指纹对不上时非零退出并点名 ②，把记录改成脚本自己算出来的那一份后 ② 通过。
    /// <para>
    /// 期望值不在这里另算一遍（那等于把 <c>find | sort | sha1sum</c> 抄第二份）：先跑一次拿到脚本
    /// 自己打出的「当前源码算出来是 X」，再把 X 写进记录——<b>脚本的输出就是这一条的判据</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void CheckpointTwo_FailsOnMismatchedFingerprintAndPassesOnceTheRecordMatches()
    {
        if (LocateBash() is null) return; // 这台机器上没有 bash：判不到。

        using var tree = new FakeTree();
        tree.WriteReading("deadbeef", "tier=96 passed=1 failed=0 exit=0");
        tree.WriteStamp("deadbeef");
        tree.SeedArchive();

        var (exit, output) = RunPreflight(tree.Env());
        Assert.True(exit != 0, "源指纹对不上时预检却退出了 0——现场会带着上一版代码的读数开讲。");
        Assert.Contains("② 未通过", output, StringComparison.Ordinal);
        Assert.Contains("源指纹对不上", output, StringComparison.Ordinal);

        var match = Regex.Match(output, @"当前源码算出来是 ([0-9a-f]{40})");
        Assert.True(match.Success, $"第 ② 条没有把「当前源码算出来是哪个指纹」打出来：{output}");
        var current = match.Groups[1].Value;

        tree.WriteReading(current, "tier=96 passed=1 failed=0 exit=0");
        tree.WriteStamp(current);
        var (exit2, output2) = RunPreflight(tree.Env());
        Assert.True(exit2 == 0, $"记录改成当前源码的指纹后预检仍非零退出：{output2}");
        Assert.Contains("② 一致", output2, StringComparison.Ordinal);
    }

    /// <summary>
    /// 第 ③ 条两个方向：读数记录里一条 <c>tier=</c> 都没有时点名 ③；有档位但冒烟器旁边没有源码指纹时
    /// 仍点名 ③（判不出它出自哪一版源码）；补上指纹后 ③ 通过。
    /// </summary>
    [Fact]
    public void CheckpointThree_RequiresTierLinesAndTheSourceStamp()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeTree();
        tree.SeedArchive();

        // 先让 ② 通过：拿脚本自己算出的指纹写进记录。
        tree.WriteReading("deadbeef");
        var (_, probe) = RunPreflight(tree.Env());
        var current = Regex.Match(probe, @"当前源码算出来是 ([0-9a-f]{40})").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(current), $"第 ② 条没打出当前指纹：{probe}");

        // ③-a：一条 tier= 都没有。
        tree.WriteReading(current);
        var (exitA, outputA) = RunPreflight(tree.Env());
        Assert.True(exitA != 0, "读数记录里一条 tier= 都没有，预检却退出了 0——三档演示没跑过也放行。");
        Assert.Contains("③ 未通过", outputA, StringComparison.Ordinal);
        Assert.Contains("一条 tier= 都没有", outputA, StringComparison.Ordinal);

        // ③-b：有档位、但冒烟器旁边没有源码指纹。
        tree.WriteReading(current, "tier=96 passed=1 failed=0 exit=0");
        var (exitB, outputB) = RunPreflight(tree.Env());
        Assert.True(exitB != 0, "冒烟器旁边没有源码指纹，预检却退出了 0——判不出这一档出自哪一版源码。");
        Assert.Contains("③ 未通过", outputB, StringComparison.Ordinal);
        Assert.Contains("没有源码指纹", outputB, StringComparison.Ordinal);

        // ③-c：补上指纹 → 通过。
        tree.WriteStamp(current);
        var (exitC, outputC) = RunPreflight(tree.Env());
        Assert.True(exitC == 0, $"补上源码指纹后预检仍非零退出：{outputC}");
        Assert.Contains("③", outputC, StringComparison.Ordinal);
    }

    /// <summary>
    /// 第 ③ 条的反向：某一档 <c>failed</c> 不为 0 或 <c>exit</c> 不为 0 时必须点名 ③——
    /// 带着失败行念「wine 量到」正是这一条要挡的假绿。
    /// </summary>
    [Fact]
    public void CheckpointThree_RejectsATierWithFailuresOrANonZeroExit()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeTree();
        tree.SeedArchive();
        tree.WriteReading("deadbeef");
        var (_, probe) = RunPreflight(tree.Env());
        var current = Regex.Match(probe, @"当前源码算出来是 ([0-9a-f]{40})").Groups[1].Value;
        tree.WriteStamp(current);

        foreach (var bad in new[] { "tier=96 passed=226 failed=1 exit=0", "tier=96 passed=227 failed=0 exit=1" })
        {
            tree.WriteReading(current, "tier=96 passed=227 failed=0 exit=0", bad);
            var (exit, output) = RunPreflight(tree.Env());
            Assert.True(exit != 0, $"读数记录里有「{bad}」，预检却退出了 0——失败行会被当成通过念出去。");
            Assert.Contains("③ 未通过", output, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 第 ④ 条两个方向：归档树读不到清单时点名 ④（<c>collect-real-machine.sh</c> 退出码 2 = 核对本身跑不动）；
    /// 九个目录都在位（只装占位、还空着）时<b>不算失败</b>——真机 0 台时它们本来就该是空的，
    /// 把「空」判成预检失败，等于逼人在答辩前伪造归档。
    /// </summary>
    [Fact]
    public void CheckpointFour_RequiresTheArchiveDirectoriesToBeInPlaceButNotFilled()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeTree();
        tree.WriteReading("deadbeef");
        var (_, probe) = RunPreflight(tree.Env());
        var current = Regex.Match(probe, @"当前源码算出来是 ([0-9a-f]{40})").Groups[1].Value;
        tree.WriteReading(current, "tier=96 passed=1 failed=0 exit=0");
        tree.WriteStamp(current);

        // ④-a：归档树里没有产出物清单 → 核对本身跑不动 → 点名 ④。
        var (exitA, outputA) = RunPreflight(tree.Env());
        Assert.True(exitA != 0, "归档树读不到清单，预检却退出了 0——九个目录在不在位没人核。");
        Assert.Contains("④ 未通过", outputA, StringComparison.Ordinal);
        Assert.Contains("collect-real-machine.sh 退出码", outputA, StringComparison.Ordinal);

        // ④-b：九个目录都在位、但都还空着 → 不算失败。
        tree.SeedArchive();
        var (exitB, outputB) = RunPreflight(tree.Env());
        Assert.True(exitB == 0, $"九个目录都在位（还空着）时预检却非零退出：{outputB}");
        Assert.Contains("九个目录都在位", outputB, StringComparison.Ordinal);
        Assert.Contains("空着不算预检失败", outputB, StringComparison.Ordinal);
    }

    /// <summary>
    /// 第 ① 条的反向：<c>pwsh</c> 既不在 PATH 也不在 <c>~/.dotnet/tools/pwsh</c> 时点名 ①。
    /// <para>
    /// 用 <c>HOME</c> 指到空目录 + 一条不含 <c>pwsh</c> 的 PATH 造出这个条件——这正是第二百零七轮
    /// 那个「判据成立、结论不成立」的反面：<b>找不到时必须说找不到</b>，而不是静默放行。
    /// </para>
    /// </summary>
    [Fact]
    public void CheckpointOne_NamesTheMissingPowerShell()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeTree();
        tree.SeedArchive();
        tree.WriteReading("deadbeef");
        var (_, probe) = RunPreflight(tree.Env());
        var current = Regex.Match(probe, @"当前源码算出来是 ([0-9a-f]{40})").Groups[1].Value;
        tree.WriteReading(current, "tier=96 passed=1 failed=0 exit=0");
        tree.WriteStamp(current);

        var emptyHome = Path.Combine(tree.Root, "home");
        Directory.CreateDirectory(emptyHome);
        var env = tree.Env(pwsh: Path.Combine(tree.Root, "no-such-pwsh"))
            .Concat([("HOME", emptyHome), ("PATH", "/usr/bin:/bin")])
            .ToArray();

        var (exit, output) = RunPreflight(env);
        Assert.True(exit != 0, "pwsh 找不到时预检却退出了 0——Windows 侧那支讲稿现场跑不起来也没人知道。");
        Assert.Contains("① 未通过", output, StringComparison.Ordinal);
        Assert.Contains("pwsh", output, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// 第 ③ 条在 <c>PUBLISH=1</c> 下的那一支（第二百八十一轮，简报 P0-1 的实测修）。
    /// <para>
    /// <b>它挡的是什么</b>：干净环境（清过 <c>/tmp/ah-smoke-pub</c>）里，第 ③ 条要判的产物
    /// （冒烟器 + 源码指纹 + 读数记录）正是 <c>demo.sh</c> 在 <c>PUBLISH=1</c> 时才发的，而 <c>demo.sh</c>
    /// 排在预检之后（<c>defense-day.sh</c> 的第 ③ 步）。拿「还没发出来的产物」判这一条，链就永远停在预检——
    /// 第二百八十一轮实测：清掉产物后 <c>PUBLISH=1 bash .winecheck/defense-day.sh</c> 整支退出码 1、停在第 ② 步。
    /// 所以 <c>PUBLISH=1</c> 时这一条改判「重发得起来」，判的仍是实物（<c>dotnet</c> 与冒烟器工程在位）。
    /// </para>
    /// <para>
    /// <b>两个方向都在</b>：① 冒烟器工程不在 → 点名 ③（重发根本起不来，别放行）；
    /// ② 工程在位 → 通过并如实说「三档过没过由第 ③ 步自己判」；③ 不带 <c>PUBLISH</c> 时照旧点名 ③
    /// （产物不在就是不在——这一支没有把默认那一支放松）。
    /// </para>
    /// </summary>
    [Fact]
    public void CheckpointThree_UnderPublishJudgesTheRepublishInsteadOfTheMissingArtifact()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeTree();
        tree.SeedArchive();
        tree.WriteReading("deadbeef");
        var (_, probe) = RunPreflight(tree.Env());
        var current = Regex.Match(probe, @"当前源码算出来是 ([0-9a-f]{40})").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(current), $"第 ② 条没打出当前指纹：{probe}");
        tree.WriteReading(current, "tier=96 passed=1 failed=0 exit=0");

        // ① 冒烟器工程不在：PUBLISH=1 也起不来 → 点名 ③。
        var (exitA, outputA) = RunPreflight([.. tree.Env(), ("PUBLISH", "1")]);
        Assert.True(exitA != 0, "PUBLISH=1 但冒烟器工程不在，预检却退出了 0——第 ③ 步会在发布那一步就停，而现场不知道。");
        Assert.Contains("③ 未通过", outputA, StringComparison.Ordinal);
        Assert.Contains("找不到冒烟器工程", outputA, StringComparison.Ordinal);

        // ② 工程在位：改判「重发得起来」→ 通过，并如实说三档过没过由第 ③ 步自己判。
        Directory.CreateDirectory(Path.Combine(tree.Root, "ApifoxHelper.Smoke"));
        File.WriteAllText(Path.Combine(tree.Root, "ApifoxHelper.Smoke", "ApifoxHelper.Smoke.csproj"), "<Project />\n");
        var (exitB, outputB) = RunPreflight([.. tree.Env(), ("PUBLISH", "1")]);
        Assert.True(exitB == 0, $"PUBLISH=1 且冒烟器工程在位时预检却非零退出：{outputB}");
        Assert.Contains("③ PUBLISH=1", outputB, StringComparison.Ordinal);
        Assert.Contains("重发得起来", outputB, StringComparison.Ordinal);
        Assert.Contains("由第 ③ 步自己判", outputB, StringComparison.Ordinal);

        // ③ 不带 PUBLISH：产物不在就照旧点名 ③（默认那一支一个字没放松）。
        var (exitC, outputC) = RunPreflight(tree.Env());
        Assert.True(exitC != 0, "不带 PUBLISH 时冒烟器旁边没有源码指纹，预检却退出了 0——判不出这一档出自哪一版源码。");
        Assert.Contains("③ 未通过", outputC, StringComparison.Ordinal);
        Assert.Contains("没有源码指纹", outputC, StringComparison.Ordinal);
    }
}
