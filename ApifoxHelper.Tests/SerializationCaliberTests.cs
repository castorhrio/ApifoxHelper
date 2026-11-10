using ApifoxHelper.Infrastructure;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 序列化口径的一致性（第一百四十二轮，简报 P1）。
/// <para>
/// 为什么钉这一条：同一份数据在本进程里有两条 JSON 出口——本地服务的 HTTP 响应
/// （<c>HelperHost</c> 的 <c>ConfigureHttpJsonOptions</c>）与进程内直接序列化
/// （<c>JsonDefaults.Compact</c>，桌面界面、任务资料落盘、指纹计算都走它）。两边此前各写一遍
/// 「中文原样 / 空值不写 / 枚举 camelCase」三项设置，改了一边忘了另一边时，同一份数据经 HTTP
/// 与经进程内序列化会得到不同的 JSON（枚举变成数字、空值忽有忽无）——这种差异不报错，
/// 只会让比对与回读核对悄悄失配。现在两边都调 <see cref="JsonDefaults.Apply"/>，
/// 本文件既断言「两条路径产出同一份 JSON」，也钉住「设置只有一处」。
/// </para>
/// </summary>
public class SerializationCaliberTests
{
    private enum SampleState
    {
        PendingReview,
        Done,
    }

    private sealed record Sample(string Name, SampleState State, string? Note, int Count);

    private static Sample Fixture() => new("订单模块", SampleState.PendingReview, null, 3);

    /// <summary>
    /// 两条路径对同一对象产出逐字节相同的 JSON：HTTP 那一侧用 ASP.NET 的
    /// <c>JsonSerializerDefaults.Web</c> 起手（与 <c>ConfigureHttpJsonOptions</c> 同款），
    /// 再经 <see cref="JsonDefaults.Apply"/> 补齐口径。
    /// </summary>
    [Fact]
    public void HttpJsonOptions_AndCompact_ProduceTheSameJson()
    {
        var viaCompact = JsonSerializer.Serialize(Fixture(), JsonDefaults.Compact);

        var http = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonDefaults.Apply(http);
        var viaHttp = JsonSerializer.Serialize(Fixture(), http);

        Assert.Equal(viaCompact, viaHttp);
    }

    /// <summary>三项口径本身：枚举写成 camelCase 字符串、空值不写、中文原样（不转成 \uXXXX）。</summary>
    [Fact]
    public void BothPaths_WriteEnumsAsCamelCaseAndOmitNullsAndKeepChinese()
    {
        var http = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonDefaults.Apply(http);

        foreach (var json in new[]
                 {
                     JsonSerializer.Serialize(Fixture(), JsonDefaults.Compact),
                     JsonSerializer.Serialize(Fixture(), http),
                 })
        {
            Assert.Contains("\"state\":\"pendingReview\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"note\"", json, StringComparison.Ordinal);
            Assert.Contains("订单模块", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
        }
    }

    /// <summary>缩进版（任务资料落盘）与紧凑版是同一份口径，只差缩进。</summary>
    [Fact]
    public void IndentedOptions_UseTheSameCaliber()
    {
        var indented = JsonSerializer.Serialize(Fixture(), JsonDefaults.Options);
        Assert.Contains("\"state\": \"pendingReview\"", indented, StringComparison.Ordinal);
        Assert.DoesNotContain("\"note\"", indented, StringComparison.Ordinal);
        Assert.Contains("订单模块", indented, StringComparison.Ordinal);
    }

    /// <summary>
    /// 设置只有一处：三项口径只写在 <see cref="JsonDefaults.Apply"/> 里，
    /// 服务层别处不得再各写一遍（第二份就是会漂移的那一份）。
    /// </summary>
    [Fact]
    public void JsonDefaults_IsTheOnlyPlaceSettingTheCaliber()
    {
        var core = Repo.Source("Infrastructure", "Core.cs");
        Assert.Single(Regex.Matches(core, "new JsonStringEnumConverter\\("));
        Assert.Single(Regex.Matches(core, "DefaultIgnoreCondition = "));
        Assert.Single(Regex.Matches(core, "options\\.Encoder = "));
        Assert.Contains("public static void Apply(JsonSerializerOptions options)", core, StringComparison.Ordinal);
    }

    /// <summary>
    /// HTTP 那一侧经同一份口径接线：<c>HelperHost</c> 只调 <see cref="JsonDefaults.Apply"/>，
    /// 不再就地写三项设置（就地写就是第二条会漂移的路径）。
    /// </summary>
    [Fact]
    public void HelperHost_ConfiguresHttpJsonThroughTheSingleSource()
    {
        var host = Repo.Source("Api", "HelperHost.cs");
        Assert.Contains("ConfigureHttpJsonOptions(o => JsonDefaults.Apply(o.SerializerOptions))", host, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultIgnoreCondition", host, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonStringEnumConverter", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Encoder =", host, StringComparison.Ordinal);
    }

    /// <summary>进程内那一侧：路由的 JSON 出口就是 <c>JsonDefaults.Compact</c>（与上面那条同一份口径）。</summary>
    [Fact]
    public void ApiRoutes_Json_UsesCompact()
    {
        var routes = Repo.Source("Api", "ApiRoutes.cs");
        Assert.Contains("public static IResult Json(object? value) => Results.Json(value, JsonDefaults.Compact);", routes, StringComparison.Ordinal);
    }
}
