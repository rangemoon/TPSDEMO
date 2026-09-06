using Mirror;

namespace TPSShooter
{
    /// <summary>
    /// Host 向所有端广播的对局会话状态。
    /// </summary>
    public struct GameSessionStateMessage : NetworkMessage
    {
        public bool paused;
        public bool finished;
        public bool isWin;
    }

    /// <summary>
    /// 客户端请求 Host 暂停对局。
    /// </summary>
    public struct GamePauseRequestMessage : NetworkMessage
    {
    }

    /// <summary>
    /// 客户端请求 Host 恢复对局。
    /// </summary>
    public struct GameResumeRequestMessage : NetworkMessage
    {
    }

    /// <summary>
    /// 客户端请求 Host 重开当前关卡。
    /// </summary>
    public struct GameReplayRequestMessage : NetworkMessage
    {
    }

    /// <summary>
    /// 客户端请求 Host 返回菜单。
    /// </summary>
    public struct GameLoadHomeRequestMessage : NetworkMessage
    {
    }
}
