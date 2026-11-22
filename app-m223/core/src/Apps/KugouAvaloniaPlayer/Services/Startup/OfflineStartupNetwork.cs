using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Infrastructure.Http;
namespace KugouAvaloniaPlayer.Services.Startup;
internal static class OfflineStartupNetwork
{
    internal static int Requests;
    internal static HttpClient MockClient()=>new(new TerminalMock());
    internal static HttpClient AudioClient()=>OfflineStartupProfile.Active?MockClient():new HttpClient(KgPrimaryHandler.Create()){Timeout=Timeout.InfiniteTimeSpan};
    internal static HttpClient ImageClient()=>OfflineStartupProfile.Active?MockClient():new HttpClient(KgPrimaryHandler.Create());
    internal static HttpClient FallbackClient()=>OfflineStartupProfile.Active?MockClient():new HttpClient();
    private sealed class TerminalMock:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"status\":0,\"error_code\":9999,\"data\":{}}")});
        }
    }
}
