using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 补充说明的编辑位与保存切片口径（冻结页面差异详情「业务说明补充」与原生对话框同一规则，
/// SupplementEntries 单一落点）：编辑位按目标/远端构造（标题远端优先、说明目标优先，参数/请求体/200
/// 响应来自目标，header 参数不设编辑位）；保存只替换本接口本次编辑位的条目（保留现状 → KeepAsIs 值为空、
/// 填写文字 → 去空白、两者皆空 → 不写条目），其他接口与未编辑位（含示例）原样保留；
/// 校验与落盘由 SupplementService 单一口径执行。未知状态原样回退的口径不在本文件（无状态映射）。
/// </summary>
public class SupplementEntriesTests
{
    private static EndpointCheck FullEndpoint() => new()
    {
        Key = "GET /api/orders",
        Method = "GET",
        Path = "/api/orders",
        Status = "structure",
        Remote = new DocOperation { Summary = "远端标题", Description = "远端说明" },
        Target = new DocOperation
        {
            Summary = "目标标题",
            Description = "目标说明",
            Parameters =
            {
                ["query:pageSize"] = new DocParameter { In = "query", Name = "pageSize", Description = "分页大小" },
                ["path:id"] = new DocParameter { In = "path", Name = "id", Description = "编号" },
                ["header:X-Auth"] = new DocParameter { In = "header", Name = "X-Auth" },
            },
            RequestBody = new DocBody { Description = "请求体说明" },
            Responses = { ["200"] = new DocResponse { Code = "200", Description = "响应说明" } },
        },
    };

    [Fact]
    public void EditorsFor_MatchesFrozenPageLocationsAndPrecedence()
    {
        var editors = SupplementEntries.EditorsFor(FullEndpoint());
        // 编辑位与冻结页面一致：标题、说明、query/path 参数、请求体、200 响应；header 参数不设编辑位。
        // 参数顺序按检查结果落盘的参数表（in:name 序），与冻结页面从同一份 JSON 读到的顺序一致。
        Assert.Equal(
            [("接口标题", "operation", "name"), ("接口说明", "operation", "description"),
             ("参数 id", "path.id", "description"), ("参数 pageSize", "query.pageSize", "description"),
             ("请求体", "requestBody", "description"), ("200 响应", "responses.200", "description")],
            editors.Select(e => (e.Label, e.Location, e.Field)).ToList());
        // 现状文字取值先后与冻结页面一致：标题远端优先，说明目标优先。
        Assert.Equal("远端标题", editors[0].CurrentText);
        Assert.Equal("目标说明", editors[1].CurrentText);
        Assert.Equal("编号", editors[2].CurrentText);
        Assert.Equal("请求体说明", editors[4].CurrentText);
        Assert.Equal("响应说明", editors[5].CurrentText);
    }

    [Fact]
    public void EditorsFor_SourceOnlyEndpointHasTitleAndDescriptionOnly()
    {
        var endpoint = new EndpointCheck { Key = "GET /api/only-source", Status = "sourceOnly" };
        var editors = SupplementEntries.EditorsFor(endpoint);
        Assert.Equal(["接口标题", "接口说明"], editors.Select(e => e.Label).ToList());
        Assert.All(editors, e => Assert.Equal("", e.CurrentText));
        // 目标存在但没有请求体与 200 响应时，只有标题、说明与参数编辑位。
        var partial = new EndpointCheck
        {
            Key = "GET /api/partial",
            Target = new DocOperation
            {
                Parameters = { ["query:q"] = new DocParameter { In = "query", Name = "q" } },
            },
        };
        var partialEditors = SupplementEntries.EditorsFor(partial);
        Assert.Equal(["接口标题", "接口说明", "参数 q"], partialEditors.Select(e => e.Label).ToList());
    }

