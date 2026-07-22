using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// On-disk raw-byte cache for decoded OCT B-scan volumes, keyed by frame + BscanStep.
///
/// PNG decoding (256 slices/frame at 512x512) is the actual cost of a frame load,
/// not disk size — PNG is already compressed. This cache skips that decode on every
/// visit after the first by storing the flat R8 voxel buffer directly, so a cache
/// hit is a single File.ReadAllBytes instead of 256 Texture2D.LoadImage calls.
///
/// SubretinalSequencePlayer writes a frame's cache lazily the first time it decodes
/// it; OCTRawCacheBaker (Editor-only) can pre-bake every frame ahead of time so Play
/// mode never hits a cold frame. Both go through this class so the format can't drift.
/// </summary>
public static class OctRawCache
{
    private const int Magic = 0x4354434F; // "OCTC" little-endian

    public static string CacheDir(string dataRootPath) => Path.Combine(dataRootPath, "_RawCache");

    public static string CachePath(string dataRootPath, string frameName, int bscanStep) =>
        Path.Combine(CacheDir(dataRootPath), $"{frameName}_step{bscanStep}.bin");

    public static bool TryRead(
        string dataRootPath, string frameName, int bscanStep,
        out byte[] volBytes, out int w, out int h, out int depth)
    {
        volBytes = null; w = h = depth = 0;

        string path = CachePath(dataRootPath, frameName, bscanStep);
        if (!File.Exists(path)) return false;

        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadInt32() != Magic) return false;

            w = reader.ReadInt32();
            h = reader.ReadInt32();
            depth = reader.ReadInt32();
            int cachedStep = reader.ReadInt32();
            if (cachedStep != bscanStep) return false;

            volBytes = reader.ReadBytes(w * h * depth);
            return volBytes.Length == w * h * depth;
        }
        catch (IOException)
        {
            // Corrupt or half-written cache file (e.g. a crashed bake) — treat as a miss.
            volBytes = null;
            return false;
        }
    }

    public static void Write(
        string dataRootPath, string frameName, int bscanStep,
        byte[] volBytes, int w, int h, int depth)
    {
        Directory.CreateDirectory(CacheDir(dataRootPath));

        // Write to a temp file and move into place, so a reader never sees a
        // partially-written cache file if the write is interrupted mid-frame.
        string finalPath = CachePath(dataRootPath, frameName, bscanStep);
        string tempPath  = finalPath + ".tmp";

        using (var writer = new BinaryWriter(File.Create(tempPath)))
        {
            writer.Write(Magic);
            writer.Write(w);
            writer.Write(h);
            writer.Write(depth);
            writer.Write(bscanStep);
            writer.Write(volBytes, 0, w * h * depth);
        }

        File.Copy(tempPath, finalPath, overwrite: true);
        File.Delete(tempPath);
    }

    /// Selects every Nth B-scan PNG from a frame folder — same ordering LoadFrame uses,
    /// so a cache built here always matches what the runtime loader would have decoded.
    public static string[] SelectBscans(string frameDir, int bscanStep)
    {
        return Directory.GetFiles(frameDir, "*.png")
            .OrderBy(Path.GetFileNameWithoutExtension)
            .Where((_, i) => i % bscanStep == 0)
            .ToArray();
    }

    /// Blocking PNG -> flat R8 volume decode, used by the Editor pre-baker. The
    /// runtime loader keeps its own yielding coroutine loop for progress reporting.
    public static byte[] DecodeVolume(string[] selected, out int w, out int h, out int depth)
    {
        depth = selected.Length;

        var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        probe.LoadImage(File.ReadAllBytes(selected[0]));
        w = probe.width;
        h = probe.height;
        Object.DestroyImmediate(probe);

        var volBytes = new byte[w * h * depth];
        for (int i = 0; i < depth; i++)
        {
            var slice = new Texture2D(w, h, TextureFormat.RGBA32, false);
            slice.LoadImage(File.ReadAllBytes(selected[i]));
            Color32[] px = slice.GetPixels32();
            int offset = i * w * h;
            for (int p = 0; p < px.Length; p++)
                volBytes[offset + p] = px[p].r;
            Object.DestroyImmediate(slice);
        }

        return volBytes;
    }
}
