using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Infrastructure;

public sealed class JobInfo
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required string Kind { get; init; }
    public string State { get; set; } = "running";
    public double Progress { get; set; }
    public string? ProgressText { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public object? Result { get; set; }

    [JsonIgnore]
    public CancellationTokenSource Cancellation { get; } = new();

    [JsonIgnore]
    public Task? Completion { get; set; }
}

public sealed class JobContext(JobInfo info)
{
    public JobInfo Info { get; } = info;

    public CancellationToken Token => Info.Cancellation.Token;

    public void Report(double progress, string text)
    {
        Info.Progress = Math.Clamp(progress, 0, 1);
        Info.ProgressText = text;
    }
}

/// <summary>
/// 长耗时操作（扫描、草稿生成、远端快照、比对、同步）放到后台执行，页面轮询进度。
/// 同一作用域（任务）同时只允许一个作业，避免扫描与回写、同步交叉进行。
/// </summary>
public sealed class JobRunner(ILogger<JobRunner> logger)
{
    private readonly ConcurrentDictionary<string, JobInfo> _jobs = new();
    private readonly ConcurrentDictionary<string, string> _running = new();

    /// <summary>
    /// 作业到达终态（succeeded/failed/canceled）后触发一次。在线程池线程触发，
    /// 订阅方（原生壳的完成提醒）自行调度到 UI 线程；页面轮询不依赖本事件。
    /// </summary>
    public event Action<JobInfo>? JobCompleted;

    public JobInfo Start(string scope, string kind, Func<JobContext, Task<object?>> work)
    {
        var job = new JobInfo { Id = Ids.New("job"), Scope = scope, Kind = kind, StartedAt = DateTimeOffset.Now };
        if (!_running.TryAdd(scope, job.Id))
        {
            var runningKind = _running.TryGetValue(scope, out var runningId) && _jobs.TryGetValue(runningId, out var running) ? running.Kind : "其他操作";
            throw new UserException($"该任务正在执行“{runningKind}”，请等待完成或取消后再试。", 409);
        }

        _jobs[job.Id] = job;
        job.Completion = Task.Run(async () =>
        {
            try
            {
                job.Result = await work(new JobContext(job));
                job.State = "succeeded";
                job.Progress = 1;
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                job.State = "canceled";
            }
            catch (UserException ex)
            {
                job.State = "failed";
                job.Error = ex.Message;
            }
            catch (Exception ex)
            {
                job.State = "failed";
                job.Error = Redactor.Redact(ex.Message);
                logger.LogError(ex, "作业 {Kind} 失败", kind);
            }
            finally
            {
                job.FinishedAt = DateTimeOffset.Now;
                _running.TryRemove(new KeyValuePair<string, string>(scope, job.Id));
                Prune();
                JobCompleted?.Invoke(job);
            }
        });
        return job;
    }

    public JobInfo? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public JobInfo? Running(string scope) =>
        _running.TryGetValue(scope, out var id) && _jobs.TryGetValue(id, out var job) ? job : null;

    public bool Cancel(string scope)
    {
        var job = Running(scope);
        if (job is null) return false;
        job.Cancellation.Cancel();
        return true;
    }

    public IReadOnlyList<JobInfo> RunningJobs() =>
        _running.Values.Select(id => _jobs.TryGetValue(id, out var job) ? job : null).OfType<JobInfo>().ToList();

    /// <summary>
    /// 退出前请求所有作业停止并等待它们结束（最多 timeout）。同步作业只在动作之间响应取消，
    /// 已经开始的单个写入会完成并回读核对，计划状态如实落盘。
    /// </summary>
    public async Task CancelAllAsync(TimeSpan timeout)
    {
        var running = RunningJobs();
        if (running.Count == 0) return;
        foreach (var job in running) job.Cancellation.Cancel();
        var completions = running.Select(j => j.Completion).OfType<Task>().ToArray();
        if (completions.Length == 0) return;
        var all = Task.WhenAll(completions);
        if (await Task.WhenAny(all, Task.Delay(timeout)) == all)
        {
            logger.LogInformation("退出前已停止 {Count} 个作业", running.Count);
            return;
        }
        logger.LogWarning("等待超时，退出时仍有作业未结束：{Kinds}", string.Join("、", running.Where(j => j.State == "running").Select(j => j.Kind)));
    }

    private void Prune()
    {
        if (_jobs.Count <= 200) return;
        foreach (var old in _jobs.Values.Where(j => j.FinishedAt is not null).OrderBy(j => j.FinishedAt).Take(_jobs.Count - 200))
        {
            _jobs.TryRemove(old.Id, out _);
        }
    }
}
