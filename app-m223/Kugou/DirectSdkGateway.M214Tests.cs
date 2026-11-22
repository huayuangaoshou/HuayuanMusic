using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HuayuanMusic.Provider.Kugou;

// Pure provider-assembly SDK test, in-memory handlers only.
// No dependency on the window/UI project, real credentials, or live internet.
public sealed partial class DirectSdkGateway
{
    sealed class M214CountRoute(bool failSource=false):HttpMessageHandler
    {
        internal int SourceForwarded,VipForwarded;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(req.Method!=HttpMethod.Get||req.RequestUri?.Scheme!="https"||
               req.RequestUri.Port!=443||
               req.Headers.Contains("Cookie")||req.Headers.Contains("Authorization"))
                throw new UnauthorizedAccessException("M214 synthetic route shape");
            string response;
            if(req.RequestUri.Host=="gateway.kugou.com" &&
               req.RequestUri.AbsolutePath=="/v5/url")
            {
                Interlocked.Increment(ref SourceForwarded);
                if(failSource)throw new HttpRequestException("M214 synthetic source failure");
                response="{\"status\":1,\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"priv_status\":0,\"url\":[\"http://audio.kugoucdn.com/synthetic-only.wav\"]}";
            }
            else if(req.RequestUri.Host=="kugouvip.kugou.com" &&
                    req.RequestUri.AbsolutePath=="/v1/get_union_vip")
            {
                Interlocked.Increment(ref VipForwarded);
                response="{\"status\":1,\"data\":{\"is_vip\":1,\"busi_vip\":[]}}";
            }
            else throw new UnauthorizedAccessException("M214 unexpected SDK route");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){
                Content=new StringContent(response,Encoding.UTF8,"application/json")});
        }
    }

    public static async Task<(int Passed,int Cases,int SourceForwarded)> TestM214SourceBudgetOfflineAsync()
    {
        int passed=0,cases=0,sourceCount=0;
        void Check(string label,bool result)
        {
            cases++;
            if(!result)throw new InvalidOperationException("M214_SOURCE_"+label);
            passed++;
        }
        const string hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        async Task<(DirectSdkGateway Gateway,M214CountRoute Fixture,List<string> Stages)> NewAsync(
            bool fail=false,bool combined=false)
        {
            var fixture=new M214CountRoute(fail);
            var gate=new DirectSdkGateway(_=>Task.FromResult(false),
                ()=>Task.FromResult(true),fixture,"7",true,true,false,combined);
            var stages=new List<string>();
            gate.OneSongDiagnostic+=stages.Add;
            gate.catalog.Add(hash,new CatalogTrack(hash,"synthetic single","synthetic","album",0,0));
            if(combined)
            {
                var entitlement=await gate.EntitlementAsync(CancellationToken.None);
                if(!entitlement.Active)throw new InvalidOperationException("M214_SYNTHETIC_VIP_UNAVAILABLE");
            }
            return (gate,fixture,stages);
        }

        {
            var (gate,fixture,stages)=await NewAsync();
            using(gate)
            {
                var approved=await gate.PermissionAsync(hash,"standard",CancellationToken.None);
                Check("FIRST_PERMISSION_SUCCEEDS",approved.Allowed);
                Check("ONE_REQUEST_AFTER_FIRST",gate.SourceRequestCount==1&&fixture.SourceForwarded==1);
                bool denied=false;
                try{await gate.PermissionAsync(hash,"standard",CancellationToken.None);}
                catch(UnauthorizedAccessException){denied=true;}
                Check("SECOND_BLOCKED_BEFORE_SDK",denied&&fixture.SourceForwarded==1&&gate.SourceRequestCount==1);
                Check("BEGIN_DIAGNOSTIC_SINGLE",stages.Count(x=>x=="SDK_SINGLE_GETURL_BEGIN")==1);
                Check("PRE_GET_DENIAL_DIAGNOSTIC",stages.Count(x=>x=="SDK_SOURCE_BUDGET_EXHAUSTED_PRE_GET")==1);
                sourceCount+=fixture.SourceForwarded;
            }
        }
        {
            var (gate,fixture,stages)=await NewAsync();
            using(gate)
            {
                var first=gate.PermissionAsync(hash,"standard",CancellationToken.None);
                var second=gate.PermissionAsync(hash,"standard",CancellationToken.None);
                int permitted=0,rejected=0;
                foreach(var task in new[]{first,second})
                {
                    try {var p=await task;if(p.Allowed)permitted++;}
                    catch(UnauthorizedAccessException){rejected++;}
                }
                Check("PARALLEL_ONE_WINNER",permitted==1&&rejected==1);
                Check("PARALLEL_ONE_SOURCE_FORWARD",fixture.SourceForwarded==1&&gate.SourceRequestCount==1);
                sourceCount+=fixture.SourceForwarded;
            }
        }
        {
            var (gate,fixture,stages)=await NewAsync(fail:true);
            using(gate)
            {
                bool firstFailure=false,secondDenied=false;
                try{await gate.PermissionAsync(hash,"standard",CancellationToken.None);}
                catch(HttpRequestException){firstFailure=true;}
                try{await gate.PermissionAsync(hash,"standard",CancellationToken.None);}
                catch(UnauthorizedAccessException){secondDenied=true;}
                Check("NETWORK_FAILURE_CONSUMES_BUDGET",firstFailure&&secondDenied);
                Check("FAILED_REQUEST_NOT_RETRIED",fixture.SourceForwarded==1&&
                      gate.SourceRequestCount==1&&
                      stages.Count(x=>x=="SDK_SINGLE_GETURL_BEGIN")==1);
                sourceCount+=fixture.SourceForwarded;
            }
        }
        {
            var (gate,fixture,stages)=await NewAsync();
            using(gate)
            {
                using var canceled=new CancellationTokenSource();
                canceled.Cancel();
                bool stopped=false;
                try{await gate.PermissionAsync(hash,"standard",canceled.Token);}
                catch(OperationCanceledException){stopped=true;}
                Check("PRE_CANCELED_DOES_NOT_USE_BUDGET",stopped&&
                      gate.SourceRequestCount==0&&fixture.SourceForwarded==0);
                sourceCount+=fixture.SourceForwarded;
            }
        }
        {
            var (gate,fixture,stages)=await NewAsync(combined:true);
            using(gate)
            {
                var granted=await gate.PermissionAsync(hash,"standard",CancellationToken.None);
                bool denied=false;
                try{await gate.PermissionAsync(hash,"standard",CancellationToken.None);}
                catch(UnauthorizedAccessException){denied=true;}
                Check("COMBINED_VIP_MODE_ONE_SOURCE",granted.Allowed&&
                      denied&&fixture.SourceForwarded==1&&gate.SourceRequestCount==1&&
                      fixture.VipForwarded==1);
                sourceCount+=fixture.SourceForwarded;
            }
        }
        if(cases!=11||passed!=11||sourceCount!=4)
            throw new InvalidOperationException("M214_CASE_COUNT");
        return(passed,cases,sourceCount);
    }
}
