using System.Buffers.Binary;
using System.Text;

namespace HuayuanMusic.Provider.Kugou;

public static class DesktopSongDownload
{
    public static async Task<string> SaveAsync(KugouProvider provider, CatalogTrack track, string preferred,
        string directory, Action<int> progress, CancellationToken cancellation)
    {
        string quality = preferred;
        if (preferred != "standard")
        {
            try
            {
                quality = await provider.UseDesktopCatalogAsync(
                    gateway => gateway.ReadDesktopQualityAsync(track, preferred, cancellation), cancellation);
            }
            catch (Exception error) when (CanRetry(error, cancellation))
            {
                quality = "standard";
            }
        }
        try { return await SaveQualityAsync(quality); }
        catch (Exception error) when (quality != "standard" && CanRetry(error, cancellation))
        {
            return await SaveQualityAsync("standard");
        }

        async Task<string> SaveQualityAsync(string effective)
        {
            var grant = await provider.ResolveAsync(track.Id, effective, cancellation);
            try
            {
                using var audio = await provider.OpenSdkAudioAsync(grant, cancellation);
                var extension = Identify(audio.Stream);
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        byte[] buffer = new byte[65536];
                        int reported = 0;
                        int length;
                        while ((length = await audio.Stream.ReadAsync(buffer, cancellation)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, length), cancellation);
                            int percent = (int)(file.Length * 100 / audio.Length);
                            if (percent >= reported + 5)
                            {
                                reported = percent;
                                progress(Math.Min(99, percent));
                            }
                        }
                        if (file.Length != audio.Length) throw new InvalidDataException("Incomplete audio download");
                    }

                    cancellation.ThrowIfCancellationRequested();
                    provider.DemandGrant(grant);
                    var stem = FileStem(track.Artist, track.Title);
                    for (int i = 1; i <= 1000; i++)
                    {
                        string name = i == 1 ? stem : $"{stem} ({i})";
                        string destination = Path.Combine(directory, name + extension);
                        try
                        {
                            File.Move(temporary, destination);
                            progress(100);
                            return destination;
                        }
                        catch (IOException) when (File.Exists(destination)) { }
                    }
                    throw new IOException("No available download filename");
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            finally { provider.ReleaseGrant(grant); }
        }
    }

    private static bool CanRetry(Exception error, CancellationToken cancellation) =>
        !cancellation.IsCancellationRequested &&
        error is HttpRequestException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException;

    public static string FileStem(string artist, string title)
    {
        var prefix = artist.Trim() + " - ";
        if (!string.IsNullOrWhiteSpace(artist) && title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            title = title[prefix.Length..];
        string name = string.IsNullOrWhiteSpace(artist) ? title : artist + " - " + title;
        var forbidden = Path.GetInvalidFileNameChars();
        var result = new StringBuilder();
        foreach (var rune in name.Normalize().EnumerateRunes())
        {
            if (result.Length + rune.Utf16SequenceLength > 140) break;
            if (rune.Value < 32 || rune.Value <= char.MaxValue && forbidden.Contains((char)rune.Value))
                result.Append('_');
            else
                result.Append(rune.ToString());
        }
        string stem = result.ToString().Trim().TrimEnd('.');
        if (stem.Length == 0) return "未知歌曲";
        var first = stem.Split('.')[0].TrimEnd().ToUpperInvariant();
        return first is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            first.Length == 4 && (first.StartsWith("COM") || first.StartsWith("LPT")) &&
            "123456789¹²³".Contains(first[3]) ? "_" + stem : stem;
    }

    public static string Identify(Stream audio)
    {
        Span<byte> head = stackalloc byte[64];
        int count = audio.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        audio.Position = 0;
        var bytes = head[..count];
        if (bytes.StartsWith("fLaC"u8) && count >= 42 && (head[4] & 0x7f) == 0 &&
            head[5] == 0 && head[6] == 0 && head[7] == 34) return ".flac";
        if (bytes.StartsWith("OggS"u8) && count >= 36 && head[4] == 0 && (head[5] & 2) != 0)
        {
            int body = 27 + head[26];
            if (body < count && (bytes[body..].StartsWith("OpusHead"u8) ||
                bytes[body..].StartsWith("\x01vorbis"u8))) return ".ogg";
        }
        if (count >= 16 && bytes[4..8].SequenceEqual("ftyp"u8) && HasClearM4aAudio(audio))
            return ".m4a";

        try
        {
            if (count >= 10 && bytes.StartsWith("ID3"u8))
            {
                if (head[3] is < 2 or > 4 || (head[6] | head[7] | head[8] | head[9]) >= 128)
                    throw new InvalidDataException("Invalid ID3 header");
                audio.Position = 10 + (head[3] == 4 && (head[5] & 16) != 0 ? 10 : 0) +
                    ((head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9]);
                count = audio.Position + 4 <= audio.Length ? audio.ReadAtLeast(head[..4], 4, throwOnEndOfStream: false) : 0;
            }
            if (count >= 4 && head[0] == 0xFF && (head[1] is 0xFB or 0xFA or 0xF3 or 0xF2 or 0xE3 or 0xE2) &&
                (head[2] >> 4 is > 0 and < 15) && (head[2] & 12) != 12) return ".mp3";
        }
        finally { audio.Position = 0; }
        throw new InvalidDataException("Unknown or unsupported audio format");
    }

    private static bool HasClearM4aAudio(Stream audio)
    {
        try
        {
            bool hasAudio = false;
            foreach (var track in Children(Boxes(0, audio.Length).FirstOrDefault(box => box.Name == "moov")))
            {
                if (track.Name != "trak") continue;
                var media = Children(track).FirstOrDefault(box => box.Name == "mdia");
                var handler = Children(media).FirstOrDefault(box => box.Name == "hdlr");
                var type = new byte[4];
                if (handler.End - handler.Start < 12) continue;
                audio.Position = handler.Start + 8;
                audio.ReadExactly(type);
                if (type.AsSpan().SequenceEqual("vide"u8)) return false;
                if (!type.AsSpan().SequenceEqual("soun"u8)) continue;
                var info = Children(media).FirstOrDefault(box => box.Name == "minf");
                var sample = Children(info).FirstOrDefault(box => box.Name == "stbl");
                var descriptions = Children(sample).FirstOrDefault(box => box.Name == "stsd");
                if (descriptions.End - descriptions.Start < 16) continue;
                var codecs = Boxes(descriptions.Start + 8, descriptions.End).Select(box => box.Name).ToArray();
                if (codecs.Length > 0 && codecs.All(codec => codec is "mp4a" or "alac")) hasAudio = true;
            }
            return hasAudio;
        }
        finally { audio.Position = 0; }

        IEnumerable<(string Name, long Start, long End)> Boxes(long start, long end)
        {
            byte[] header = new byte[8];
            for (long offset = start; offset <= end - 8;)
            {
                audio.Position = offset;
                audio.ReadExactly(header);
                var size = BinaryPrimitives.ReadUInt32BigEndian(header);
                var name = Encoding.ASCII.GetString(header, 4, 4);
                long actualSize = size;
                int headerSize = 8;
                if (size == 1)
                {
                    if (end - offset < 16) yield break;
                    audio.ReadExactly(header);
                    actualSize = BinaryPrimitives.ReadInt64BigEndian(header);
                    headerSize = 16;
                }
                else if (size == 0) actualSize = end - offset;
                if (actualSize < headerSize || actualSize > end - offset) yield break;
                yield return (name, offset + headerSize, offset + actualSize);
                offset += actualSize;
            }
        }

        IEnumerable<(string Name, long Start, long End)> Children((string Name, long Start, long End) box) =>
            Boxes(box.Start, box.End);
    }
}
