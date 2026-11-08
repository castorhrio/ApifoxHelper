namespace ApifoxHelper.Desktop.Native;

/// <summary>排序记忆的两个页面键（ListSortMemory 的取值口径，界面代码不就地写字符串字面量）。</summary>
public static class ListSortMemoryKeys
{
    public const string Tasks = "tasks";
    public const string Profiles = "profiles";
}

/// <summary>
/// 列表排序状态的跨启动记忆（DESIGN.md §6「列表列排序」）：点列头排好的那一列与方向，
/// 下次启动仍在同一列同一方向——与「启动回到上次使用的页面」（LastPage）、窗口位置记忆
/// （WindowPlacement）同族的会话型工具便利设置，记的只有「按哪一列、朝哪个方向」。
/// 记的是<b>列标题</b>而不是列号：列号在界面增删列后会指向另一列，
/// 那等于把用户没点过的那一列当成上次排的那一列——认不出标题就回退数据源顺序，
/// 与第五十三轮「声明的列落在格数之外时宁可复制不到」同一取舍。
/// 无记录、读取失败、内容损坏、页键不认识、标题不在本页列头里，一律回退未排序
/// （任务=创建时间倒序、配置=名称升序，即第四十七轮的原有行为）；保存失败不影响排序。
/// 不记筛选关键词（筛到零行的下一步是空态的「清空筛选」，把关键词带到下次启动会把行藏起来）、
/// 不记选中行、不记列宽列序，也不写任务与配置数据。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public sealed class ListSortMemory(string path)
{
    private sealed record Entry(string? Column, bool Descending);

    private sealed record Saved(Entry? Tasks, Entry? Profiles);

    /// <summary>
    /// 读取本页上次排序的列与方向：<paramref name="titles"/> 是本页建列时快照的原标题，
    /// 记忆里的标题在其中才生效（重复标题取第一列）；否则返回未排序，方向位一并归零。
    /// </summary>
    public (int Column, bool Descending) Read(string page, IReadOnlyList<string> titles)
    {
        var entry = EntryOf(ReadSaved(), page);
        if (entry is null || string.IsNullOrEmpty(entry.Column)) return (ListSort.None, false);
        var column = entry.Column;
        for (var i = 0; i < titles.Count; i++)
        {
            if (string.Equals(titles[i], column, StringComparison.Ordinal)) return (i, entry.Descending);
        }
        return (ListSort.None, false);
    }

    /// <summary>
    /// 记录本页本次排序的列（按标题）与方向；页键不认识、列号落在本页列头之外、标题为空都不写，
    /// 另一页的记忆原样保留（两页各排各的列）。保存失败不影响本次排序。
    /// </summary>
    public void Save(string page, IReadOnlyList<string> titles, int column, bool descending)
    {
        if (page is not (ListSortMemoryKeys.Tasks or ListSortMemoryKeys.Profiles)) return;
        if (column < 0 || column >= titles.Count) return;
        var title = titles[column];
        if (title.Length == 0) return;
        var saved = ReadSaved();
        var entry = new Entry(title, descending);
        var next = page == ListSortMemoryKeys.Tasks
            ? new Saved(entry, saved?.Profiles)
            : new Saved(saved?.Tasks, entry);
        try
        {
            FileStore.WriteJson(path, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 排序记忆只是便利设置，保存失败不影响本次排序。
        }
    }

    private Saved? ReadSaved()
    {
        try
        {
            return FileStore.ReadJson<Saved>(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Entry? EntryOf(Saved? saved, string page) => page switch
    {
        ListSortMemoryKeys.Tasks => saved?.Tasks,
        ListSortMemoryKeys.Profiles => saved?.Profiles,
        _ => null,
    };
}
