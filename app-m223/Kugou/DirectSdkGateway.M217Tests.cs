using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HuayuanMusic.Provider.Kugou;

public sealed partial class DirectSdkGateway
{
    public static async Task<(int Cases,int WireRequests)> TestM217MediaBoundaryAsync()
    {
        const string target="http://audio.kugoucdn.com/m217-test.wav";
        int wire=0;
        foreach(int status in new[]{302,307,0})
        {
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            var proxy=new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            var serve=Task.Run(async ()=>{
                using var peer=await listener.AcceptTcpClientAsync(limit.Token);
                using var stream=peer.GetStream();
                using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);
                var line=await reader.ReadLineAsync(limit.Token);
                if(line!=$"GET {target} HTTP/1.1")throw new InvalidOperationException("Unexpected proxy request");
                while((line=await reader.ReadLineAsync(limit.Token)) is {Length:>0})
                    if(line.StartsWith("Cookie:",StringComparison.OrdinalIgnoreCase)||
                       line.StartsWith("Authorization:",StringComparison.OrdinalIgnoreCase)||
                       line.StartsWith("Proxy-Authorization:",StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Sensitive proxy header");
                Interlocked.Increment(ref wire);
                if(status!=0)
                {
                    var location=status==302?"http://redirect.invalid/loop":target;
                    var response=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Redirect\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response,limit.Token);
                }
                // Without a response, disposing the peer simulates an abrupt proxy drop.
            });
            using var client=new HttpClient(NewOriginalHttpMediaTransport(new SyntheticProxy(proxy)));
            try
            {
                using var res=await client.GetAsync(target,limit.Token);
                if(status==0||(int)res.StatusCode!=status)
                    throw new InvalidOperationException("Redirect followed or disconnect accepted");
            }
            catch(HttpRequestException) when(status==0) { }
            await serve;
            try
            {
                using var res=await client.GetAsync(target,limit.Token);
                throw new InvalidOperationException("Media budget reused");
            }
            catch(UnauthorizedAccessException) { }
            if(listener.Pending())throw new InvalidOperationException("Second socket reached proxy");
        }
        using(var denied=new HttpClient(NewOriginalHttpMediaTransport(new SyntheticProxy(null,true))))
        {
            try{using var res=await denied.GetAsync(target);throw new InvalidOperationException("Proxy bypass accepted");}
            catch(UnauthorizedAccessException) { }
        }
        using(var denied=new HttpClient(new F3HttpMediaGuard(new F3ProbeInner())))
        {
            try{using var res=await denied.GetAsync("http://foreign.invalid/file.wav");throw new InvalidOperationException("Foreign source accepted");}
            catch(UnauthorizedAccessException) { }
        }
        if(wire!=3)throw new InvalidOperationException("Wrong proxy wire count");
        await TestSingleSdkSocketAsync();
        return (6,wire+1);
    }

    static async Task TestSingleSdkSocketAsync()
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        var endpoint=new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/sdk-dry");
        var serve=Task.Run(async ()=>{
            using var peer=await listener.AcceptTcpClientAsync(limit.Token);
            using var reader=new StreamReader(peer.GetStream(),Encoding.ASCII);
            if(await reader.ReadLineAsync(limit.Token)!="GET /sdk-dry HTTP/1.1")
                throw new InvalidOperationException("SDK loopback request missing");
            while(await reader.ReadLineAsync(limit.Token) is {Length:>0}) { }
            // No HTTP response. The normal SDK socket handler must not reconnect.
        });
        using var client=new HttpClient(NewSdkSockets(true));
        using var request=new HttpRequestMessage(HttpMethod.Get,endpoint);
        SetSingleConnection(request,endpoint);
        try
        {
            using var response=await client.SendAsync(request,limit.Token);
            throw new InvalidOperationException("SDK disconnect accepted");
        }
        catch(HttpRequestException) { }
        await serve;
        if(listener.Pending())throw new InvalidOperationException("SDK retried on a second socket");
    }
}
