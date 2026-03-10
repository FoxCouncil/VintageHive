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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine("libs", "ffmpeg.exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine("libs", "ffmpeg.osx.intel");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine("libs", "ffmpeg.amd64");
        }

        throw new Exception("Cannot determine operating system!");
    }
}
