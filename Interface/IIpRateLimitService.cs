namespace ComputerVision.Interface
{
    public interface IIpRateLimitService
    {
        /// <summary>
        /// 檢查指定 IP 是否超過每日限制
        /// </summary>
        /// <param name="ip">呼叫者 IP</param>
        /// <param name="limitPerDay">每日限制次數</param>
        /// <param name="remaining">剩餘次數</param>
        /// <returns>是否允許呼叫</returns>
        bool CheckLimit(string ip, int limitPerDay, out int remaining);
    }
}