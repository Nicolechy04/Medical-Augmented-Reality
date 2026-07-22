using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Data structs
// ─────────────────────────────────────────────────────────────────────────────

public enum DeformationPhase
{
    PreDeformation,   // before t_start_deformation
    Deforming,        // t_start <= frame < t_peak
    PeakDeformation,  // frame >= t_peak
    PostDeformation   // frame > t_end (only if t_end < t_peak in some sequences)
}

/// <summary>One time-step: matched B-scan + robot-log entry + optional segmentation.</summary>
public struct PigEyeFrame
{
    public int    Index;          // frame number from filename (b_scans000052_...)
    public double Timestamp;      // unix timestamp from filename
    public string BscanPath;      // absolute path to .jpg
    public string SegPath;        // absolute path to seg .png, or null
    public float  TipForceNorm;   // TipForceNorm_mN from robot log
    public float  NeedleTipX;     // pixel x in B-scan coordinate
    public float  NeedleTipY;     // pixel y in B-scan coordinate
    public bool   ValidForce;     // ValidTipForce column != 0
}

/// <summary>Ground-truth deformation timing from deformation_labels.json.</summary>
[Serializable]
public class PigEyeLabels
{
    public string SequenceName;
    public int    TotalFrames;
    public int    TStart;   // t_start_deformation
    public int    TPeak;    // t_peak_deformation
    public int    TEnd;     // t_end_deformation
}