    [Fact]
    public void ExistingEntry_MatchesByLocationAndField()
    {
        var store = new SupplementStore
        {
            Entries =
            {
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "operation", Field = "description", Value = "已保存说明" },
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "operation", Field = "name", KeepAsIs = true },
                new SupplementEntry { ResourceKey = "GET /api/other", Location = "operation", Field = "description", Value = "别的接口" },
            },
        };
        var editors = SupplementEntries.EditorsFor(FullEndpoint());
        Assert.Equal("已保存说明", SupplementEntries.ExistingEntry(store, "GET /api/orders", editors[1])?.Value);
        Assert.True(SupplementEntries.ExistingEntry(store, "GET /api/orders", editors[0])?.KeepAsIs);
        Assert.Null(SupplementEntries.ExistingEntry(store, "GET /api/orders", editors[2]));
        Assert.Equal(2, SupplementEntries.EndpointEntryCount(store, "GET /api/orders"));
        Assert.Equal(1, SupplementEntries.EndpointEntryCount(store, "GET /api/other"));
        Assert.Equal(0, SupplementEntries.EndpointEntryCount(store, "GET /api/missing"));
    }

    [Fact]
    public void MergeEndpoint_KeepWinsOverTextAndEmptyMeansNoEntry()
    {
        var editors = SupplementEntries.EditorsFor(FullEndpoint());
        var values = new List<SupplementEntries.EditorValue>
        {
            // 保留现状：值为空、KeepAsIs，即使输入框还有文字（与冻结页面一致，勾选优先）。
            new(editors[0], "输入框残留", true),
            // 填写文字：去空白后保存。
            new(editors[1], "  使用场景说明  ", false),
            // 两者皆空：不写条目。
            new(editors[2], "", false),
            new(editors[3], "   ", false),
        };
        var merged = SupplementEntries.MergeEndpoint(new SupplementStore(), "GET /api/orders", values);
        Assert.Equal(2, merged.Entries.Count);
        var keep = merged.Entries.Single(e => e.Field == "name");
        Assert.True(keep.KeepAsIs);
        Assert.Null(keep.Value);
        var filled = merged.Entries.Single(e => e.Field == "description");
        Assert.Equal("使用场景说明", filled.Value);
        Assert.False(filled.KeepAsIs);
    }

    [Fact]
    public void MergeEndpoint_ReplacesEditedSliceAndKeepsEverythingElse()
    {
        var editors = SupplementEntries.EditorsFor(FullEndpoint());
        var store = new SupplementStore
        {
            Entries =
            {
                // 本接口旧说明（将被替换）。
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "operation", Field = "description", Value = "旧说明" },
                // 本接口示例（不在本次编辑位：原样保留）。
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "responses.200", Field = "example", Value = "{}" },
                // 其他接口的补充（原样保留）。
                new SupplementEntry { ResourceKey = "GET /api/other", Location = "operation", Field = "description", Value = "其他接口" },
            },
        };
        var merged = SupplementEntries.MergeEndpoint(store, "GET /api/orders",
            [new SupplementEntries.EditorValue(editors[1], "新说明", false)]);
        // 本接口旧说明被替换；本接口示例（未编辑位）与其他接口的补充原样保留，新条目追加在后。
        Assert.Equal(3, merged.Entries.Count);
        Assert.Equal("新说明", merged.Entries.Single(e => e.ResourceKey == "GET /api/orders" && e.Field == "description").Value);
        Assert.Equal("example", merged.Entries.Single(e => e.ResourceKey == "GET /api/orders" && e.Field == "example").Field);
        Assert.Equal("其他接口", merged.Entries.Single(e => e.ResourceKey == "GET /api/other").Value);
        // 原 store 不被改动（对话框重复保存不会累积）。
        Assert.Equal("旧说明", store.Entries[0].Value);
    }

    [Fact]
    public void SavedText_StatesChangedAndUnchangedHonestly()
    {
        var changed = SupplementEntries.SavedText(changed: true);
        Assert.Contains("已保存", changed);
        Assert.Contains("重新检查", changed);
        var unchanged = SupplementEntries.SavedText(changed: false);
        Assert.DoesNotContain("已保存", unchanged);
        Assert.Contains("没有变化", unchanged);
    }

    [Fact]
    public void UserVisibleTexts_AvoidBannedPhrases()
    {
        var texts = new List<string>
        {
            SupplementEntries.SavedText(true),
            SupplementEntries.SavedText(false),
        };
        texts.AddRange(SupplementEntries.EditorsFor(FullEndpoint()).Select(e => e.Label));
        foreach (var text in texts)
        {
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }
}
