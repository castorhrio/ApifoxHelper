using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// ProfileStore 保存校验断言（共享源码单一口径，真实 FileStore/ProfileStore 在 Linux 执行）：
/// 全部拒绝路径的文案逐字不变，且随 UserException.Field 带出字段标识（配置 JSON 的 camelCase 属性名，
/// DESIGN.md §6 校验失败描边的定位依据——外层「保存」的失败由配置对话框按它描红对应输入框）。
/// 被拒的保存不落半份状态；合法保存经 Normalize 规整落盘并可重读。
/// </summary>
public class ProfileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-profiletests-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _store = new ProfileStore(new FileStore(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>工作区与 API 项目都真实存在的基础配置；各拒绝用例在此基础上只破坏一个字段。</summary>
    private ProjectProfile Valid() => new()
    {
        Name = "示例配置",
        WorkspaceRoot = _workspace,
        ApiProject = "Api.csproj",
    };

    private UserException Reject(ProjectProfile profile)
    {
        try
        {
            _store.Save(profile, isNew: true);
        }
        catch (UserException ex)
        {
            return ex;
        }
        throw new Xunit.Sdk.XunitException("期望保存被拒绝，但保存成功了。");
    }

    [Fact]
    public void FieldIds_AreConfigJsonPropertyNames()
    {
        // 字段标识 = 配置 JSON 的 camelCase 属性名（UserException.Field 的约定）：改名即失配，必须两处同改。
        Assert.Equal("name", ProfileStore.FieldName);
        Assert.Equal("workspaceRoot", ProfileStore.FieldWorkspaceRoot);
        Assert.Equal("apiProject", ProfileStore.FieldApiProject);
        Assert.Equal("allowedSourcePaths", ProfileStore.FieldAllowedSourcePaths);
        Assert.Equal("projectId", ProfileStore.FieldProjectId);
        Assert.Equal("type", ProfileStore.FieldSwaggerType);
        Assert.Equal("location", ProfileStore.FieldSwaggerLocation);
        Assert.Equal("mode", ProfileStore.FieldGenerationMode);
        Assert.Equal("baseUrl", ProfileStore.FieldBaseUrl);
        Assert.Equal("model", ProfileStore.FieldModel);
        Assert.Equal("namingPolicy", ProfileStore.FieldNamingPolicy);
    }

    // ── 核心字段（配置对话框外层「保存」直接编辑的输入框）──────────────

    [Fact]
    public void Save_RejectsEmptyName_WithFieldId()
    {
        var profile = Valid();
        profile.Name = "";
        var ex = Reject(profile);
        Assert.Equal("请填写配置名称。", ex.Message);
        Assert.Equal(ProfileStore.FieldName, ex.Field);
    }

    [Fact]
    public void Save_RejectsRelativeWorkspace_WithFieldId()
    {
        var profile = Valid();
        profile.WorkspaceRoot = "relative/path";
        var ex = Reject(profile);
        Assert.Equal("代码工作区必须是绝对路径。", ex.Message);
        Assert.Equal(ProfileStore.FieldWorkspaceRoot, ex.Field);
    }

    [Fact]
    public void Save_RejectsMissingWorkspace_WithFieldId()
    {
        var profile = Valid();
        profile.WorkspaceRoot = Path.Combine(_root, "no-such-dir");
        var ex = Reject(profile);
        Assert.Equal("代码工作区目录不存在。", ex.Message);
        Assert.Equal(ProfileStore.FieldWorkspaceRoot, ex.Field);
    }

    [Fact]
    public void Save_RejectsNonCsprojApiProject_WithFieldId()
    {
        var profile = Valid();
        profile.ApiProject = "Api.sln";
        var ex = Reject(profile);
        Assert.Equal("API 项目必须是 .csproj 文件。", ex.Message);
        Assert.Equal(ProfileStore.FieldApiProject, ex.Field);
    }

    [Fact]
    public void Save_RejectsApiProjectOutsideWorkspace_WithFieldId()
    {
        var profile = Valid();
        profile.ApiProject = "../outside.csproj";
        var ex = Reject(profile);
        Assert.Equal("API 项目必须位于代码工作区内。", ex.Message);
        Assert.Equal(ProfileStore.FieldApiProject, ex.Field);
    }

    [Fact]
    public void Save_RejectsMissingApiProjectFile_WithFieldId()
    {
        var profile = Valid();
        profile.ApiProject = "Missing.csproj";
        var ex = Reject(profile);
        Assert.Equal("API 项目文件不存在。", ex.Message);
        Assert.Equal(ProfileStore.FieldApiProject, ex.Field);
    }

    [Fact]
    public void Save_RejectsAllowedPathOutsideWorkspace_WithFieldId()
    {
        var profile = Valid();
        profile.AllowedSourcePaths = ["../outside"];
        var ex = Reject(profile);
        Assert.Equal("回写范围“../outside”不在代码工作区内。", ex.Message);
        Assert.Equal(ProfileStore.FieldAllowedSourcePaths, ex.Field);
    }

    [Fact]
    public void Save_RejectsNonDigitProjectId_WithFieldId()
    {
        var profile = Valid();
        profile.Apifox.ProjectId = "12a";
        var ex = Reject(profile);
        Assert.Equal("Apifox 项目 ID 应为数字。", ex.Message);
        Assert.Equal(ProfileStore.FieldProjectId, ex.Field);
    }

    [Fact]
    public void Save_RejectsMissingSwaggerFileLocation_WithFieldId()
    {
        var profile = Valid();
        profile.Swagger = new SwaggerSource { Type = "file", Location = null };
        var ex = Reject(profile);
        Assert.Equal("请填写 Swagger 文件路径。", ex.Message);
        Assert.Equal(ProfileStore.FieldSwaggerLocation, ex.Field);
    }

    [Fact]
    public void Save_RejectsInvalidSwaggerUrl_WithFieldId()
    {
        var profile = Valid();
        profile.Swagger = new SwaggerSource { Type = "url", Location = "not a url" };
        var ex = Reject(profile);
        Assert.Equal("Swagger 地址必须是 http/https 的 JSON 地址。", ex.Message);
        Assert.Equal(ProfileStore.FieldSwaggerLocation, ex.Field);
    }

    // ── 子设置字段（「模型服务」「序列化与分析」对话框编辑，外层「保存」统一校验）──

    [Fact]
    public void Save_RejectsInvalidSwaggerType_WithFieldId()
    {
        var profile = Valid();
        profile.Swagger = new SwaggerSource { Type = "bogus" };
        var ex = Reject(profile);
        Assert.Equal("Swagger 来源类型无效。", ex.Message);
        Assert.Equal(ProfileStore.FieldSwaggerType, ex.Field);
    }

    [Fact]
    public void Save_RejectsInvalidGenerationMode_WithFieldId()
    {
        var profile = Valid();
        profile.Generation.Mode = "bogus";
        var ex = Reject(profile);
        Assert.Equal("草稿生成方式无效。", ex.Message);
        Assert.Equal(ProfileStore.FieldGenerationMode, ex.Field);
    }

    [Fact]
    public void Save_RejectsOpenAiWithoutBaseUrl_WithFieldId()
    {
        var profile = Valid();
        profile.Generation.Mode = "openai";
        profile.Generation.BaseUrl = null;
        profile.Generation.Model = "模型名";
        var ex = Reject(profile);
        Assert.Equal("请填写模型服务地址。", ex.Message);
        Assert.Equal(ProfileStore.FieldBaseUrl, ex.Field);
    }

    [Fact]
    public void Save_RejectsOpenAiNonHttpsBaseUrl_WithFieldId()
    {
        var profile = Valid();
        profile.Generation.Mode = "openai";
        profile.Generation.BaseUrl = "http://example.com/v1";
        profile.Generation.Model = "模型名";
        var ex = Reject(profile);
        Assert.Equal("模型服务地址必须使用 https（本机地址除外）。", ex.Message);
        Assert.Equal(ProfileStore.FieldBaseUrl, ex.Field);
    }

    [Fact]
    public void Save_RejectsOpenAiWithoutModel_WithFieldId()
    {
        var profile = Valid();
        profile.Generation.Mode = "openai";
        profile.Generation.BaseUrl = "https://example.com/v1";
        profile.Generation.Model = " ";
        var ex = Reject(profile);
        Assert.Equal("请填写模型名称。", ex.Message);
        Assert.Equal(ProfileStore.FieldModel, ex.Field);
    }

    [Fact]
    public void Save_RejectsInvalidNamingPolicy_WithFieldId()
    {
        var profile = Valid();
        profile.Serialization.NamingPolicy = "PascalCase";
        var ex = Reject(profile);
        Assert.Equal("JSON 命名策略只支持 camelCase 或 none。", ex.Message);
        Assert.Equal(ProfileStore.FieldNamingPolicy, ex.Field);
    }

    // ── 保存边界与规整 ────────────────────────────────────────────────

    [Fact]
    public void Save_RejectedProfile_WritesNoFile()
    {
        var profile = Valid();
        profile.Name = "";
        Reject(profile);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "profiles"), "*.json"));
    }

    [Fact]
    public void Save_ValidProfile_PersistsAndRoundTrips()
    {
        var profile = Valid();
        profile.SourceVersionNote = null;
        profile.Generation.TimeoutSeconds = 5;
        profile.Apifox.ReadConcurrency = 99;
        profile.Analysis.MaxTraceDepth = 99;

        var saved = _store.Save(profile, isNew: true);

        Assert.StartsWith("p", saved.Id, StringComparison.Ordinal);
        Assert.NotEqual(default, saved.CreatedAt);
        Assert.NotEqual(default, saved.UpdatedAt);
        // 越界钳制在 Normalize 单一口径执行（10–600、1–8、0–6），本切片一行未动。
        Assert.Equal(10, saved.Generation.TimeoutSeconds);
        Assert.Equal(8, saved.Apifox.ReadConcurrency);
        Assert.Equal(6, saved.Analysis.MaxTraceDepth);
        Assert.Null(saved.SourceVersionNote);

        var reread = _store.Get(saved.Id);
        Assert.Equal(saved.Name, reread.Name);
        Assert.Equal(saved.WorkspaceRoot, reread.WorkspaceRoot);
        Assert.Equal(saved.ApiProject, reread.ApiProject);
    }
}
