using System.Text.Json.Serialization.Metadata;

namespace KuGou.Net.util;

/// <summary>Expose the existing SDK serializer without duplicating its generated JSON contract.</summary>
public static class SdkRequestSerialization {
    public static string Serialize(object body, JsonTypeInfo? bodyTypeInfo = null)
        => RequestBodyJsonSerializer.Serialize(body, bodyTypeInfo);
}
