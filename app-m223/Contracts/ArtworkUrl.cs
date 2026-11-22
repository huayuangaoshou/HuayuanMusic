namespace HuayuanMusic.Provider.Contracts;

public static class ArtworkUrl
{
    private const string SizeMarker = "{size}";
    public static event Action<string>? Diagnostic;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var template = value.Trim()
            .Replace("%7Bsize%7D", SizeMarker, StringComparison.OrdinalIgnoreCase)
            .Replace("%7B0%7D", SizeMarker, StringComparison.OrdinalIgnoreCase)
            .Replace("{size}", SizeMarker, StringComparison.OrdinalIgnoreCase)
            .Replace("{0}", SizeMarker, StringComparison.OrdinalIgnoreCase);
        if (template.StartsWith("//", StringComparison.Ordinal))
            template = "https:" + template;
        if (!TryStaticUri(template.Replace(SizeMarker, "400"), out _))
        {
            Diagnostic?.Invoke("URL_REJECTED");
            return null;
        }
        if (template.Contains(SizeMarker, StringComparison.Ordinal))
        {
            // Size markers belong to the object path, never the origin.
            var pathStart = template.IndexOf('/', template.IndexOf("://", StringComparison.Ordinal) + 3);
            return pathStart >= 0 && !template[..pathStart].Contains(SizeMarker, StringComparison.Ordinal)
                ? template : null;
        }
        TryStaticUri(template, out var uri);
        return uri!.AbsoluteUri;
    }

    public static string? Resolve(string? value, int size)
    {
        var normalized = Normalize(value);
        return normalized == null ? null : normalized.Replace(SizeMarker,
            size.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static bool TryStaticUri(string url, out Uri? uri)
    {
        if (url.Length > 2048 || url.Any(char.IsControl) || url.Contains('%') ||
            !Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            uri = null;
            return false;
        }
        var host = uri.IdnHost;
        bool imageHost = host is "imge.kugou.com" or "imgessl.kugou.com" or "img.kugou.com" or "image.kugou.com";
        if (!imageHost && host.EndsWith(".kgimg.com", StringComparison.Ordinal))
        {
            var label = host[..^".kgimg.com".Length];
            if (label.StartsWith('p'))
            {
                var number = label.Length > 2 && label.EndsWith("fx", StringComparison.Ordinal) ? label[1..^2] : label[1..];
                imageHost = number.Length is >= 1 and <= 2 && number.All(char.IsAsciiDigit);
            }
        }
        var path = uri.AbsolutePath;
        return imageHost && uri.Scheme is "http" or "https" && uri.IsDefaultPort &&
            uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            !uri.AbsolutePath.Contains('@') &&
            new[] { ".jpg", ".jpeg", ".png", ".webp" }.Any(extension =>
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static void Validate(string? value)
    {
        if (value != null && Normalize(value) != value)
            throw new ArgumentException("Unsafe cover metadata");
    }
}
