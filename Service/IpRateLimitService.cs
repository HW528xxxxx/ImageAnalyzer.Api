using ComputerVision.Interface;
using Microsoft.Extensions.Caching.Memory;

namespace ComputerVision.Services
{
    public class IpRateLimitService : IIpRateLimitService
    {
        private readonly IMemoryCache _cache;

        public IpRateLimitService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public bool CheckLimit(string ip, int limitPerDay, out int remaining)
        {
            string key = $"RateLimit_{ip}_{DateTime.UtcNow:yyyyMMdd}";

            int count = _cache.GetOrCreate(key, entry =>
            {
                // 設定1天5次過期
                entry.AbsoluteExpiration = DateTime.UtcNow.Date.AddDays(1);
                return 0;
            });

            if (count > limitPerDay)
            {
                remaining = 0;
                return false;
            }

            _cache.Set(key, count + 1);
            remaining = limitPerDay - (count + 1);
            return true;
        }
    }
}
