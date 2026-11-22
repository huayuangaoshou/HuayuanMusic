using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HuayuanMusic.Provider.Kugou;

public sealed partial class DirectSdkGateway
{
    [StructLayout(LayoutKind.Sequential)]
    struct IeProxyConfig
    {
        [MarshalAs(UnmanagedType.Bool)] public bool AutoDetect;
        public IntPtr AutoConfig,Proxy,Bypass;
    }

    [DllImport("winhttp.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool WinHttpGetIEProxyConfigForCurrentUser(out IeProxyConfig config);

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalFree(IntPtr handle);

    // Read configuration without executing PAC or issuing WPAD discovery.
    static IWebProxy ReadLocalTrialProxy()
    {
        if(!WinHttpGetIEProxyConfigForCurrentUser(out var config))
            throw new UnauthorizedAccessException("Windows proxy configuration unavailable");
        try
        {
            if(config.AutoDetect||config.AutoConfig!=IntPtr.Zero)
                throw new UnauthorizedAccessException("Automatic proxy discovery outside trial scope");
            string server=Marshal.PtrToStringUni(config.Proxy)??"";
            if(server.Contains('='))
            {
                var entries=server.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                var http=entries.Where(s=>s.StartsWith("http=",StringComparison.OrdinalIgnoreCase)).ToArray();
                server=http.Length==1?http[0][5..]:"";
            }
            if(!Uri.TryCreate(server.Contains("://")?server:"http://"+server,UriKind.Absolute,out var address)||
               address.Scheme!="http"||address.UserInfo.Length!=0||address.AbsolutePath!="/"||
               address.Query.Length!=0||address.Fragment.Length!=0||address.Host.Length==0)
                throw new UnauthorizedAccessException("Explicit HTTP system proxy required");
            bool local=false;
            var rules=new List<string>();
            foreach(var item in (Marshal.PtrToStringUni(config.Bypass)??"").Split(';',
                StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            {
                if(item.Equals("<local>",StringComparison.OrdinalIgnoreCase)){local=true;continue;}
                if(item.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('.' or '*' or '-' or '_')))
                    throw new UnauthorizedAccessException("Unsupported Windows proxy bypass rule");
                rules.Add("^http://"+Regex.Escape(item).Replace("\\*",".*")+"(?::[0-9]+)?/?$");
            }
            return new WebProxy(address,local,rules.ToArray());
        }
        finally
        {
            foreach(var p in new[]{config.AutoConfig,config.Proxy,config.Bypass})
                if(p!=IntPtr.Zero)GlobalFree(p);
        }
    }
}

