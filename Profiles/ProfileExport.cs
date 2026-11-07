namespace ApifoxHelper.Profiles;

/// <summary>
/// 配置导出的单一口径：REST 下载（/api/profiles/{id}/export）与原生「导出配置（不含凭据）」另存为
/// 共用同一份文件名与字节（JsonDefaults.Options 序列化，UTF-8 无 BOM，与配置落盘同一形状）。
/// 配置对象本身不含凭据：模型服务密钥按配置 ID 存放在 CredentialStore，从不进入配置 JSON。
/// 只依赖 BCL，测试工程共享编译。
/// </summary>
public static class ProfileExport
{
    public const string ContentType = "application/json";

    public static string FileName(ProjectProfile profile) => $"apifox-helper-profile-{profile.Id}.json";

    public static byte[] Render(ProjectProfile profile) => JsonSerializer.SerializeToUtf8Bytes(profile, JsonDefaults.Options);
}