// ─────────────────────────────────────────────────────────────────────────────
//  Loader
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Parses one pig-eye sequence folder and produces a list of PigEyeFrames,
/// each matched to the nearest robot-log entry by timestamp.
///
/// Expected folder layout:
///   <sequenceFolder>/
///     b_scans000000_<timestamp>.jpg   (B-scan images)
///     <name>-RobotLog_with_tip.csv    (force + needle-tip sensor log)
///     deformation_labels.json         (ground-truth timing)
///   <sequenceFolder>_segmentation/
///     <name>_segmentation000.png      (per-frame segmentation masks)
/// </summary>
public static class PigEyeDataLoader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── Public entry point ────────────────────────────────────────────────

    public static List<PigEyeFrame> LoadSequence(string sequenceFolder,
                                                  out PigEyeLabels labels)
    {
        labels = null;
        sequenceFolder = Path.GetFullPath(sequenceFolder);

        if (!Directory.Exists(sequenceFolder))
        {
            Debug.LogError($"[PigEyeLoader] Folder not found: {sequenceFolder}");
            return null;
        }

        // 1 — Robot log
        List<RobotLogRow> robotLog = LoadRobotLog(sequenceFolder);
        if (robotLog == null || robotLog.Count == 0)
        {
            Debug.LogError("[PigEyeLoader] Robot log not found or empty.");
            return null;
        }

        // 2 — Deformation labels (optional but very useful)
        labels = LoadDeformationLabels(sequenceFolder);

        // 3 — Segmentation folder (sibling directory ending in _segmentation)
        string segFolder = FindSegmentationFolder(sequenceFolder);

        // 4 — B-scan images → match each to nearest robot-log row
        List<PigEyeFrame> frames = MatchBscansToLog(sequenceFolder, segFolder, robotLog);

        Debug.Log($"[PigEyeLoader] Loaded {frames.Count} frames from '{Path.GetFileName(sequenceFolder)}'" +
                  (labels != null ? $", deformation t_start={labels.TStart} t_peak={labels.TPeak}" : ""));

        return frames;
    }

    // ── Deformation phase helper ──────────────────────────────────────────

    public static DeformationPhase GetPhase(int frameIndex, PigEyeLabels labels)
    {
        if (labels == null) return DeformationPhase.PreDeformation;
        if (frameIndex < labels.TStart)  return DeformationPhase.PreDeformation;
        if (frameIndex < labels.TPeak)   return DeformationPhase.Deforming;
        if (frameIndex <= labels.TEnd)   return DeformationPhase.PeakDeformation;
        return DeformationPhase.PostDeformation;
    }

    /// <summary>Normalises TipForceNorm_mN to [0, 1] clamped.</summary>
    public static float NormalizeForce(float tipForceNorm_mN, float maxForceMN)
    {
        return Mathf.Clamp01(Mathf.Abs(tipForceNorm_mN) / Mathf.Max(maxForceMN, 1e-6f));
    }

    // ── Robot log ─────────────────────────────────────────────────────────

    private struct RobotLogRow
    {
        public double Timestamp;
        public float  TipForceNorm;
        public float  NeedleTipX;
        public float  NeedleTipY;
        public bool   ValidForce;
    }

    private static List<RobotLogRow> LoadRobotLog(string folder)
    {
        // Find *RobotLog_with_tip.csv (prefer the one with "calibration_results" in name last)
        string[] csvFiles = Directory.GetFiles(folder, "*RobotLog_with_tip.csv", SearchOption.TopDirectoryOnly);
        if (csvFiles.Length == 0) return null;

        // Prefer the plain "with_tip" over "with_tip_calibration_results"
        string csvPath = null;
        foreach (string f in csvFiles)
        {
            if (!f.Contains("calibration_results")) { csvPath = f; break; }
        }
        if (csvPath == null) csvPath = csvFiles[0];

        List<RobotLogRow> rows = new List<RobotLogRow>();

        using (StreamReader sr = new StreamReader(csvPath))
        {
            string headerLine = sr.ReadLine();
            if (headerLine == null) return rows;

            // Strip leading '#' from header
            if (headerLine.StartsWith("#")) headerLine = headerLine.Substring(1);
            string[] headers = headerLine.Split(',');

            int idxTimestamp    = Array.IndexOf(headers, "TimeStamp");
            int idxValidForce   = Array.IndexOf(headers, "ValidTipForce");
            int idxTipForceNorm = Array.IndexOf(headers, "TipForceNorm_mN");
            int idxNeedleX      = Array.IndexOf(headers, "needle_tip_x");
            int idxNeedleY      = Array.IndexOf(headers, "needle_tip_y");

            if (idxTimestamp < 0 || idxTipForceNorm < 0 || idxNeedleX < 0 || idxNeedleY < 0)
            {
                Debug.LogError("[PigEyeLoader] Required columns missing from robot log CSV.");
                return null;
            }

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] cols = line.Split(',');
                if (cols.Length <= idxNeedleY) continue;

                RobotLogRow row;
                row.Timestamp    = ParseDouble(cols[idxTimestamp]);
                row.ValidForce   = idxValidForce >= 0 && ParseFloat(cols[idxValidForce]) != 0f;
                row.TipForceNorm = ParseFloat(cols[idxTipForceNorm]);
                row.NeedleTipX   = ParseFloat(cols[idxNeedleX]);
                row.NeedleTipY   = ParseFloat(cols[idxNeedleY]);
                rows.Add(row);
            }
        }

        return rows;
    }

    // ── Deformation labels ────────────────────────────────────────────────

    [Serializable]
    private class LabelsJson
    {
        public string sequence_name;
        public int    total_frames;
        public LabelsBlock labels;

        [Serializable]
        public class LabelsBlock
        {
            public int t_start_deformation;
            public int t_peak_deformation;
            public int t_end_deformation;
        }
    }

    private static PigEyeLabels LoadDeformationLabels(string folder)
    {
        string jsonPath = Path.Combine(folder, "deformation_labels.json");
        if (!File.Exists(jsonPath)) return null;

        try
        {
            string json = File.ReadAllText(jsonPath);
            LabelsJson raw = JsonUtility.FromJson<LabelsJson>(json);
            if (raw == null) return null;

            return new PigEyeLabels
            {
                SequenceName = raw.sequence_name,
                TotalFrames  = raw.total_frames,
                TStart       = raw.labels?.t_start_deformation ?? 0,
                TPeak        = raw.labels?.t_peak_deformation  ?? 0,
                TEnd         = raw.labels?.t_end_deformation   ?? 0
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PigEyeLoader] Could not parse deformation_labels.json: {e.Message}");
            return null;
        }
    }

    // ── Segmentation folder ───────────────────────────────────────────────

    private static string FindSegmentationFolder(string sequenceFolder)
    {
        string candidate = sequenceFolder + "_segmentation";
        return Directory.Exists(candidate) ? candidate : null;
    }

    // ── B-scan matching ───────────────────────────────────────────────────

    private static List<PigEyeFrame> MatchBscansToLog(
        string sequenceFolder, string segFolder, List<RobotLogRow> robotLog)
    {
        // Collect and sort B-scan files by frame index
        string[] jpgFiles = Directory.GetFiles(sequenceFolder, "b_scans*.jpg");
        Array.Sort(jpgFiles, (a, b) =>
            ExtractFrameIndex(a).CompareTo(ExtractFrameIndex(b)));

        // Build sorted timestamp array from robot log for binary search
        double[] logTimestamps = new double[robotLog.Count];
        for (int i = 0; i < robotLog.Count; i++) logTimestamps[i] = robotLog[i].Timestamp;

        List<PigEyeFrame> frames = new List<PigEyeFrame>(jpgFiles.Length);

        foreach (string bscanPath in jpgFiles)
        {
            string filename = Path.GetFileNameWithoutExtension(bscanPath);
            int frameIndex = ExtractFrameIndex(filename);
            double ts = ExtractTimestamp(filename);

            // Find nearest robot-log row
            int logIdx = NearestIndex(logTimestamps, ts);
            RobotLogRow logRow = robotLog[logIdx];

            // Find segmentation file (3-digit index suffix)
            string segPath = null;
            if (segFolder != null)
            {
                string segName = Path.GetFileName(sequenceFolder) + $"_segmentation{frameIndex:D3}.png";
                string candidate = Path.Combine(segFolder, segName);
                if (File.Exists(candidate)) segPath = candidate;
            }

            frames.Add(new PigEyeFrame
            {
                Index        = frameIndex,
                Timestamp    = ts,
                BscanPath    = bscanPath,
                SegPath      = segPath,
                TipForceNorm = logRow.TipForceNorm,
                NeedleTipX   = logRow.NeedleTipX,
                NeedleTipY   = logRow.NeedleTipY,
                ValidForce   = logRow.ValidForce
            });
        }

        return frames;
    }

    // ── Filename parsing ──────────────────────────────────────────────────

    // b_scans000052_1687979373.0632424  →  52
    private static int ExtractFrameIndex(string nameOrPath)
    {
        string name = Path.GetFileNameWithoutExtension(nameOrPath);
        // name = "b_scans000052_1687979373.0632424"
        int start = "b_scans".Length;
        int underscorePos = name.IndexOf('_', start);
        if (underscorePos < 0) underscorePos = name.Length;
        string digits = name.Substring(start, underscorePos - start);
        return int.TryParse(digits, out int idx) ? idx : 0;
    }

    // b_scans000052_1687979373.0632424  →  1687979373.0632424
    private static double ExtractTimestamp(string nameOrPath)
    {
        string name = Path.GetFileNameWithoutExtension(nameOrPath);
        int underscorePos = name.IndexOf('_', "b_scans".Length);
        if (underscorePos < 0 || underscorePos + 1 >= name.Length) return 0.0;
        string tsStr = name.Substring(underscorePos + 1);
        return ParseDouble(tsStr);
    }

    // ── Binary search for nearest timestamp ──────────────────────────────

    private static int NearestIndex(double[] sorted, double target)
    {
        if (sorted.Length == 0) return 0;
        int lo = 0, hi = sorted.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < target) lo = mid + 1;
            else hi = mid;
        }
        // lo is the first index >= target; compare with lo-1
        if (lo > 0 && Math.Abs(sorted[lo - 1] - target) < Math.Abs(sorted[lo] - target))
            return lo - 1;
        return lo;
    }

    // ── Parse helpers ─────────────────────────────────────────────────────

    private static float  ParseFloat(string s)  => float.TryParse(s.Trim(),  NumberStyles.Float, Inv, out float  v) ? v : 0f;
    private static double ParseDouble(string s) => double.TryParse(s.Trim(), NumberStyles.Float, Inv, out double v) ? v : 0.0;
}
