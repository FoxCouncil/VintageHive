// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Runtime.InteropServices;

namespace VintageHive.Utilities;

internal static class FfmpegUtils
{
    public static string GetExecutablePath()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new ApplicationException("Somehow, it's not x64? Everything VintageHive is 64bit. What?");
        }

        string bundled;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bundled = Path.Combine("libs", "ffmpeg.exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            bundled = Path.Combine("libs", "ffmpeg.osx.intel");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            bundled = Path.Combine("libs", "ffmpeg.amd64");
        }
        else
        {
            throw new Exception("Cannot determine operating system!");
        }

        // Fall back to a system-installed ffmpeg (resolved via PATH) when the bundled binary isn't shipped -
        // the Docker image and non-amd64 Linux/macOS hosts rely on the OS package instead.
        return File.Exists(bundled) ? bundled : "ffmpeg";
    }
}
