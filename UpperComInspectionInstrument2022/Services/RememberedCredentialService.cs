using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 保存登录页“记住密码”状态。
    /// 密码通过 Windows DPAPI 按当前 Windows 用户加密，不以明文写入本地文件。
    /// </summary>
    public sealed class RememberedCredentialService
    {
        private const int StoreVersion = 1;
        private static readonly byte[] AdditionalEntropy =
            Encoding.UTF8.GetBytes("UpperComInspectionInstrument2022/remembered-login/v1");

        public static RememberedCredentialService Default { get; } = new();

        public RememberedCredentialService(string? storageDirectory = null)
        {
            StorageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndustrialEquipmentCalibration")
                : storageDirectory;
            CredentialFilePath = Path.Combine(StorageDirectory, "remembered-login.json");
        }

        public string StorageDirectory { get; }

        public string CredentialFilePath { get; }

        /// <summary>读取并解密已保存的用户名和密码；没有保存记录不视为错误。</summary>
        public bool TryLoad(
            out bool hasCredential,
            out string userName,
            out string password,
            out string error)
        {
            hasCredential = false;
            userName = string.Empty;
            password = string.Empty;
            if (!File.Exists(CredentialFilePath))
            {
                error = string.Empty;
                return true;
            }

            try
            {
                RememberedCredentialStore? store = JsonSerializer.Deserialize<RememberedCredentialStore>(
                    File.ReadAllText(CredentialFilePath, Encoding.UTF8));
                if (store == null || store.Version != StoreVersion ||
                    string.IsNullOrWhiteSpace(store.UserName) ||
                    string.IsNullOrWhiteSpace(store.ProtectedPassword))
                    throw new InvalidDataException("记住密码文件格式或版本不受支持。");

                byte[] encrypted = Convert.FromBase64String(store.ProtectedPassword);
                byte[] plain = ProtectedData.Unprotect(
                    encrypted, AdditionalEntropy, DataProtectionScope.CurrentUser);
                try
                {
                    userName = store.UserName;
                    password = Encoding.UTF8.GetString(plain);
                    hasCredential = true;
                    error = string.Empty;
                    return true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            catch (Exception ex) when (IsCredentialException(ex))
            {
                error = BuildError("读取记住密码失败", ex);
                return false;
            }
        }

        /// <summary>使用当前 Windows 用户的 DPAPI 密钥加密并保存密码。</summary>
        public bool TrySave(string userName, string password, out string error)
        {
            string trimmedUserName = userName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedUserName) || string.IsNullOrEmpty(password))
            {
                error = "用户名和密码不能为空。";
                return false;
            }

            try
            {
                Directory.CreateDirectory(StorageDirectory);
                byte[] plain = Encoding.UTF8.GetBytes(password);
                byte[] encrypted;
                try
                {
                    encrypted = ProtectedData.Protect(
                        plain, AdditionalEntropy, DataProtectionScope.CurrentUser);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }

                RememberedCredentialStore store = new()
                {
                    UserName = trimmedUserName,
                    ProtectedPassword = Convert.ToBase64String(encrypted)
                };
                string json = JsonSerializer.Serialize(
                    store, new JsonSerializerOptions { WriteIndented = true });
                string temporaryPath = CredentialFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                File.Move(temporaryPath, CredentialFilePath, true);
                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (IsCredentialException(ex))
            {
                error = BuildError("保存记住密码失败", ex);
                return false;
            }
        }

        /// <summary>删除已保存的加密凭据；不会删除账户或校准业务数据。</summary>
        public bool TryClear(out string error)
        {
            try
            {
                if (File.Exists(CredentialFilePath)) File.Delete(CredentialFilePath);
                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (IsCredentialException(ex))
            {
                error = BuildError("清除记住密码失败", ex);
                return false;
            }
        }

        private static bool IsCredentialException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or FormatException or CryptographicException or
                PlatformNotSupportedException;

        private string BuildError(string prefix, Exception exception) =>
            $"{prefix}：{exception.Message}\n凭据文件：{CredentialFilePath}";

        private sealed class RememberedCredentialStore
        {
            public int Version { get; set; } = StoreVersion;
            public string UserName { get; set; } = string.Empty;
            public string ProtectedPassword { get; set; } = string.Empty;
        }
    }
}
