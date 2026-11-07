namespace ApifoxHelper.Profiles;

/// <summary>
/// 模型服务密钥用 Windows DPAPI（当前用户）加密保存；不返回给页面，不进入任务资料、报告或导出包。
/// Apifox 凭据由 CLI 自己管理，本工具不读取。
/// </summary>
public sealed class CredentialStore(FileStore store)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ApifoxHelper.ModelKey.v1");

    public void SetModelKey(string profileId, string? key)
    {
        var path = PathOf(profileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        if (!OperatingSystem.IsWindows()) throw new UserException("当前系统不支持受保护凭据存储。");
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
        FileStore.WriteBytesAtomic(path, protectedBytes);
    }

    public bool HasModelKey(string profileId) => File.Exists(PathOf(profileId));

    public string? GetModelKey(string profileId)
    {
        var path = PathOf(profileId);
        if (!File.Exists(path) || !OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            throw new UserException("模型密钥无法解密（可能来自其他 Windows 账户），请重新填写。");
        }
    }

    private string PathOf(string profileId) => Path.Combine(store.CredentialsDir, Ids.Validate(profileId) + ".model.bin");
}
