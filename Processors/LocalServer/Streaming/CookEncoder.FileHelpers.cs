// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
// Cook encoder — file encoding helpers (FFmpeg-based WAV/audio → .rm conversion)

using VintageHive.Utilities;

namespace VintageHive.Processors.LocalServer.Streaming;

internal partial class CookEncoder
{
    /// <summary>
    /// Generate a static .rm test file from a sine wave.
    /// Used to validate cook encoding with FFmpeg offline.
    /// </summary>
    public static byte[] GenerateTestRmFile(float durationSeconds = 5.0f)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

        // Generate sine wave PCM (440 Hz)
        int totalSamples = (int)(sampleRate * durationSeconds);
        var pcm = new byte[totalSamples * channels * 2];
        for (int i = 0; i < totalSamples; i++)
        {
            short sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 4000);
            for (int ch = 0; ch < channels; ch++)
            {
                int idx = (i * channels + ch) * 2;
                pcm[idx] = (byte)(sample & 0xFF);
                pcm[idx + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        // Encode all PCM — returns RM data packets (12-byte header + payload each)
        var allPackets = encoder.EncodePcm(pcm);

        // Compute file metadata for DATA chunk and PROP chunk
        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        // Build complete RM file with correct packet counts
        string title = "Cook Test - 440Hz Sine";
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Encode a WAV file to a cook .rm file. Uses FFmpeg to resample/convert
    /// the input to 11025Hz stereo s16le PCM before encoding.
    /// </summary>
    public static byte[] EncodeWavFile(string inputPath)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

        // Use FFmpeg to convert input to raw s16le PCM at our target format
        var ffmpegPath = FfmpegUtils.GetExecutablePath();
        var ffmpegArgs = $"-i \"{inputPath}\" -ar {sampleRate} -ac {channels} -c:a pcm_s16le -f s16le pipe:1";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = ffmpegArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        using var stdout = process.StandardOutput.BaseStream;

        // Read all PCM data from stdout
        using var pcmStream = new MemoryStream();
        stdout.CopyTo(pcmStream);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"FFmpeg failed (exit {process.ExitCode}): {stderr}");
        }

        var pcm = pcmStream.ToArray();
        int totalSamplesPerChannel = pcm.Length / (channels * 2);
        float durationSeconds = (float)totalSamplesPerChannel / sampleRate;

        Console.WriteLine($"Input: {totalSamplesPerChannel} samples/ch, {durationSeconds:F1}s at {sampleRate}Hz {channels}ch");

        // Encode all PCM
        var allPackets = encoder.EncodePcm(pcm);

        // Compute file metadata
        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        // Build complete RM file
        string title = Path.GetFileNameWithoutExtension(inputPath);
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        Console.WriteLine($"Encoded {numPackets} RM packets, {allPackets.Sum(p => p.Length)} bytes data");
        return ms.ToArray();
    }

    /// <summary>
    /// Same as EncodeWavFile but also returns the encoder instance for accessing RawFrames.
    /// </summary>
    public static (byte[] rmData, CookEncoder encoder) EncodeWavFileWithEncoder(string inputPath)
    {
        int sampleRate = 11025;
        int channels = 2;
        int bitrate = 19000;
        var encoder = new CookEncoder(sampleRate, channels, bitrate);

        var ffmpegPath = FfmpegUtils.GetExecutablePath();
        var ffmpegArgs = $"-i \"{inputPath}\" -ar {sampleRate} -ac {channels} -c:a pcm_s16le -f s16le pipe:1";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = ffmpegArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        using var stdout = process.StandardOutput.BaseStream;

        using var pcmStream = new MemoryStream();
        stdout.CopyTo(pcmStream);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"FFmpeg failed (exit {process.ExitCode}): {stderr}");
        }

        var pcm = pcmStream.ToArray();
        int totalSamplesPerChannel = pcm.Length / (channels * 2);
        float durationSeconds = (float)totalSamplesPerChannel / sampleRate;

        Console.WriteLine($"Input: {totalSamplesPerChannel} samples/ch, {durationSeconds:F1}s at {sampleRate}Hz {channels}ch");

        var allPackets = encoder.EncodePcm(pcm);

        uint numPackets = (uint)allPackets.Count;
        uint totalDataSize = 0;
        foreach (var pkt in allPackets)
        {
            totalDataSize += (uint)pkt.Length;
        }
        uint durationMs = (uint)(durationSeconds * 1000);

        string title = Path.GetFileNameWithoutExtension(inputPath);
        var headers = encoder.BuildRmFileHeaders(title, numPackets, totalDataSize, durationMs);

        using var ms = new MemoryStream();
        ms.Write(headers, 0, headers.Length);
        foreach (var pkt in allPackets)
        {
            ms.Write(pkt, 0, pkt.Length);
        }

        Console.WriteLine($"Encoded {numPackets} RM packets, {allPackets.Sum(p => p.Length)} bytes data");
        return (ms.ToArray(), encoder);
    }
}
