using ComputerVision.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ComputerVision.Factories
{
    public static class ServiceFactory
    {
        public static LineNotifyService CreateLineNotifyService(IServiceProvider sp, string token, string userId)
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new LineNotifyService(httpFactory, token, userId);
        }

        public static AzureOpenAiTtsService CreateTtsService(IServiceProvider sp,
            string endpoint, string key, string deployName, string apiVersion)
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new AzureOpenAiTtsService(httpFactory, endpoint, key, deployName, apiVersion);
        }
    }
}
