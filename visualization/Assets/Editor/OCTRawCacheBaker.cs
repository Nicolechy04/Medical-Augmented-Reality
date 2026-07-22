using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot menu command that pre-bakes the raw OCT cache (see OctRawCache) for
/// every frame of "Subretinal Injection 1", so Play mode never has to PNG-decode
/// a cold frame. Uses the same cache format SubretinalSequencePlayer writes lazily
/// during normal playback, so baking ahead of time and lazy caching are interchangeable.
/// </summary>
public static class OCTRawCacheBaker
{
    [MenuItem("Tools/Subretinal Injection/Bake OCT Raw Cache (All Frames)")]
    public static void BakeAllFrames()
    {
        var player = Object.FindObjectOfType<SubretinalSequencePlayer>();
        string dataRoot = ResolveDataRoot(player);

        string volumeRoot = Path.Combine(dataRoot, "iOCT Microscope", "Volume");
        if (!Directory.Exists(volumeRoot))
        {
            EditorUtility.DisplayDialog("Bake OCT Raw Cache",
                $"Could not find:\n{volumeRoot}", "OK");
            return;
        }

        int bscanStep = player != null ? player.BscanStep : 4;

        string[] frameDirs = Directory.GetDirectories(volumeRoot);
        System.Array.Sort(frameDirs);

        int baked = 0, skipped = 0;
        try
        {
            for (int f = 0; f < frameDirs.Length; f++)
            {
                string frameDir  = frameDirs[f];
                string frameName = Path.GetFileName(frameDir);

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Baking OCT Raw Cache",
                        $"{frameName}  ({f + 1}/{frameDirs.Length})  baked={baked} skipped={skipped}",
                        (float)f / frameDirs.Length))
                    break;

                if (OctRawCache.TryRead(dataRoot, frameName, bscanStep, out _, out _, out _, out _))
                {
                    skipped++; // already cached for this BscanStep
                    continue;
                }

                string[] selected = OctRawCache.SelectBscans(frameDir, bscanStep);
                if (selected.Length == 0) continue;

                byte[] volBytes = OctRawCache.DecodeVolume(selected, out int w, out int h, out int depth);
                OctRawCache.Write(dataRoot, frameName, bscanStep, volBytes, w, h, depth);
                baked++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[OCTRawCacheBaker] Done. baked={baked} skipped={skipped} cacheDir={OctRawCache.CacheDir(dataRoot)}");
    }

    [MenuItem("Tools/Subretinal Injection/Clear OCT Raw Cache")]
    public static void ClearCache()
    {
        string dataRoot = ResolveDataRoot(Object.FindObjectOfType<SubretinalSequencePlayer>());
        string cacheDir = OctRawCache.CacheDir(dataRoot);

        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
            Debug.Log($"[OCTRawCacheBaker] Cleared: {cacheDir}");
        }
        else
        {
            Debug.Log($"[OCTRawCacheBaker] Nothing to clear at: {cacheDir}");
        }
    }

    // Mirrors SubretinalSequencePlayer.TryAutoDetectDataRoot so the baker finds the
    // same folder at edit time that the player would auto-detect at Play time.
    private static string ResolveDataRoot(SubretinalSequencePlayer player)
    {
        if (player != null && !string.IsNullOrEmpty(player.DataRootPath) && Directory.Exists(player.DataRootPath))
            return player.DataRootPath;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, "Subretinal Injection 1");
    }
}
