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

        string fileName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = "ffmpeg.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fileName = "ffmpeg.osx.intel";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            fileName = "ffmpeg.amd64";
        }
        else
        {
            throw new Exception("Cannot determine operating system!");
        }

        // Resolve against the app directory (like VFS/Log/DbContext), NOT the current working directory - a service
        // started from a different CWD would otherwise never find the bundled binary and silently fall back to PATH.
        var bundled = Path.Combine(AppContext.BaseDirectory, "libs", fileName);

        // Fall back to a system-installed ffmpeg (resolved via PATH) when the bundled binary isn't shipped -
        // the Docker image and non-amd64 Linux/macOS hosts rely on the OS package instead.
        return File.Exists(bundled) ? bundled : "ffmpeg";
    }
}
