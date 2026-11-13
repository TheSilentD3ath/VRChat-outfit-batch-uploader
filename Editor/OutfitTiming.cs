// ============================================================
//  VRC Outfit Batch Uploader — upload timing
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  Measures where a batch spends its time, so that decisions
//  about speeding it up rest on numbers instead of guesses.
//  Every batch upload writes one TIME line to the upload log
//  (ProjectSettings/ShiroOutfit_upload.log); the batch ends
//  with a summary in the log, the Console and the status box.
//
//  Phases of one upload:
//    activate   switching outfit, items, blendshapes, FaceEmo
//    save       saving scene and assets before the build
//    fetch      reading the avatar record from VRChat
//    build      SDK: processing the avatar and packing its file
//    upload     SDK: sending the file (its own fixed waits included)
//    sdk other  SDK: checks before the build, hand-over, refresh
//    tool wait  this tool's own fixed pauses
//
//  build / upload come from the SDK's public builder events
//  (IVRCSdkBuilderApi). If an event does not fire, the phase is
//  printed as "?" and its time is counted under "sdk other".
//  Express uploads of new avatars are not measured.
// ============================================================

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        private const string SESSION_TIMING         = "Shiro_BatchTiming";
        private const string SESSION_SWITCH_STARTED = "Shiro_SwitchStartedTicks";

        /// <summary>Sums over the running batch. Kept as JSON in SessionState so a platform
        /// switch (domain reload) in the middle of the batch does not lose them.</summary>
        [Serializable]
        private class BatchTiming
        {
            public int   uploads;
            public float total;
            public float activate;
            public float save;
            public float fetch;
            public float build;
            public float upload;
            public float sdkOther;
            public float toolWait;
            public int   buildMeasured;        // uploads where the SDK's build events fired
            public int   uploadMeasured;       // uploads where the SDK's upload events fired
            public float megabytes;            // sum of the file sizes that are known
            public int   sized;                // uploads with a known file size
            public float uploadSecondsSized;   // upload time of exactly those — for MB/s
            public int   switches;
            public float switchSeconds;
        }

        // A Stopwatch, not EditorApplication.timeSinceStartup: it keeps counting while the
        // editor is busy inside the build. It restarts with every domain reload, which is fine —
        // one upload never spans a reload (the queue re-runs the interrupted item).
        private readonly System.Diagnostics.Stopwatch _timingClock = System.Diagnostics.Stopwatch.StartNew();
        private IVRCSdkAvatarBuilderApi _timedBuilder;   // the builder our handlers are attached to
        private double _tBuildStart = -1, _tBuildEnd = -1, _tUploadStart = -1, _tUploadEnd = -1;
        private long   _timedBundleBytes;

        private double TimingNow() => _timingClock.Elapsed.TotalSeconds;

        // ============================================================
        //  SDK events
        // ============================================================
        /// <summary>Hooks the SDK's public build / upload events. Once per builder instance —
        /// the SDK creates a new one whenever its panel is rebuilt or the domain reloads.</summary>
        private void AttachSdkTiming(IVRCSdkAvatarBuilderApi builder)
        {
            if (builder == null || ReferenceEquals(_timedBuilder, builder)) return;
            DetachSdkTiming();

            builder.OnSdkBuildStart   += OnTimedBuildStart;
            builder.OnSdkBuildSuccess += OnTimedBuildSuccess;
            builder.OnSdkBuildFinish  += OnTimedBuildFinish;
            builder.OnSdkUploadStart  += OnTimedUploadStart;
            builder.OnSdkUploadFinish += OnTimedUploadFinish;
            _timedBuilder = builder;
        }

        private void DetachSdkTiming()
        {
            if (_timedBuilder == null) return;
            try
            {
                _timedBuilder.OnSdkBuildStart   -= OnTimedBuildStart;
                _timedBuilder.OnSdkBuildSuccess -= OnTimedBuildSuccess;
                _timedBuilder.OnSdkBuildFinish  -= OnTimedBuildFinish;
                _timedBuilder.OnSdkUploadStart  -= OnTimedUploadStart;
                _timedBuilder.OnSdkUploadFinish -= OnTimedUploadFinish;
            }
            catch (Exception) { /* a builder that is already gone — nothing to unhook */ }
            _timedBuilder = null;
        }

        private void ResetSdkTiming()
        {
            _tBuildStart = _tBuildEnd = _tUploadStart = _tUploadEnd = -1;
            _timedBundleBytes = 0;
        }

        // (object, object) on purpose: one signature fits EventHandler, EventHandler<string> and
        // EventHandler<object> alike, which is what these five events use between them.
        private void OnTimedBuildStart(object sender, object args)   { _tBuildStart = TimingNow(); }
        private void OnTimedBuildFinish(object sender, object args)  { _tBuildEnd = TimingNow(); }
        private void OnTimedUploadStart(object sender, object args)  { _tUploadStart = TimingNow(); }
        private void OnTimedUploadFinish(object sender, object args) { _tUploadEnd = TimingNow(); }

        /// <summary>The SDK passes the path of the file it just built.</summary>
        private void OnTimedBuildSuccess(object sender, object args)
        {
            try
            {
                string path = args as string;
                _timedBundleBytes = !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch (Exception) { _timedBundleBytes = 0; }
        }

        // ============================================================
        //  One upload
        // ============================================================
        /// <summary>Writes the TIME line of one finished upload and adds it to the batch sums.
        /// The t… arguments are TimingNow() readings; pauseAfterSeconds is the fixed pause the
        /// queue takes before the next upload (it happens after this call, so it is passed in
        /// rather than measured). Never throws — measuring must not be able to break an upload
        /// that already succeeded.</summary>
        private void RecordUploadTiming(string outfitName, VRCPlatform platform,
            double tStart, double tActivated, double tSaved, double tFetchStart, double tFetched,
            double tSdkStart, double tSdkEnd, float pauseAfterSeconds)
        {
            try
            {
                float total    = (float)(tSdkEnd - tStart) + pauseAfterSeconds;
                float activate = (float)(tActivated - tStart);
                float save     = (float)(tSaved - tActivated);
                float fetch    = (float)(tFetched - tFetchStart);
                float sdk      = (float)(tSdkEnd - tSdkStart);

                // Only events of THIS call count: both stamps set, and not older than its start.
                bool hasBuild  = _tBuildStart  >= tSdkStart && _tBuildEnd  >= _tBuildStart;
                bool hasUpload = _tUploadStart >= tSdkStart && _tUploadEnd >= _tUploadStart;
                float build    = hasBuild  ? (float)(_tBuildEnd  - _tBuildStart)  : 0f;
                float upload   = hasUpload ? (float)(_tUploadEnd - _tUploadStart) : 0f;
                float sdkOther = Mathf.Max(0f, sdk - build - upload);
                float toolWait = Mathf.Max(0f, total - activate - save - fetch - sdk);
                float megabytes = _timedBundleBytes / (1024f * 1024f);

                string buildText  = hasBuild  ? Sec(build)  : "?";
                string uploadText = hasUpload ? Sec(upload) : "?";
                string sizeText   = megabytes > 0f ? " | file " + Sec(megabytes) + " MB" : "";

                LogUpload("TIME  " + outfitName + " (" + platform + "): total " + Sec(total) + "s" +
                          " | activate " + Sec(activate) + " | save " + Sec(save) + " | fetch " + Sec(fetch) +
                          " | build " + buildText + " | upload " + uploadText +
                          " | sdk other " + Sec(sdkOther) + " | tool wait " + Sec(toolWait) + sizeText);

                var t = LoadBatchTiming();
                t.uploads++;
                t.total    += total;
                t.activate += activate;
                t.save     += save;
                t.fetch    += fetch;
                t.build    += build;
                t.upload   += upload;
                t.sdkOther += sdkOther;
                t.toolWait += toolWait;
                if (hasBuild)  t.buildMeasured++;
                if (hasUpload) t.uploadMeasured++;
                if (megabytes > 0f && hasUpload)
                {
                    t.megabytes += megabytes;
                    t.sized++;
                    t.uploadSecondsSized += upload;
                }
                SaveBatchTiming(t);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not record upload timing: " + ex.Message);
            }
        }

        // ============================================================
        //  Platform switches
        // ============================================================
        /// <summary>Call right before a platform switch. The domain reload that follows resets
        /// every clock in memory, so the start is kept as wall-clock time in SessionState.</summary>
        private static void MarkPlatformSwitchStarted()
        {
            SessionState.SetString(SESSION_SWITCH_STARTED,
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static void ClearPlatformSwitchMark()
        {
            SessionState.EraseString(SESSION_SWITCH_STARTED);
        }

        /// <summary>Call when the queue carries on with an upload: if a switch was under way,
        /// log how long it took until the SDK was ready again.</summary>
        private void RecordPlatformSwitchIfPending(VRCPlatform arrivedOn)
        {
            try
            {
                string stamp = SessionState.GetString(SESSION_SWITCH_STARTED, "");
                if (string.IsNullOrEmpty(stamp)) return;
                ClearPlatformSwitchMark();

                if (!long.TryParse(stamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) return;
                float seconds = (float)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
                if (seconds < 0f || seconds > 6f * 3600f) return;   // system clock was changed — no measurement

                LogUpload("TIME  platform switch to " + arrivedOn + ": " + Sec(seconds) + "s (until the SDK was ready again)");

                var t = LoadBatchTiming();
                t.switches++;
                t.switchSeconds += seconds;
                SaveBatchTiming(t);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not record platform switch timing: " + ex.Message);
            }
        }

        // ============================================================
        //  Batch sums
        // ============================================================
        private static BatchTiming LoadBatchTiming()
        {
            string json = SessionState.GetString(SESSION_TIMING, "");
            if (string.IsNullOrEmpty(json)) return new BatchTiming();
            try { return JsonUtility.FromJson<BatchTiming>(json) ?? new BatchTiming(); }
            catch (Exception) { return new BatchTiming(); }
        }

        private static void SaveBatchTiming(BatchTiming t)
        {
            SessionState.SetString(SESSION_TIMING, JsonUtility.ToJson(t));
        }

        /// <summary>Forget the sums — at batch start, and after the summary has been written.</summary>
        private static void ClearBatchTiming()
        {
            SessionState.EraseString(SESSION_TIMING);
            ClearPlatformSwitchMark();
        }

        /// <summary>Writes the summary of the batch that just ended (or was cancelled) to the log
        /// and the Console and clears the sums. Returns one short line for the status box, or
        /// null when nothing was measured.</summary>
        private static string FinishBatchTiming()
        {
            try
            {
                var t = LoadBatchTiming();
                ClearBatchTiming();
                if (t.uploads <= 0 || t.total <= 0f) return null;

                float n = t.uploads;
                var sb = new StringBuilder();
                sb.Append("Batch timing — " + t.uploads + " upload(s), " + Sec(t.total) + "s measured, on average " +
                          Sec(t.total / n) + "s each:");
                AppendPhase(sb, "build", t.build, t.total, t.buildMeasured, t.uploads);
                AppendPhase(sb, "upload", t.upload, t.total, t.uploadMeasured, t.uploads);
                AppendPhase(sb, "sdk other", t.sdkOther, t.total, t.uploads, t.uploads);
                AppendPhase(sb, "save", t.save, t.total, t.uploads, t.uploads);
                AppendPhase(sb, "activate", t.activate, t.total, t.uploads, t.uploads);
                AppendPhase(sb, "fetch", t.fetch, t.total, t.uploads, t.uploads);
                AppendPhase(sb, "tool wait", t.toolWait, t.total, t.uploads, t.uploads);
                if (t.sized > 0)
                {
                    sb.Append(Environment.NewLine + "    file size   on average " + Sec(t.megabytes / t.sized) + " MB");
                    if (t.uploadSecondsSized > 0f)
                        sb.Append(", sent at " + (t.megabytes / t.uploadSecondsSized).ToString("0.00", CultureInfo.InvariantCulture) +
                                  " MB/s (the SDK's own waits included)");
                }
                if (t.switches > 0)
                    sb.Append(Environment.NewLine + "    platform switches: " + t.switches + ", " + Sec(t.switchSeconds) +
                              "s in total (not part of the per-upload times)");

                string text = sb.ToString();
                LogUpload(text);
                Debug.Log("[OutfitBatchUploader] " + text);

                string shortLine = "Timing: on average " + Sec(t.total / n) + "s per upload";
                if (t.buildMeasured > 0)  shortLine += " — build " + Sec(t.build / t.buildMeasured) + "s";
                if (t.uploadMeasured > 0) shortLine += ", upload " + Sec(t.upload / t.uploadMeasured) + "s";
                return shortLine + ". Details: Log button.";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not write the timing summary: " + ex.Message);
                return null;
            }
        }

        /// <summary>One summary row: average per measured upload and share of the total time.</summary>
        private static void AppendPhase(StringBuilder sb, string name, float sum, float total, int measured, int uploads)
        {
            sb.Append(Environment.NewLine + "    " + name.PadRight(11) + " ");
            if (measured <= 0)
            {
                sb.Append("not measured (the SDK sent no event for it)");
                return;
            }
            int percent = total > 0f ? Mathf.RoundToInt(100f * sum / total) : 0;
            sb.Append("on average " + Sec(sum / measured) + "s, " + percent + "% of the time");
            if (measured < uploads) sb.Append(" (measured in " + measured + " of " + uploads + ")");
        }

        /// <summary>One decimal, always with a dot — the log should read the same on every machine.</summary>
        private static string Sec(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
