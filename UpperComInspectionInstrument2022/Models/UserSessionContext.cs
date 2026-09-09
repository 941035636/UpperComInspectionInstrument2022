using System;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 当前已登录用户的最小会话信息。
    /// 密码及密码摘要不会进入会话，避免业务页面接触认证凭据。
    /// </summary>
    public sealed record UserSession(string UserName, string DisplayName, DateTime SignedInAt);

    /// <summary>
    /// 保存当前进程的登录状态。应用退出或注销时会话立即清空，不写入磁盘。
    /// </summary>
    public static class UserSessionContext
    {
        public static UserSession? Current { get; private set; }

        public static bool IsAuthenticated => Current != null;

        /// <summary>在本地账户服务验证密码成功后建立会话。</summary>
        public static void SignIn(string userName, string displayName)
        {
            Current = new UserSession(userName, displayName, DateTime.Now);
        }

        /// <summary>清除当前会话；不会删除本地账户。</summary>
        public static void SignOut()
        {
            Current = null;
        }
    }
}
