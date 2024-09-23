using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;
using System.Diagnostics;
using System.Text.Json.Nodes;
using VintageHive.Network;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Printer;

internal class PrinterProxy : Listener
{
    const string DEFAULT_PS_SIGNATURE = "%!PS-Adobe-3.0";

    const string DEFAULT_PRINTER_NAME = "VintageHiveIPP";

    const string DEFAULT_MEDIA_SIZE = "iso_a4_210x297mm";

    const string DEFAULT_DOCUMENT_FORMAT = HttpContentTypeMimeType.Application.Pdf;

    const int DEFAULT_JOB_PRIORITY = 1;

    const int DEFAULT_NUMBER_OF_COPIES = 1;

    static readonly Sides DEFAULT_NUMBER_OF_SIDES = Sides.OneSided;

    static readonly PrintScaling DEAFULT_PRINT_SCALING = PrintScaling.Auto;

    static readonly Resolution DEAULT_PRINTER_RESOLUTION = new(600, 600, ResolutionUnit.DotsPerInch);

    static readonly Finishings DEFAULT_FINISHINGS = Finishings.None;

    static readonly PrintQuality DEFAULT_PRINT_QUALITY = PrintQuality.High;

    static readonly Orientation DEFAULT_ORIENTATION = Orientation.Portrait;

    static readonly JobHoldUntil DEFAULT_JOB_UNTIL = JobHoldUntil.NoHold;

    static readonly SharpIppServer _ippServer = new();

    static string PrinterUrl;

    static string SpoolerPath;

    static string DepotPath;

    public PrinterProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
        GhostScriptNative.Init();

        SpoolerPath = $"{VFS.DataPath}printspooler/";

        DepotPath = $"{VFS.DownloadsPath}printer/";

        VFS.DirectoryCreate(SpoolerPath);

        VFS.DirectoryCreate(DepotPath);

        Task.Run(() => PrinterSpoolerThread());
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = await HttpRequest.Build(connection, Encoding.ASCII, data[..read]);

        PrinterUrl = $"http://{httpRequest.Uri.Authority}/";

