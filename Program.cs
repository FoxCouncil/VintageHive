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
        var rmData = CookEncoder.EncodeWavFile(inputFile);
        File.WriteAllBytes(outputPath, rmData);
        Console.WriteLine($"Written {rmData.Length} bytes ({rmData.Length / 1024}KB)");
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