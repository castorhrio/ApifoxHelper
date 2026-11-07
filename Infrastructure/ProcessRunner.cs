namespace ApifoxHelper.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, TimeSpan Elapsed);

/// <summary>
/// 子进程统一入口：明确的可执行文件 + 参数列表，不拼 shell 字符串；UTF-8 读取输出；不弹窗口。
/// </summary>
public static class ProcessRunner
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["FORCE_COLOR"] = "0";

        using var process = new Process { StartInfo = psi };
        var stopwatch = Stopwatch.StartNew();
        process.Start();
        // 不允许子进程等待交互输入。
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(grace.Token);
            }
            catch (OperationCanceledException)
            {
                // 进程树无法及时结束时不再等待，调用方按超时处理。
            }
            if (!timedOut)
            {
                throw;
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();
        return new ProcessResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut, stopwatch.Elapsed);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
