using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Infrastructure;

/// <summary>
/// 桌面程序没有控制台，日志按天写入资料目录下的 logs 文件夹，保留 14 天；内容经过脱敏。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "helper-*.log"))
        {
            if (File.GetLastWriteTime(file) >= DateTime.Now.AddDays(-14)) continue;
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 旧日志被占用时下次启动再清理。
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string text)
    {
        var path = Path.Combine(_directory, $"helper-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(path, text, Utf8);
            }
            catch (IOException)
            {
                // 写日志失败（磁盘满、文件被占用）不能影响正在进行的检查或同步。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ').Append(logLevel switch
                {
                    LogLevel.Trace => "TRC",
                    LogLevel.Debug => "DBG",
                    LogLevel.Information => "INF",
                    LogLevel.Warning => "WRN",
                    LogLevel.Error => "ERR",
                    _ => "CRT",
                })
                .Append(' ').Append(category).Append(": ")
                .Append(Redactor.Redact(formatter(state, exception)));
            if (exception is not null) sb.Append("\r\n").Append(Redactor.Redact(exception.ToString()));
            sb.Append("\r\n");
            provider.Write(sb.ToString());
        }
    }
}