        if (!httpRequest.IsValid)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $"Unhandled type of IPP request; {Encoding.GetString(data[..read])}", httpRequest.ListenerSocket?.TraceId.ToString() ?? "N/A");

            return null;
        }

        var httpResponse = new HttpResponse(httpRequest);

        switch (httpRequest.Method)
        {
            case "GET":
            {
                httpResponse.SetBodyString("VintageHive IPP Proxy", "text/plain");

                return httpResponse.GetResponseEncodedData();
            }

            case "POST":
            {
                using var responseStream = new MemoryStream();

                try
                {
                    using var dataStream = new MemoryStream(httpRequest.BodyData);

                    var ippRequest = await _ippServer.ReceiveRequestAsync(dataStream);

                    IIppResponseMessage ippResponse = ippRequest switch
                    {
                        CancelJobRequest x => GetCancelJobResponse(x),
                        CreateJobRequest x => GetCreateJobResponse(x),
                        CUPSGetPrintersRequest x => GetCUPSGetPrintersResponse(x),
                        GetJobAttributesRequest x => GetJobAttributesResponse(x),
                        GetJobsRequest x => GetJobsResponse(x),
                        GetPrinterAttributesRequest x => GetPrinterAttributesResponse(x),
                        HoldJobRequest x => GetHoldJobResponse(x),
                        PausePrinterRequest x => GetPausePrinterResponse(x),
                        PrintJobRequest x => GetPrintJobResponse(x),
                        PrintUriRequest x => GetPrintUriResponse(x),
                        PurgeJobsRequest x => GetPurgeJobsResponse(x),
                        ReleaseJobRequest x => GetReleaseJobResponse(x),
                        RestartJobRequest x => GetRestartJobResponse(x),
                        ResumePrinterRequest x => GetResumePrinterResponse(x),
                        SendDocumentRequest x => GetSendDocumentResponse(x),
                        SendUriRequest x => SendUriResponse(x),
                        ValidateJobRequest x => GetValidateJobResponse(x),
                        _ => throw new NotImplementedException()
                    };

                    await _ippServer.SendResponseAsync(ippResponse, responseStream);

                    return responseStream.ToArray();
                }
                catch (IppRequestException ex)
                {
                    var response = new IppResponseMessage
                    {
                        RequestId = ex.RequestMessage.RequestId,
                        Version = ex.RequestMessage.Version,
                        StatusCode = ex.StatusCode
                    };

                    var operation = new IppSection { Tag = SectionTag.OperationAttributesTag };

                    operation.Attributes.Add(new IppAttribute(Tag.Charset, JobAttribute.AttributesCharset, "utf-8"));
                    operation.Attributes.Add(new IppAttribute(Tag.NaturalLanguage, JobAttribute.AttributesNaturalLanguage, "en"));

                    response.Sections.Add(operation);

                    await _ippServer.SendRawResponseAsync(response, responseStream);

                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Process Request failed: IppRequestException {ex}", "");

                    return responseStream.ToArray();
                }
                catch (Exception ex)
                {
                    httpResponse.StatusCode = Http.HttpStatusCode.InternalServerError;

                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Process Request failed: Exception {ex}", "");

                    return null;
                }
            }

            default:
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $"Unhandled IPP method; {httpRequest.Method}", httpRequest.ListenerSocket?.TraceId.ToString() ?? "N/A");
                return null;
            }
        }
    }

    async Task PrinterSpoolerThread()
    {
        while (Mind.IsRunning)
        {
            var job = Mind.PrinterDb.GetNextJob();

            if (job != null)
            {
                Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Job found! Processing job {job.Id}!");

                try
                {
                    if (job.DocData.Length == 0)
                    {
                        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Canceled))
                        {
                            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job state to canceled; {job.Id}", "");
                        }

                        Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"{job.Id} has no data, looping!");

                        continue;
                    }

                    var docNewAttributes = JsonSerializer.Deserialize<JsonObject>(job.DocNewAttr);

                    var jobName = docNewAttributes["JobName"].ToString();

                    if (job.DocData.AsSpan().IndexOf(Encoding.ASCII.GetBytes(DEFAULT_PS_SIGNATURE)) >= 0)
                    {
                        Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Job {job.Id} is a PS file!");

                        // PostScript
                        var tempInputPath = $"{SpoolerPath}{job.Id}.ps";
                        var tempOutputPath = $"{SpoolerPath}{job.Id}.pdf";

                        using var inputFileStream = VFS.FileWrite(tempInputPath);

                        await inputFileStream.WriteAsync(job.DocData, 0, job.DocData.Length);
                        await inputFileStream.FlushAsync();

                        inputFileStream.Close();

                        GhostScriptNative.ConvertPsToPdf(VFS.GetFullPath(tempInputPath), VFS.GetFullPath(tempOutputPath));

                        var ouputData = await VFS.FileReadDataAsync(tempOutputPath);

                        if (!Mind.PrinterDb.SetJobPrintData(job.Id, ouputData, HttpContentTypeMimeType.Application.Pdf))
                        {
                            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job print data; {job.Id}", "");

                            continue;
                        }

                        using var outputFile = VFS.FileWrite($"{DepotPath}{job.Id}_{jobName}.pdf");

                        await outputFile.WriteAsync(ouputData);
                        await outputFile.FlushAsync();

                        VFS.FileDelete(tempInputPath);
                        VFS.FileDelete(tempOutputPath);

                        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
                        {
                            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job state to completed; {job.Id}", "");

                            continue;
                        }
                    }
                    else
                    {
                        Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Job {job.Id} is a plain text file!");

                        // Plain Text
                        if (!Mind.PrinterDb.SetJobPrintData(job.Id, job.DocData, HttpContentTypeMimeType.Text.Plain))
                        {
                            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job print data; {job.Id}", "");

                            continue;
                        }

                        var fileName = $"{DepotPath}{job.Id}_{jobName}.txt";

                        if (VFS.FileExists(fileName))
                        {
                            VFS.FileDelete(fileName);
                        }

                        using var outputFile = VFS.FileWrite(fileName);

                        await outputFile.WriteAsync(job.DocData);
                        await outputFile.FlushAsync();

                        if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Completed))
                        {
                            Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job state to completed; {job.Id}", "");

                            continue;
                        }
                    }

                    Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Job completed; {job.Id}", "");
                }
                catch (Exception ex)
                {
                    if (!Mind.PrinterDb.SetJobState(job.Id, PrinterJobState.Canceled))
                    {
                        Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Failed to set job state to canceled; {job.Id}", "");
                    }

                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Error in PrinterSpoolerThread; {ex}", "");
                }
            }
            else
            {
                Thread.Sleep(100);
            }
        }

        Debugger.Break();
    }

    private ValidateJobResponse GetValidateJobResponse(ValidateJobRequest request)
    {
        return new ValidateJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk
        };
    }

    private SendUriResponse SendUriResponse(SendUriRequest request)
    {
        var response = new SendUriResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = Mind.PrinterDb.CreateJob(request.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.PrinterUri + $"/{jobId}";

        Debugger.Break();

        request.DocumentAttributes ??= new();

        FillWithDefaultValues(request.DocumentAttributes);

        response.JobState = JobState.Pending;
        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private SendDocumentResponse GetSendDocumentResponse(SendDocumentRequest request)
    {
        var response = new SendDocumentResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = Mind.PrinterDb.CreateJob(request.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.PrinterUri + $"/{jobId}";

        Debugger.Break();

        request.DocumentAttributes ??= new();

        FillWithDefaultValues(request.DocumentAttributes);

        response.JobState = JobState.Pending;
        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private ReleaseJobResponse GetResumePrinterResponse(ResumePrinterRequest request)
    {
        return new ReleaseJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version
        };
    }

    private ReleaseJobResponse GetRestartJobResponse(RestartJobRequest request)
    {
        var response = new ReleaseJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = request.JobId;

        if (jobId.HasValue && Mind.PrinterDb.SetJobState(jobId.Value, PrinterJobState.Pending))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private ReleaseJobResponse GetReleaseJobResponse(ReleaseJobRequest request)
    {
        var response = new ReleaseJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = request.JobId;

        if (jobId.HasValue && Mind.PrinterDb.SetJobState(jobId.Value, PrinterJobState.Pending))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private PurgeJobsResponse GetPurgeJobsResponse(PurgeJobsRequest request)
    {
        return new PurgeJobsResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk
        };
    }

    private PrintUriResponse GetPrintUriResponse(PrintUriRequest request)
    {
        var response = new PrintUriResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            JobState = JobState.Pending,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = Mind.PrinterDb.CreateJob(request.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.PrinterUri + $"/{jobId}";

        request.NewJobAttributes ??= new();

        FillWithDefaultValues(jobId, request.NewJobAttributes);

        request.DocumentAttributes ??= new();

        FillWithDefaultValues(request.DocumentAttributes);

        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private PrintJobResponse GetPrintJobResponse(PrintJobRequest request)
    {
        var response = new PrintJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            JobState = JobState.Pending,
            StatusCode = IppStatusCode.ClientErrorNotPossible,
            JobStateReasons = [JobStateReason.None]
        };

        var jobId = Mind.PrinterDb.CreateJob(request.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.PrinterUri + $"/{jobId}";

        request.NewJobAttributes ??= new();

        FillWithDefaultValues(jobId, request.NewJobAttributes);

        request.DocumentAttributes ??= new();

        FillWithDefaultValues(request.DocumentAttributes);

        if (!Mind.PrinterDb.SetJobDocumentData(
            jobId,
            JsonSerializer.Serialize(request.DocumentAttributes),
            JsonSerializer.Serialize(request.NewJobAttributes),
            request.Document.ReadAllBytes()))
        {
            return response;
        }

        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private PausePrinterResponse GetPausePrinterResponse(PausePrinterRequest request)
    {
        return new PausePrinterResponse
        {
            RequestId = request.RequestId,
            Version = request.Version
        };
    }

    private HoldJobResponse GetHoldJobResponse(HoldJobRequest request)
    {
        var response = new HoldJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        var jobId = request.JobId;

        if (jobId.HasValue && Mind.PrinterDb.SetJobState(jobId.Value, 0))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private GetPrinterAttributesResponse GetPrinterAttributesResponse(GetPrinterAttributesRequest request)
    {
        bool IsRequired(string attributeName) => request.RequestedAttributes?.Contains(attributeName) ?? true;

        return new GetPrinterAttributesResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk,
            PrinterState = !IsRequired(PrinterAttribute.PrinterState) ? null : Mind.PrinterDb.GetPrinterState(),
            PrinterStateReasons = !IsRequired(PrinterAttribute.PrinterStateReasons) ? null : ["none"],
            CharsetConfigured = !IsRequired(PrinterAttribute.CharsetConfigured) ? null : "utf-8",
            CharsetSupported = !IsRequired(PrinterAttribute.CharsetSupported) ? null : ["utf-8"],
            NaturalLanguageConfigured = !IsRequired(PrinterAttribute.NaturalLanguageConfigured) ? null : "en-us",
            GeneratedNaturalLanguageSupported = !IsRequired(PrinterAttribute.GeneratedNaturalLanguageSupported) ? null : ["en-us"],
            PrinterIsAcceptingJobs = !IsRequired(PrinterAttribute.PrinterIsAcceptingJobs) ? null : true,
            PrinterMakeAndModel = !IsRequired(PrinterAttribute.PrinterMakeAndModel) ? null : DEFAULT_PRINTER_NAME,
            PrinterName = !IsRequired(PrinterAttribute.PrinterName) ? null : DEFAULT_PRINTER_NAME,
            PrinterInfo = !IsRequired(PrinterAttribute.PrinterInfo) ? null : DEFAULT_PRINTER_NAME,
            IppVersionsSupported = !IsRequired(PrinterAttribute.IppVersionsSupported) ? null : [new IppVersion(1, 0), IppVersion.V1_1],
            DocumentFormatDefault = !IsRequired(PrinterAttribute.DocumentFormatDefault) ? null : DEFAULT_DOCUMENT_FORMAT,
            ColorSupported = !IsRequired(PrinterAttribute.ColorSupported) ? null : true,
            PrinterCurrentTime = !IsRequired(PrinterAttribute.PrinterCurrentTime) ? null : DateTime.UtcNow,
            OperationsSupported = !IsRequired(PrinterAttribute.OperationsSupported) ? null :
            [
                IppOperation.PrintJob,
                IppOperation.PrintUri,
                IppOperation.ValidateJob,
                IppOperation.CreateJob,
                IppOperation.SendDocument,
                IppOperation.SendUri,
                IppOperation.CancelJob,
                IppOperation.GetJobAttributes,
                IppOperation.GetJobs,
                IppOperation.GetPrinterAttributes,
                IppOperation.HoldJob,
                IppOperation.ReleaseJob,
                IppOperation.RestartJob,
                IppOperation.PausePrinter,
                IppOperation.ResumePrinter
            ],
            QueuedJobCount = !IsRequired(PrinterAttribute.QueuedJobCount) ? null : Mind.PrinterDb.GetProcessingJobCount(),
            DocumentFormatSupported = !IsRequired(PrinterAttribute.DocumentFormatSupported) ? null : [DEFAULT_DOCUMENT_FORMAT],
            MultipleDocumentJobsSupported = !IsRequired(PrinterAttribute.MultipleDocumentJobsSupported) ? null : true,
            CompressionSupported = !IsRequired(PrinterAttribute.CompressionSupported) ? null : [Compression.None],
            PrinterLocation = !IsRequired(PrinterAttribute.PrinterLocation) ? null : "VintageHive",
            PrintScalingDefault = !IsRequired(PrinterAttribute.PrintScalingDefault) ? null : DEAFULT_PRINT_SCALING,
            PrintScalingSupported = !IsRequired(PrinterAttribute.PrintScalingSupported) ? null : [DEAFULT_PRINT_SCALING],
            PrinterUriSupported = !IsRequired(PrinterAttribute.PrinterUriSupported) ? null : [PrinterUrl],
            UriAuthenticationSupported = !IsRequired(PrinterAttribute.UriAuthenticationSupported) ? null : [UriAuthentication.None],
            UriSecuritySupported = !IsRequired(PrinterAttribute.UriSecuritySupported) ? null : [UriSecurity.None],
            PrinterUpTime = !IsRequired(PrinterAttribute.PrinterUpTime) ? null : (int)Mind.TotalRuntime.TotalSeconds,
            MediaDefault = !IsRequired(PrinterAttribute.MediaDefault) ? null : DEFAULT_MEDIA_SIZE,
            MediaColDefault = !IsRequired(PrinterAttribute.MediaDefault) ? null : DEFAULT_MEDIA_SIZE,
            MediaSupported = !IsRequired(PrinterAttribute.MediaSupported) ? null : [DEFAULT_MEDIA_SIZE],
            SidesDefault = !IsRequired(PrinterAttribute.SidesDefault) ? null : DEFAULT_NUMBER_OF_SIDES,
            SidesSupported = !IsRequired(PrinterAttribute.SidesSupported) ? null : Enum.GetValues(typeof(Sides)).Cast<Sides>().Where(x => x != Sides.Unsupported).ToArray(),
            PdlOverrideSupported = !IsRequired(PrinterAttribute.PdlOverrideSupported) ? null : "attempted",
            MultipleOperationTimeOut = !IsRequired(PrinterAttribute.MultipleOperationTimeOut) ? null : 120,
            FinishingsDefault = !IsRequired(PrinterAttribute.FinishingsDefault) ? null : DEFAULT_FINISHINGS,
            PrinterResolutionDefault = !IsRequired(PrinterAttribute.PrinterResolutionDefault) ? null : DEAULT_PRINTER_RESOLUTION,
            PrinterResolutionSupported = !IsRequired(PrinterAttribute.PrinterResolutionSupported) ? null : [DEAULT_PRINTER_RESOLUTION],
            PrintQualityDefault = !IsRequired(PrinterAttribute.PrintQualityDefault) ? null : DEFAULT_PRINT_QUALITY,
            PrintQualitySupported = !IsRequired(PrinterAttribute.PrintQualitySupported) ? null : [DEFAULT_PRINT_QUALITY],
            JobPriorityDefault = !IsRequired(PrinterAttribute.JobPriorityDefault) ? null : DEFAULT_JOB_PRIORITY,
            JobPrioritySupported = !IsRequired(PrinterAttribute.JobPrioritySupported) ? null : DEFAULT_JOB_PRIORITY,
            CopiesDefault = !IsRequired(PrinterAttribute.CopiesDefault) ? null : DEFAULT_NUMBER_OF_COPIES,
            CopiesSupported = !IsRequired(PrinterAttribute.CopiesSupported) ? null : new SharpIpp.Protocol.Models.Range(DEFAULT_NUMBER_OF_COPIES, DEFAULT_NUMBER_OF_COPIES),
            OrientationRequestedDefault = !IsRequired(PrinterAttribute.OrientationRequestedDefault) ? null : DEFAULT_ORIENTATION,
            OrientationRequestedSupported = !IsRequired(PrinterAttribute.OrientationRequestedSupported) ? null : Enum.GetValues(typeof(Orientation)).Cast<Orientation>().Where(x => x != Orientation.Unsupported).ToArray(),
            PageRangesSupported = !IsRequired(PrinterAttribute.PageRangesSupported) ? null : false,
            PagesPerMinute = !IsRequired(PrinterAttribute.PagesPerMinute) ? null : 20,
            PagesPerMinuteColor = !IsRequired(PrinterAttribute.PagesPerMinuteColor) ? null : 20,
            PrinterMoreInfo = !IsRequired(PrinterAttribute.PrinterMoreInfo) ? null : PrinterUrl,
            JobHoldUntilSupported = !IsRequired(PrinterAttribute.JobHoldUntilSupported) ? null : [DEFAULT_JOB_UNTIL],
            JobHoldUntilDefault = !IsRequired(PrinterAttribute.JobHoldUntilDefault) ? null : DEFAULT_JOB_UNTIL,
            ReferenceUriSchemesSupported = !IsRequired(PrinterAttribute.ReferenceUriSchemesSupported) ? null : [UriScheme.Ftp, UriScheme.Http, UriScheme.Https],
        };
    }

    private GetJobsResponse GetJobsResponse(GetJobsRequest request)
    {
        return new GetJobsResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk,
            Jobs = []
        };
    }

    private GetJobAttributesResponse GetJobAttributesResponse(GetJobAttributesRequest request)
    {
        var response = new GetJobAttributesResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible,
            JobAttributes = new JobAttributes()
        };

        return response;
    }

    private static CUPSGetPrintersResponse GetCUPSGetPrintersResponse(CUPSGetPrintersRequest request)
    {
        return new CUPSGetPrintersResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk
        };
    }

    private CancelJobResponse GetCancelJobResponse(CancelJobRequest request)
    {
        var response = new CancelJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        if (request.JobId.HasValue && Mind.PrinterDb.SetJobState(request.JobId.Value, PrinterJobState.Canceled))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private CreateJobResponse GetCreateJobResponse(CreateJobRequest request)
    {
        request.NewJobAttributes ??= new();

        var response = new CreateJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            JobState = JobState.Pending,
            StatusCode = IppStatusCode.ClientErrorNotPossible,
            JobStateReasons = [JobStateReason.None]
        };

        var jobId = Mind.PrinterDb.CreateJob(request.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.PrinterUri + $"/{jobId}";

        FillWithDefaultValues(jobId, request.NewJobAttributes);

        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private static void FillWithDefaultValues(int jobId, NewJobAttributes attributes)
    {
        attributes.PrintScaling ??= DEAFULT_PRINT_SCALING;
        attributes.Sides ??= DEFAULT_NUMBER_OF_SIDES;
        attributes.Media ??= DEFAULT_MEDIA_SIZE;
        attributes.PrinterResolution ??= DEAULT_PRINTER_RESOLUTION;
        attributes.Finishings ??= DEFAULT_FINISHINGS;
        attributes.PrintQuality ??= DEFAULT_PRINT_QUALITY;
        attributes.JobPriority ??= DEFAULT_JOB_PRIORITY;
        attributes.Copies ??= DEFAULT_NUMBER_OF_COPIES;
        attributes.OrientationRequested ??= DEFAULT_ORIENTATION;
        attributes.JobHoldUntil ??= DEFAULT_JOB_UNTIL;

        if (string.IsNullOrEmpty(attributes.JobName))
        {
            attributes.JobName = $"Job {jobId}";
        }
    }

    private static void FillWithDefaultValues(DocumentAttributes attributes)
    {
        if (string.IsNullOrEmpty(attributes.DocumentFormat))
        {
            attributes.DocumentFormat = DEFAULT_DOCUMENT_FORMAT;
        }
    }
}
