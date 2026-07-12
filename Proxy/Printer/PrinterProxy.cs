// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using SharpIpp;
using SharpIpp.Exceptions;
using SharpIpp.Models;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;
using VintageHive.Network;
using VintageHive.Proxy.Http;
using static VintageHive.Proxy.Http.HttpUtilities;

namespace VintageHive.Proxy.Printer;

public class PrinterProxy : Listener
{
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

    public PrinterProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var httpRequest = await HttpRequest.Build(connection, Encoding.ASCII, data[..read]);

        if (!httpRequest.IsValid)
        {
            Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $"Unhandled type of IPP request; {Encoding.GetString(data[..read])}", httpRequest.ListenerSocket?.TraceId.ToString() ?? "N/A");

            return null;
        }

        // Per-request local (was a shared static that raced across concurrent IPP connections)
        var printerUrl = $"http://{httpRequest.Uri.Authority}/";

        var httpResponse = new HttpResponse(httpRequest);

        switch (httpRequest.Method)
        {
            case "GET":
            {
                httpResponse.SetBodyString($"{Mind.ProductName} IPP Proxy", "text/plain");

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
                        GetJobAttributesRequest x => GetJobAttributesResponse(x, printerUrl),
                        GetJobsRequest x => GetJobsResponse(x, printerUrl),
                        GetPrinterAttributesRequest x => GetPrinterAttributesResponse(x, printerUrl),
                        HoldJobRequest x => GetHoldJobResponse(x),
                        PausePrinterRequest x => GetPausePrinterResponse(x),
                        PrintJobRequest x => GetPrintJobResponse(x),
                        PrintUriRequest x => GetPrintUriResponse(x),
                        PurgeJobsRequest x => GetPurgeJobsResponse(x),
                        ReleaseJobRequest x => GetReleaseJobResponse(x),
                        RestartJobRequest x => GetRestartJobResponse(x),
                        ResumePrinterRequest x => GetResumePrinterResponse(x),
                        SendDocumentRequest x => GetSendDocumentResponse(x, printerUrl),
                        SendUriRequest x => SendUriResponse(x),
                        ValidateJobRequest x => GetValidateJobResponse(x),
                        _ => null
                    };

                    if (ippResponse == null)
                    {
                        // Unknown/unsupported IPP operation - reply with a well-formed status instead of
                        // tearing down the connection.
                        var unsupported = new IppResponseMessage
                        {
                            RequestId = ippRequest.RequestId,
                            Version = ippRequest.Version,
                            StatusCode = IppStatusCode.ServerErrorOperationNotSupported
                        };

                        var operation = new IppSection { Tag = SectionTag.OperationAttributesTag };

                        operation.Attributes.Add(new IppAttribute(Tag.Charset, JobAttribute.AttributesCharset, "utf-8"));
                        operation.Attributes.Add(new IppAttribute(Tag.NaturalLanguage, JobAttribute.AttributesNaturalLanguage, "en"));

                        unsupported.Sections.Add(operation);

                        Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Unsupported IPP operation: {ippRequest.GetType().Name}", connection.TraceId.ToString());

                        await _ippServer.SendRawResponseAsync(unsupported, responseStream);

                        return WrapIpp(httpResponse, responseStream);
                    }

                    await _ippServer.SendResponseAsync(ippResponse, responseStream);

                    return WrapIpp(httpResponse, responseStream);
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

                    return WrapIpp(httpResponse, responseStream);
                }
                catch (Exception ex)
                {
                    Log.WriteLine(Log.LEVEL_ERROR, nameof(PrinterProxy), $"Process Request failed: Exception {ex}", "");

                    httpResponse.SetStatusCode(Http.HttpStatusCode.InternalServerError);
                    httpResponse.SetBodyString("IPP processing error", "text/plain");

                    return httpResponse.GetResponseEncodedData();
                }
            }

            default:
            {
                Log.WriteLine(Log.LEVEL_ERROR, nameof(HttpProxy), $"Unhandled IPP method; {httpRequest.Method}", httpRequest.ListenerSocket?.TraceId.ToString() ?? "N/A");
                return null;
            }
        }
    }

    private static byte[] WrapIpp(HttpResponse httpResponse, MemoryStream responseStream)
    {
        // Wrap the raw IPP bytes in a proper HTTP response. The old code returned raw IPP with no HTTP status
        // line - era WinInet clients tolerated it but CUPS / strict IPP tooling reject a non-HTTP response.
        httpResponse.SetBodyData(responseStream.ToArray(), "application/ipp");

        return httpResponse.GetResponseEncodedData();
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

    private static SendUriResponse SendUriResponse(SendUriRequest request)
    {
        // VintageHive does not fetch documents from URIs, so report unsupported instead of stranding a New job
        return new SendUriResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ServerErrorOperationNotSupported
        };
    }

    private SendDocumentResponse GetSendDocumentResponse(SendDocumentRequest request, string printerUrl)
    {
        var response = new SendDocumentResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible
        };

        // Send-Document must attach to the job Create-Job made (was creating a brand-new job and discarding the
        // document, stranding both in New/Pending forever). Store the bytes on that job and move it to Pending.
        var jobId = request.OperationAttributes.JobId;

        if (!jobId.HasValue)
        {
            return response;
        }

        if (string.IsNullOrEmpty(request.OperationAttributes.DocumentFormat))
        {
            request.OperationAttributes.DocumentFormat = DEFAULT_DOCUMENT_FORMAT;
        }

        var documentData = request.Document?.ReadAllBytes() ?? [];

        if (!Mind.PrinterDb.SetJobDocumentData(jobId.Value, JsonSerializer.Serialize(request.OperationAttributes), "{}", documentData))
        {
            return response;
        }

        response.JobId = jobId.Value;
        response.JobUri = printerUrl + jobId.Value;
        response.JobState = JobState.Pending;
        response.StatusCode = IppStatusCode.SuccessfulOk;

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(PrinterProxy), $"SendDocument stored {documentData.Length} bytes for job {jobId.Value}", "");

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

        var jobId = request.OperationAttributes.JobId;

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

        var jobId = request.OperationAttributes.JobId;

        if (jobId.HasValue && Mind.PrinterDb.SetJobState(jobId.Value, PrinterJobState.Pending))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private PurgeJobsResponse GetPurgeJobsResponse(PurgeJobsRequest request)
    {
        var purged = Mind.PrinterDb.PurgeTerminalJobs();

        Log.WriteLine(Log.LEVEL_INFO, nameof(PrinterProxy), $"Purge-Jobs removed {purged} finished job(s)", "");

        return new PurgeJobsResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk
        };
    }

    private static PrintUriResponse GetPrintUriResponse(PrintUriRequest request)
    {
        // VintageHive does not fetch documents from URIs, so report unsupported instead of stranding a New job
        return new PrintUriResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            JobState = JobState.Aborted,
            StatusCode = IppStatusCode.ServerErrorOperationNotSupported
        };
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

        var jobId = Mind.PrinterDb.CreateJob(request.OperationAttributes.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.OperationAttributes.PrinterUri + $"/{jobId}";

        request.JobTemplateAttributes ??= new();

        FillWithDefaultValues(request.JobTemplateAttributes);

        if (string.IsNullOrEmpty(request.OperationAttributes.JobName))
        {
            request.OperationAttributes.JobName = $"Job {jobId}";
        }

        if (string.IsNullOrEmpty(request.OperationAttributes.DocumentFormat))
        {
            request.OperationAttributes.DocumentFormat = DEFAULT_DOCUMENT_FORMAT;
        }

        if (!Mind.PrinterDb.SetJobDocumentData(
            jobId,
            JsonSerializer.Serialize(request.OperationAttributes),
            JsonSerializer.Serialize(request.JobTemplateAttributes),
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

        var jobId = request.OperationAttributes.JobId;

        if (jobId.HasValue && Mind.PrinterDb.SetJobState(jobId.Value, PrinterJobState.PendingHeld))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private GetPrinterAttributesResponse GetPrinterAttributesResponse(GetPrinterAttributesRequest request, string printerUrl)
    {
        bool IsRequired(string attributeName) => request.OperationAttributes.RequestedAttributes?.Contains(attributeName) ?? true;

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
            DocumentFormatSupported = !IsRequired(PrinterAttribute.DocumentFormatSupported) ? null : [
                HttpContentTypeMimeType.Application.Pdf,
                HttpContentTypeMimeType.Application.PostScript,
                HttpContentTypeMimeType.Text.Plain,
                HttpContentTypeMimeType.Application.OctetStream,
            ],
            MultipleDocumentJobsSupported = !IsRequired(PrinterAttribute.MultipleDocumentJobsSupported) ? null : true,
            CompressionSupported = !IsRequired(PrinterAttribute.CompressionSupported) ? null : [Compression.None],
            PrinterLocation = !IsRequired(PrinterAttribute.PrinterLocation) ? null : Mind.ProductName,
            PrintScalingDefault = !IsRequired(PrinterAttribute.PrintScalingDefault) ? null : DEAFULT_PRINT_SCALING,
            PrintScalingSupported = !IsRequired(PrinterAttribute.PrintScalingSupported) ? null : [DEAFULT_PRINT_SCALING],
            PrinterUriSupported = !IsRequired(PrinterAttribute.PrinterUriSupported) ? null : [printerUrl],
            UriAuthenticationSupported = !IsRequired(PrinterAttribute.UriAuthenticationSupported) ? null : [UriAuthentication.None],
            UriSecuritySupported = !IsRequired(PrinterAttribute.UriSecuritySupported) ? null : [UriSecurity.None],
            PrinterUpTime = !IsRequired(PrinterAttribute.PrinterUpTime) ? null : (int)Mind.TotalRuntime.TotalSeconds,
            MediaDefault = !IsRequired(PrinterAttribute.MediaDefault) ? null : DEFAULT_MEDIA_SIZE,
            MediaColDefault = !IsRequired(PrinterAttribute.MediaDefault) ? null : new MediaCol { MediaSize = new MediaSize { XDimension = 21000, YDimension = 29700 } },
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
            PrinterMoreInfo = !IsRequired(PrinterAttribute.PrinterMoreInfo) ? null : printerUrl,
            JobHoldUntilSupported = !IsRequired(PrinterAttribute.JobHoldUntilSupported) ? null : [DEFAULT_JOB_UNTIL],
            JobHoldUntilDefault = !IsRequired(PrinterAttribute.JobHoldUntilDefault) ? null : DEFAULT_JOB_UNTIL,
            ReferenceUriSchemesSupported = !IsRequired(PrinterAttribute.ReferenceUriSchemesSupported) ? null : [UriScheme.Ftp, UriScheme.Http, UriScheme.Https],
        };
    }

    private GetJobsResponse GetJobsResponse(GetJobsRequest request, string printerUrl)
    {
        var allJobs = Mind.PrinterDb.GetAllJobs();

        // Filter by requested states if specified
        var whichJobs = request.OperationAttributes?.WhichJobs;
        var filteredJobs = allJobs;

        if (whichJobs == WhichJobs.Completed)
        {
            filteredJobs = allJobs.Where(j => j.State == PrinterJobState.Completed || j.State == PrinterJobState.Canceled || j.State == PrinterJobState.Aborted).ToList();
        }
        else if (whichJobs == WhichJobs.NotCompleted)
        {
            filteredJobs = allJobs.Where(j => j.State != PrinterJobState.Completed && j.State != PrinterJobState.Canceled && j.State != PrinterJobState.Aborted).ToList();
        }

        var limit = request.OperationAttributes?.Limit ?? 50;
        filteredJobs = filteredJobs.Take(limit).ToList();

        var jobs = filteredJobs.Select(j => new JobDescriptionAttributes
        {
            JobId = j.Id,
            JobState = MapJobState(j.State),
            JobName = j.Name ?? $"Job {j.Id}",
            JobUri = printerUrl + j.Id,
            JobStateReasons = [MapJobStateReason(j.State)],
            TimeAtCreation = (int)(j.Created - DateTime.UnixEpoch).TotalSeconds,
            TimeAtProcessing = j.Processed.HasValue ? (int)(j.Processed.Value - DateTime.UnixEpoch).TotalSeconds : 0,
            TimeAtCompleted = j.Completed.HasValue ? (int)(j.Completed.Value - DateTime.UnixEpoch).TotalSeconds : 0,
        }).ToArray();

        return new GetJobsResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.SuccessfulOk,
            Jobs = jobs
        };
    }

    private GetJobAttributesResponse GetJobAttributesResponse(GetJobAttributesRequest request, string printerUrl)
    {
        var response = new GetJobAttributesResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            StatusCode = IppStatusCode.ClientErrorNotPossible,
            JobAttributes = new JobDescriptionAttributes()
        };

        var jobId = request.OperationAttributes?.JobId;

        if (!jobId.HasValue)
        {
            return response;
        }

        var job = Mind.PrinterDb.GetJob(jobId.Value);

        if (job == null)
        {
            return response;
        }

        response.StatusCode = IppStatusCode.SuccessfulOk;
        response.JobAttributes = new JobDescriptionAttributes
        {
            JobId = job.Id,
            JobState = MapJobState(job.State),
            JobName = job.Name ?? $"Job {job.Id}",
            JobUri = printerUrl + job.Id,
            JobPrinterUri = printerUrl,
            JobStateReasons = [MapJobStateReason(job.State)],
            TimeAtCreation = (int)(job.Created - DateTime.UnixEpoch).TotalSeconds,
            TimeAtProcessing = job.Processed.HasValue ? (int)(job.Processed.Value - DateTime.UnixEpoch).TotalSeconds : 0,
            TimeAtCompleted = job.Completed.HasValue ? (int)(job.Completed.Value - DateTime.UnixEpoch).TotalSeconds : 0,
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

        if (request.OperationAttributes.JobId.HasValue && Mind.PrinterDb.SetJobState(request.OperationAttributes.JobId.Value, PrinterJobState.Canceled))
        {
            response.StatusCode = IppStatusCode.SuccessfulOk;
        }

        return response;
    }

    private CreateJobResponse GetCreateJobResponse(CreateJobRequest request)
    {
        request.JobTemplateAttributes ??= new();

        var response = new CreateJobResponse
        {
            RequestId = request.RequestId,
            Version = request.Version,
            JobState = JobState.Pending,
            StatusCode = IppStatusCode.ClientErrorNotPossible,
            JobStateReasons = [JobStateReason.None]
        };

        var jobId = Mind.PrinterDb.CreateJob(request.OperationAttributes.RequestingUserName);

        response.JobId = jobId;
        response.JobUri = request.OperationAttributes.PrinterUri + $"/{jobId}";

        FillWithDefaultValues(request.JobTemplateAttributes);

        if (string.IsNullOrEmpty(request.OperationAttributes.JobName))
        {
            request.OperationAttributes.JobName = $"Job {jobId}";
        }

        response.StatusCode = IppStatusCode.SuccessfulOk;

        return response;
    }

    private static void FillWithDefaultValues(JobTemplateAttributes attributes)
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
    }

    private static JobState MapJobState(PrinterJobState state)
    {
        return state switch
        {
            PrinterJobState.Pending => JobState.Pending,
            PrinterJobState.PendingHeld => JobState.PendingHeld,
            PrinterJobState.Processing => JobState.Processing,
            PrinterJobState.ProcessingStopped => JobState.ProcessingStopped,
            PrinterJobState.Canceled => JobState.Canceled,
            PrinterJobState.Aborted => JobState.Aborted,
            PrinterJobState.Completed => JobState.Completed,
            _ => JobState.Pending
        };
    }

    private static JobStateReason MapJobStateReason(PrinterJobState state)
    {
        return state switch
        {
            PrinterJobState.Completed => JobStateReason.JobCompletedSuccessfully,
            PrinterJobState.Canceled => JobStateReason.JobCanceledByUser,
            PrinterJobState.Aborted => JobStateReason.AbortedBySystem,
            PrinterJobState.Processing => JobStateReason.JobPrinting,
            _ => JobStateReason.None
        };
    }
}
