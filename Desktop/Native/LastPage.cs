namespace ApifoxHelper.Desktop.Native;

/// <summary>壳的两个页面键（LastPage 的取值口径，界面代码不就地写字符串字面量）。</summary>
public static class LastPageKeys
{
    public const string Projects = "projects";
    public const string Tasks = "tasks";
}

/// <summary>
/// 壳启动时回到上次使用的页面：会话型工具的窗口位置记忆（WindowPlacement）同族——
/// 日常旅程在「检查任务」页，每次启动固定落在「项目设置」页要多点一次。
/// 只记页面键，不记任何业务数据；读不到、内容损坏或值不认识时回退项目设置页（首次启动的缺省）。
/// </summary>
public sealed class LastPage(string path)
{
    private sealed record Saved(string? Page);

    /// <summary>读取上次使用的页面键；无记录、读取失败或值不在两页键内时回退项目设置页。</summary>
    public string Read()
    {
        Saved? saved;
        try
        {
            saved = FileStore.ReadJson<Saved>(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return LastPageKeys.Projects;
        }
        return saved?.Page == LastPageKeys.Tasks ? LastPageKeys.Tasks : LastPageKeys.Projects;
    }

    /// <summary>记录页面键；只接受两页键，其余忽略（调用面只有壳内两处切换）。保存失败不影响切换。</summary>
    public void Save(string page)
    {
        if (page is not (LastPageKeys.Projects or LastPageKeys.Tasks)) return;
        try
        {
            FileStore.WriteJson(path, new Saved(page));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 页面记忆只是便利设置，保存失败不影响切换。
        }
    }
}
