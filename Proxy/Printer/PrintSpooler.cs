// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Text.Json.Nodes;
using VintageHive.Utilities;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Printer;

internal static class PrintSpooler
{
    const string DEFAULT_PS_SIGNATURE = "%!PS-Adobe-3.0";

    static string _spoolerPath;
    static string _depotPath;

    public static void Init()
    {
        GhostScriptNative.Init();

        _spoolerPath = $"{VFS.DataPath}printspooler/";
        _depotPath = $"{VFS.DownloadsPath}printer/";

        VFS.DirectoryCreate(_spoolerPath);
        VFS.DirectoryCreate(_depotPath);

        if (!GhostScriptNative.IsAvailable)
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), "PostScript/PCL print jobs will be saved as raw files (GhostScript unavailable)", "");
        }

        Task.Run(SpoolerThread);
    }

    public static string SpoolerPath => _spoolerPath;

    public static string DepotPath => _depotPath;

    static async Task SpoolerThread()
    {
        while (Mind.IsRunning)
        {
            var job = Mind.PrinterDb.GetNextJob();

            if (job == null)
            {
                Thread.Sleep(100);
                continue;
            }

            Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job found! Processing job {job.Id}!");

            try
            {
                if (job.DocData == null || job.DocData.Length == 0)
                {
                    if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Canceled))
                    {
                        Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to canceled; {job.Id}", "");
                    }

                    Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"{job.Id} has no data, skipping!");
                    continue;
                }

                // Set job to Processing state
                if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Processing))
                {
                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to processing; {job.Id}", "");
                    continue;
                }

                var jobName = "Unknown";

                if (!string.IsNullOrEmpty(job.DocNewAttr))
                {
                    try
                    {
                        var docNewAttributes = JsonSerializer.Deserialize<JsonObject>(job.DocNewAttr);
                        jobName = docNewAttributes["JobName"]?.ToString() ?? "Unknown";
                    }
                    catch
                    {
                        // Malformed JSON — use default name
                    }
                }

                // Detect format and route to appropriate handler
                var format = PrintFormatDetector.Detect(job.DocData);

                Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job {job.Id} detected as {format}");

                switch (format)
                {
                    case PrintDataFormat.PostScript:
                    {
                        await ProcessPostScript(job, jobName);
                    }
                    break;

                    case PrintDataFormat.EscP:
                    {
                        await ProcessEscP(job, jobName, ibmMode: false);
                    }
                    break;

                    case PrintDataFormat.IbmProPrinter:
                    {
                        await ProcessEscP(job, jobName, ibmMode: true);
                    }
                    break;

                    case PrintDataFormat.Pcl:
                    {
                        // PCL: GhostScript can handle PCL if the PCL interpreter is available
                        // For now, try treating it as PostScript (GhostScript will auto-detect)
                        await ProcessPostScript(job, jobName);
                    }
                    break;

                    case PrintDataFormat.PlainText:
                    case PrintDataFormat.Unknown:
                    default:
                    {
                        await ProcessPlainText(job, jobName);
                    }
                    break;
                }

                Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job completed; {job.Id}", "");
            }
            catch (Exception ex)
            {
                if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Aborted))
                {
                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to aborted; {job.Id}", "");
                }

                Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Error in SpoolerThread; {ex}", "");
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), "SpoolerThread exited", "");
    }

    static async Task ProcessPostScript(PrinterJob job, string jobName)
    {
        if (!GhostScriptNative.IsAvailable)
        {
            Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job {job.Id} is PostScript but GhostScript unavailable — saving raw data");

            if (!Mind.PrinterDb.SetJobPrintData(job.Id, job.DocData, "application/postscript"))
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job print data; {job.Id}", "");
                return;
            }

            using var rawOutputFile = VFS.FileWrite($"{_depotPath}{job.Id}_{SanitizeFileName(jobName)}.ps");

            await rawOutputFile.WriteAsync(job.DocData);
            await rawOutputFile.FlushAsync();

            if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to completed; {job.Id}", "");
            }

            return;
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job {job.Id} is a PostScript file!");

        var tempInputPath = $"{_spoolerPath}{job.Id}.ps";
        var tempOutputPath = $"{_spoolerPath}{job.Id}.pdf";

        using var inputFileStream = VFS.FileWrite(tempInputPath);

        await inputFileStream.WriteAsync(job.DocData, 0, job.DocData.Length);
        await inputFileStream.FlushAsync();
        inputFileStream.Close();

        GhostScriptNative.ConvertPsToPdf(VFS.GetFullPath(tempInputPath), VFS.GetFullPath(tempOutputPath));

        var outputData = await VFS.FileReadDataAsync(tempOutputPath);

        if (!Mind.PrinterDb.SetJobPrintData(job.Id, outputData, HttpContentTypeMimeType.Application.Pdf))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job print data; {job.Id}", "");
            return;
        }

        using var outputFile = VFS.FileWrite($"{_depotPath}{job.Id}_{SanitizeFileName(jobName)}.pdf");

        await outputFile.WriteAsync(outputData);
        await outputFile.FlushAsync();

        VFS.FileDelete(tempInputPath);
        VFS.FileDelete(tempOutputPath);

        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to completed; {job.Id}", "");
        }
    }

    static async Task ProcessEscP(PrinterJob job, string jobName, bool ibmMode)
    {
        var modeLabel = ibmMode ? "IBM ProPrinter" : "ESC/P";
        Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job {job.Id} is {modeLabel} data!");

        var pdfData = await EscPosRenderer.RenderToPdfAsync(job.DocData, ibmMode, _spoolerPath, job.Id);

        if (pdfData == null || pdfData.Length == 0)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"ESC/P rendering produced no output for job {job.Id}", "");

            if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Aborted))
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to aborted; {job.Id}", "");
            }

            return;
        }

        if (!Mind.PrinterDb.SetJobPrintData(job.Id, pdfData, HttpContentTypeMimeType.Application.Pdf))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job print data; {job.Id}", "");
            return;
        }

        using var outputFile = VFS.FileWrite($"{_depotPath}{job.Id}_{SanitizeFileName(jobName)}.pdf");

        await outputFile.WriteAsync(pdfData);
        await outputFile.FlushAsync();

        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to completed; {job.Id}", "");
        }
    }

    static async Task ProcessPlainText(PrinterJob job, string jobName)
    {
        Log.WriteLine(Log.LEVEL_INFO, nameof(PrintSpooler), $"Job {job.Id} is plain text!");

        if (!Mind.PrinterDb.SetJobPrintData(job.Id, job.DocData, HttpContentTypeMimeType.Text.Plain))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job print data; {job.Id}", "");
            return;
        }

        var fileName = $"{_depotPath}{job.Id}_{SanitizeFileName(jobName)}.txt";

        if (VFS.FileExists(fileName))
        {
            VFS.FileDelete(fileName);
        }

        using var outputFile = VFS.FileWrite(fileName);

        await outputFile.WriteAsync(job.DocData);
        await outputFile.FlushAsync();

        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrintSpooler), $"Failed to set job state to completed; {job.Id}", "");
        }
    }

    static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > 60 ? sanitized[..60] : sanitized;
    }
}
