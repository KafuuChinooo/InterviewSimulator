using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Ghi lại số liệu độ trễ của từng bước STT/LLM/TTS ra CSV để tiện đo hiệu năng sau mỗi phiên.
/// </summary>
public static class AIPerformanceCsvLogger
{
    private static readonly object SyncRoot = new object();
    private const string DefaultFileName = "ai_latency_metrics.csv";
    private static bool _hasLoggedPath;

    public static string GetCsvPath(string fileName = null)
    {
        string safeFileName = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName.Trim();
        return Path.Combine(Application.persistentDataPath, safeFileName);
    }

    public static void LogMetric(
        string stage,
        bool success,
        double durationMs,
        string sessionId,
        string interactionId,
        string language,
        string jobTitle,
        string interviewType,
        int requestChars = 0,
        int responseChars = 0,
        float audioSeconds = 0f,
        int chunkIndex = 0,
        int chunkCount = 0,
        long httpStatus = 0,
        string error = null,
        string fileName = null)
    {
        try
        {
            string csvPath = GetCsvPath(fileName);
            lock (SyncRoot)
            {
                // Đảm bảo file log luôn có thư mục và header trước khi append dữ liệu mới.
                EnsureDirectoryExists(csvPath);
                EnsureHeader(csvPath);

                if (!_hasLoggedPath)
                {
                    Debug.Log("[AI] Performance CSV: " + csvPath);
                    _hasLoggedPath = true;
                }

                string[] fields =
                {
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    stage,
                    success ? "true" : "false",
                    durationMs.ToString("F2", CultureInfo.InvariantCulture),
                    sessionId ?? "",
                    interactionId ?? "",
                    language ?? "",
                    jobTitle ?? "",
                    interviewType ?? "",
                    requestChars.ToString(CultureInfo.InvariantCulture),
                    responseChars.ToString(CultureInfo.InvariantCulture),
                    audioSeconds.ToString("F3", CultureInfo.InvariantCulture),
                    chunkIndex.ToString(CultureInfo.InvariantCulture),
                    chunkCount.ToString(CultureInfo.InvariantCulture),
                    httpStatus.ToString(CultureInfo.InvariantCulture),
                    error ?? ""
                };

                using (var writer = new StreamWriter(csvPath, true, new UTF8Encoding(false)))
                {
                    writer.WriteLine(string.Join(",", Array.ConvertAll(fields, EscapeCsv)));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AI] Failed to write performance CSV: " + ex.Message);
        }
    }

    private static void EnsureDirectoryExists(string csvPath)
    {
        string directory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void EnsureHeader(string csvPath)
    {
        if (File.Exists(csvPath)) return;

        using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("timestamp_utc,stage,success,duration_ms,session_id,interaction_id,language,job_title,interview_type,request_chars,response_chars,audio_seconds,chunk_index,chunk_count,http_status,error");
        }
    }

    private static string EscapeCsv(string value)
    {
        string safeValue = value ?? "";
        bool needsQuotes = safeValue.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuotes) return safeValue;

        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
    }

    // /\_/\\
    // ( o.o )  [ kafuu ]
    //  > ^ <
}
