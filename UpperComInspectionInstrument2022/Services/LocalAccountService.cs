using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>认证成功后返回给界面的非敏感账户信息。</summary>
    public sealed record LocalUserIdentity(string UserName, string DisplayName, DateTime CreatedAt);

    /// <summary>
    /// 本地离线账户服务。
    /// 账户保存到当前 Windows 用户的 LocalApplicationData，密码仅保存 PBKDF2-SHA256 盐值和摘要。
    /// </summary>
    public sealed class LocalAccountService
    {
        private const int StoreVersion = 1;
        private const int PasswordIterations = 210_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private readonly object _syncRoot = new();

        public static LocalAccountService Default { get; } = new();

        public LocalAccountService(string? storageDirectory = null)
        {
            StorageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndustrialEquipmentCalibration")
                : storageDirectory;
            AccountFilePath = Path.Combine(StorageDirectory, "users.json");
        }

        /// <summary>账户文件所在目录，便于故障提示和测试使用。</summary>
        public string StorageDirectory { get; }

        /// <summary>本机账户文件的完整路径。</summary>
        public string AccountFilePath { get; }

        /// <summary>检查是否已经创建至少一个本地账户。</summary>
        public bool TryHasAccounts(out bool hasAccounts, out string error)
        {
            lock (_syncRoot)
            {
                try
                {
                    hasAccounts = LoadStore().Accounts.Count > 0;
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (IsStorageException(ex))
                {
                    hasAccounts = false;
                    error = BuildStorageError("读取账户信息失败", ex);
                    return false;
                }
            }
        }

        /// <summary>首次使用时创建本地账户。</summary>
        public bool TryRegister(
            string userName,
            string displayName,
            string password,
            out LocalUserIdentity? identity,
            out string error) =>
            TrySaveAccount(userName, displayName, password, false, out identity, out error);

        /// <summary>
        /// 忘记密码时重新注册本机账户。该操作替换现有登录账户，但不接触任何校准业务数据。
        /// </summary>
        public bool TryReRegister(
            string userName,
            string displayName,
            string password,
            out LocalUserIdentity? identity,
            out string error) =>
            TrySaveAccount(userName, displayName, password, true, out identity, out error);

        /// <summary>校验注册资料并新增或替换本机账户。</summary>
        private bool TrySaveAccount(
            string userName,
            string displayName,
            string password,
            bool replaceExistingAccounts,
            out LocalUserIdentity? identity,
            out string error)
        {
            identity = null;
            string trimmedUserName = userName?.Trim() ?? string.Empty;
            string trimmedDisplayName = displayName?.Trim() ?? string.Empty;
            if (!TryValidateUserName(trimmedUserName, out error) ||
                !TryValidatePassword(password, out error))
                return false;
            if (trimmedDisplayName.Length > 32 || trimmedDisplayName.Any(char.IsControl))
            {
                error = "显示名称不能超过 32 个字符，也不能包含控制字符。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(trimmedDisplayName)) trimmedDisplayName = trimmedUserName;

            lock (_syncRoot)
            {
                try
                {
                    AccountStore store = LoadStore();
                    string normalizedName = NormalizeUserName(trimmedUserName);
                    if (!replaceExistingAccounts &&
                        store.Accounts.Any(item => item.NormalizedUserName == normalizedName))
                    {
                        error = "该用户名已经存在，请更换用户名。";
                        return false;
                    }

                    byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
                    byte[] hash = DerivePasswordHash(password, salt, PasswordIterations);
                    DateTime createdAt = DateTime.Now;
                    AccountRecord account = new()
                    {
                        UserName = trimmedUserName,
                        NormalizedUserName = normalizedName,
                        DisplayName = trimmedDisplayName,
                        PasswordSalt = Convert.ToBase64String(salt),
                        PasswordHash = Convert.ToBase64String(hash),
                        PasswordIterations = PasswordIterations,
                        CreatedAt = createdAt
                    };
                    if (replaceExistingAccounts) store.Accounts.Clear();
                    store.Accounts.Add(account);
                    SaveStore(store);
                    identity = new LocalUserIdentity(trimmedUserName, trimmedDisplayName, createdAt);
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (IsStorageException(ex))
                {
                    string prefix = replaceExistingAccounts ? "重新注册账户失败" : "注册账户失败";
                    error = BuildStorageError(prefix, ex);
                    return false;
                }
            }
        }

        /// <summary>验证用户名和密码，成功时更新最后登录时间并返回非敏感身份信息。</summary>
        public bool TryAuthenticate(
            string userName,
            string password,
            out LocalUserIdentity? identity,
            out string error)
        {
            identity = null;
            string normalizedName = NormalizeUserName(userName);
            if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrEmpty(password))
            {
                error = "请输入用户名和密码。";
                return false;
            }

            lock (_syncRoot)
            {
                try
                {
                    AccountStore store = LoadStore();
                    AccountRecord? account = store.Accounts.FirstOrDefault(
                        item => item.NormalizedUserName == normalizedName);
                    if (account == null || !VerifyPassword(account, password))
                    {
                        error = "用户名或密码错误。";
                        return false;
                    }

                    account.LastLoginAt = DateTime.Now;
                    SaveStore(store);
                    identity = new LocalUserIdentity(account.UserName, account.DisplayName, account.CreatedAt);
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (IsStorageException(ex))
                {
                    error = BuildStorageError("登录验证失败", ex);
                    return false;
                }
            }
        }

        /// <summary>检查用户名格式，允许中文等 Unicode 字母。</summary>
        private static bool TryValidateUserName(string userName, out string error)
        {
            if (userName.Length is < 2 or > 32)
            {
                error = "用户名长度应为 2～32 个字符。";
                return false;
            }
            if (userName.Any(character =>
                    !char.IsLetterOrDigit(character) &&
                    character != '_' && character != '-' && character != '.'))
            {
                error = "用户名只能包含字母、数字、汉字、点、横线和下划线。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>检查密码强度；密码不允许以任何形式写入错误信息或日志。</summary>
        private static bool TryValidatePassword(string password, out string error)
        {
            if (password == null || password.Length is < 8 or > 128)
            {
                error = "密码长度应为 8～128 个字符。";
                return false;
            }
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            {
                error = "密码必须同时包含字母和数字。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>从磁盘读取账户文件，并拒绝继续使用损坏或版本不兼容的数据。</summary>
        private AccountStore LoadStore()
        {
            if (!File.Exists(AccountFilePath)) return new AccountStore();

            AccountStore? store = JsonSerializer.Deserialize<AccountStore>(
                File.ReadAllText(AccountFilePath, Encoding.UTF8));
            if (store == null || store.Version != StoreVersion || store.Accounts == null)
                throw new InvalidDataException("账户文件格式或版本不受支持。");

            foreach (AccountRecord account in store.Accounts)
            {
                if (string.IsNullOrWhiteSpace(account.UserName) ||
                    string.IsNullOrWhiteSpace(account.NormalizedUserName) ||
                    string.IsNullOrWhiteSpace(account.DisplayName) ||
                    account.PasswordIterations < 100_000 ||
                    Convert.FromBase64String(account.PasswordSalt).Length < SaltSize ||
                    Convert.FromBase64String(account.PasswordHash).Length != HashSize)
                    throw new InvalidDataException("账户文件包含无效记录。");

            }
            return store;
        }

        /// <summary>先写临时文件再原子替换，降低程序异常退出造成账户文件损坏的概率。</summary>
        private void SaveStore(AccountStore store)
        {
            Directory.CreateDirectory(StorageDirectory);
            string temporaryPath = AccountFilePath + ".tmp";
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, AccountFilePath, true);
        }

        private static string NormalizeUserName(string? userName) =>
            (userName ?? string.Empty).Trim().ToUpperInvariant();

        private static byte[] DerivePasswordHash(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);

        private static bool VerifyPassword(AccountRecord account, string password)
        {
            byte[] salt = Convert.FromBase64String(account.PasswordSalt);
            byte[] expectedHash = Convert.FromBase64String(account.PasswordHash);
            byte[] actualHash = DerivePasswordHash(password, salt, account.PasswordIterations);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        private static bool IsStorageException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or FormatException or CryptographicException;

        private string BuildStorageError(string prefix, Exception exception) =>
            $"{prefix}：{exception.Message}\n账户文件：{AccountFilePath}";

        private sealed class AccountStore
        {
            public int Version { get; set; } = StoreVersion;
            public List<AccountRecord> Accounts { get; set; } = new();
        }

        private sealed class AccountRecord
        {
            public string UserName { get; set; } = string.Empty;
            public string NormalizedUserName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string PasswordSalt { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
            public int PasswordIterations { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? LastLoginAt { get; set; }
        }
    }
}
