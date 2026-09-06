using Mirror;

namespace TPSShooter
{
    /// <summary>
    /// Mirror 会话状态查询，避免业务代码直接散落 NetworkServer/NetworkClient 判断。
    /// </summary>
    public static class GameNetwork
    {
        /// <summary>
        /// 当前是否处于联机（Host / Server / Client）。
        /// </summary>
        public static bool IsActive
        {
            get { return NetworkServer.active || NetworkClient.active; }
        }

        /// <summary>
        /// 当前进程是否作为 Server 或 Host 运行。
        /// </summary>
        public static bool IsServer
        {
            get { return NetworkServer.active; }
        }

        /// <summary>
        /// 当前进程是否为纯客户端（不含 Host）。
        /// </summary>
        public static bool IsClientOnly
        {
            get { return NetworkClient.active && !NetworkServer.active; }
        }
    }
}
