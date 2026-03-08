// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Processors.LocalServer.Streaming;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding.RegisterProvider(new MacEncodingProvider());

Encoding.GetEncoding("ISO-8859-1");

if (args.Contains("--cook-test"))
{
    // Optional: --cook-test inputfile.wav → encode WAV to cook .rm
    var inputIdx = Array.IndexOf(args, "--cook-test") + 1;
    var inputFile = inputIdx < args.Length ? args[inputIdx] : null;

    if (inputFile != null && File.Exists(inputFile))
    {
        var outputPath = Path.ChangeExtension(inputFile, ".cook.rm");
        Console.WriteLine($"Encoding {inputFile} → {outputPath}");
        var (rmData, encoder) = CookEncoder.EncodeWavFileWithEncoder(inputFile);
        File.WriteAllBytes(outputPath, rmData);
        Console.WriteLine($"Written {rmData.Length} bytes ({rmData.Length / 1024}KB)");

        // Dump raw cook frames for DLL comparison
        var rawPath = Path.ChangeExtension(inputFile, ".cook.raw");
        using (var rawFs = File.Create(rawPath))
        {
            foreach (var frame in encoder.RawFrames)
            {
                rawFs.Write(frame, 0, frame.Length);
            }
        }
        Console.WriteLine($"Raw frames: {encoder.RawFrames.Count} × {encoder.RawFrames[0].Length} bytes → {rawPath}");
    }
    else
    {
        var outputPath = Path.Combine(Environment.CurrentDirectory, "cook_test.rm");
        Console.WriteLine($"Generating cook test file: {outputPath}");
        var rmData = CookEncoder.GenerateTestRmFile(5.0f);
        File.WriteAllBytes(outputPath, rmData);
        Console.WriteLine($"Written {rmData.Length} bytes ({rmData.Length / 1024}KB)");
    }
    return;
}

await Mind.Init();

Mind.Start();