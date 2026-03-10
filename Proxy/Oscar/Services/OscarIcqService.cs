// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar.Services;

internal class OscarIcqService : IOscarService
{
    public const ushort FAMILY_ID = 0x15;

    public const ushort CLI_SRV_ERROR = 0x01;
    public const ushort CLI_META_REQ = 0x02;
    public const ushort SRV_META_REPLY = 0x03;

    public const ushort CLI_META_INFO_REQ = 0x07D0;
    public const ushort META_DATA = 0x07DA;

    public const ushort CLI_FULLINFO_REQUEST2 = 0x04D0;
    public const ushort CLI_SET_BASIC_INFO = 0x03EA;
    public const ushort CLI_SET_WORK_INFO = 0x03F3;
    public const ushort CLI_SET_MORE_INFO = 0x03FD;
    public const ushort CLI_SET_NOTES_INFO = 0x0406;
    public const ushort CLI_SET_EMAIL_INFO = 0x040B;
    public const ushort CLI_SET_INTERESTS_INFO = 0x0410;
    public const ushort CLI_SET_AFFILIATIONS_INFO = 0x041A;

    public const ushort META_BASIC_USERINFO = 0x00C8;
    public const ushort META_MORE_USERINFO = 0x00DC;
    public const ushort META_EMAIL_USERINFO = 0x00EB;
    public const ushort META_HPAGECAT_USERINFO = 0x010E;
    public const ushort META_WORK_USERINFO = 0x00D2;
    public const ushort META_NOTES_USERINFO = 0x00E6;
    public const ushort META_INTERESTS_USERINFO = 0x00F0;
    public const ushort META_AFFILATIONS_USERINFO = 0x00FA;
    public const ushort META_XML_INFO = 0x08A2;
    public const ushort META_SET_PERMS_USERINFO = 0x0424;
    public const ushort META_SET_PERMS_ACK = 0x00A0;
    public const ushort META_SET_ACK = 0x00B4;
    public const ushort META_USER_FOUND_LAST = 0x01AE;
    public const ushort CLI_OFFLINE_MESSAGE_REQ = 0x003C;
    public const ushort SRV_END_OF_OFFLINE_MSGS = 0x0042;
    public const ushort CLI_REQ_XML_INFO = 0x0898;
    public const ushort CLI_UNKNOWN_ONE = 0x0758;
    public const ushort CLI_UNKNOWN_TWO = 0x06EA;
    public const ushort CLI_FIND_BY_UIN = 0x051F;

    public const byte DATA_CHUNK_HEADER_SIZE = 0x10;

    public const string XML_REQ_BANNERIP = "<key>BannersIP</key>";

    public ushort Family => FAMILY_ID;

    public OscarServer Server { get; }

    public OscarIcqService(OscarServer server)
    {
        Server = server;
    }

    public async Task ProcessSnac(OscarSession session, Snac snac)
    {
        switch (snac.SubType)
        {
            case CLI_META_REQ:
            {
                var icqMetaReq = new IcqUserMetaRequest(snac);

                switch (icqMetaReq.RequestType)
                {
                    case CLI_META_INFO_REQ:
                    {
                        await HandleMetaInfoRequest(session, snac, icqMetaReq);
                    }
                    break;

                    case CLI_OFFLINE_MESSAGE_REQ:
                    {
                        // Deliver any pending offline messages via ICQ format
                        var offlineMessages = Mind.Db.OscarGetOfflineMessages(session.ScreenName);

                        foreach (var msg in offlineMessages)
                        {
                            await SendIcqOfflineMessage(session, snac, icqMetaReq, msg);
                        }

                        Mind.Db.OscarDeleteOfflineMessages(session.ScreenName);

                        // Send end-of-offline-messages marker
                        var mem = new MemoryStream();

                        mem.Write(BitConverter.GetBytes((ushort)9));
                        mem.Write(BitConverter.GetBytes(icqMetaReq.ClientUin));
                        mem.Write(BitConverter.GetBytes(SRV_END_OF_OFFLINE_MSGS));
                        mem.Write(BitConverter.GetBytes((ushort)0x0002));
                        mem.WriteByte(0x00);

                        var offlineTlv = new Tlv(0x01, mem.ToArray());

                        var endOfOfflineSnac = snac.NewReply(FAMILY_ID, SRV_META_REPLY);

                        endOfOfflineSnac.WriteTlv(offlineTlv);

                        await session.SendSnac(endOfOfflineSnac);
                    }
                    break;

                    default:
                    {
                        Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcqService), $"Unknown meta request type 0x{icqMetaReq.RequestType:X4}", session.Client.TraceId.ToString());
                    }
                    break;
                }
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcqService), $"Unknown SNAC subtype 0x{snac.SubType:X4} for family 0x{FAMILY_ID:X4}", session.Client.TraceId.ToString());
            }
            break;
        }
    }

    private async Task HandleMetaInfoRequest(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var traceId = session.Client.TraceId.ToString();

        switch (icqMetaReq.RequestSubType)
        {
            case CLI_FULLINFO_REQUEST2:
            {
                // Get real profile from database
                var profile = GetProfileForUin(icqMetaReq.SearchUin, session.ScreenName);

                await SendBasicUserInfo(session, snac, icqMetaReq, profile);
                await SendMoreUserInfo(session, snac, icqMetaReq, profile);
                await SendEmailUserInfo(session, snac, icqMetaReq, profile);
                await SendHomepageCatUserInfo(session, snac, icqMetaReq);
                await SendWorkUserInfo(session, snac, icqMetaReq, profile);
                await SendNotesUserInfo(session, snac, icqMetaReq, profile);
                await SendInterestsUserInfo(session, snac, icqMetaReq, profile);
                await SendAffiliationUserInfo(session, snac, icqMetaReq, profile);
            }
            break;

            case CLI_REQ_XML_INFO:
            {
                switch (icqMetaReq.XmlKey)
                {
                    case XML_REQ_BANNERIP:
                    {
                        var mem = new MemoryStream();

                        mem.WriteByte(0x0A);
                        mem.Write(WriteIcqString("<value>http://192.168.69.1/api/icq2000a/%d/%s.cb</value>"));

                        await SendMetaData(session, snac, icqMetaReq, META_XML_INFO, mem);
                    }
                    break;

                    default:
                    {
                        Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcqService), $"Unknown XML request key: {icqMetaReq.XmlKey}", traceId);
                    }
                    break;
                }
            }
            break;

            case META_SET_PERMS_USERINFO:
            {
                var mem = new MemoryStream();

                mem.WriteByte(0x0A);

                await SendMetaData(session, snac, icqMetaReq, META_SET_PERMS_ACK, mem);
            }
            break;

            case CLI_SET_BASIC_INFO:
            {
                await HandleSetBasicInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_SET_WORK_INFO:
            {
                await HandleSetWorkInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_SET_MORE_INFO:
            {
                await HandleSetMoreInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_SET_NOTES_INFO:
            {
                await HandleSetNotesInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_SET_INTERESTS_INFO:
            {
                await HandleSetInterestsInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_SET_AFFILIATIONS_INFO:
            {
                await HandleSetAffiliationsInfo(session, snac, icqMetaReq);
            }
            break;

            case CLI_FIND_BY_UIN:
            {
                var profile = GetProfileForUin(icqMetaReq.SearchUin, null);

                var uinUserFound = new MemoryStream();

                uinUserFound.Write(BitConverter.GetBytes(icqMetaReq.SearchUin));

                uinUserFound.Write(WriteIcqString(profile.Nickname));
                uinUserFound.Write(WriteIcqString(profile.FirstName));
                uinUserFound.Write(WriteIcqString(profile.LastName));
                uinUserFound.Write(WriteIcqString(profile.Email));

                uinUserFound.WriteByte(0x01); // Auth required

                uinUserFound.Write(BitConverter.GetBytes((ushort)1)); // Status: online

                uinUserFound.WriteByte(profile.Gender);

                uinUserFound.Write(BitConverter.GetBytes(profile.Age));

                var uinUserFoundData = new MemoryStream();

                uinUserFoundData.WriteByte(0x0A);

                uinUserFoundData.Write(BitConverter.GetBytes((ushort)uinUserFound.Length));

                uinUserFoundData.Write(uinUserFound.ToArray());

                uinUserFoundData.Write(BitConverter.GetBytes((uint)0));

                await SendMetaData(session, snac, icqMetaReq, META_USER_FOUND_LAST, uinUserFoundData, true);
            }
            break;

            case CLI_UNKNOWN_ONE:
            case CLI_UNKNOWN_TWO:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcqService), $"Ignored ICQ meta request subtype 0x{icqMetaReq.RequestSubType:X4}", traceId);
            }
            break;

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(OscarIcqService), $"Unknown ICQ meta request subtype 0x{icqMetaReq.RequestSubType:X4}", traceId);
            }
            break;
        }
    }

    private OscarUserProfile GetProfileForUin(uint searchUin, string fallbackScreenName)
    {
        // In VintageHive, UIN = screenname since usernames are the only identity
        // Try to find a user by UIN if it matches known users, otherwise use fallback
        var screenName = fallbackScreenName ?? "unknown";

        // If searchUin != 0, try to look up by iterating known users (UIN is hash-based)
        // For now, use the requesting user's own profile for self-lookup
        var profile = Mind.Db.OscarGetProfile(screenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(screenName);
            profile = Mind.Db.OscarGetProfile(screenName);
        }

        return profile;
    }

    private async Task HandleSetBasicInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var readIdx = 0;

            profile.Nickname = ReadIcqString(data, ref readIdx);
            profile.FirstName = ReadIcqString(data, ref readIdx);
            profile.LastName = ReadIcqString(data, ref readIdx);
            profile.Email = ReadIcqString(data, ref readIdx);
            profile.HomeCity = ReadIcqString(data, ref readIdx);
            profile.HomeState = ReadIcqString(data, ref readIdx);
            profile.HomePhone = ReadIcqString(data, ref readIdx);
            profile.HomeFax = ReadIcqString(data, ref readIdx);
            profile.HomeAddress = ReadIcqString(data, ref readIdx);
            profile.CellPhone = ReadIcqString(data, ref readIdx);
            profile.HomeZip = ReadIcqString(data, ref readIdx);

            if (readIdx + 2 <= data.Length)
            {
                profile.HomeCountry = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
            }

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated basic info for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task HandleSetWorkInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var readIdx = 0;

            profile.WorkCity = ReadIcqString(data, ref readIdx);
            profile.WorkState = ReadIcqString(data, ref readIdx);
            profile.WorkPhone = ReadIcqString(data, ref readIdx);
            profile.WorkFax = ReadIcqString(data, ref readIdx);
            profile.WorkAddress = ReadIcqString(data, ref readIdx);
            profile.WorkZip = ReadIcqString(data, ref readIdx);

            if (readIdx + 2 <= data.Length)
            {
                profile.WorkCountry = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                readIdx += 2;
            }

            profile.WorkCompany = ReadIcqString(data, ref readIdx);
            profile.WorkDepartment = ReadIcqString(data, ref readIdx);
            profile.WorkPosition = ReadIcqString(data, ref readIdx);

            if (readIdx + 2 <= data.Length)
            {
                profile.WorkOccupation = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                readIdx += 2;
            }

            profile.WorkHomepage = ReadIcqString(data, ref readIdx);

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated work info for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task HandleSetMoreInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var readIdx = 0;

            if (readIdx + 2 <= data.Length)
            {
                profile.Age = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                readIdx += 2;
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.Gender = data[readIdx++];
            }

            profile.Homepage = ReadIcqString(data, ref readIdx);

            if (readIdx + 2 <= data.Length)
            {
                profile.BirthYear = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                readIdx += 2;
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.BirthMonth = data[readIdx++];
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.BirthDay = data[readIdx++];
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.Language1 = data[readIdx++];
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.Language2 = data[readIdx++];
            }

            if (readIdx + 1 <= data.Length)
            {
                profile.Language3 = data[readIdx++];
            }

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated more info for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task HandleSetNotesInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var readIdx = 0;

            profile.Notes = ReadIcqString(data, ref readIdx);

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated notes for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task HandleSetInterestsInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var interests = new List<object>();
            var readIdx = 0;

            if (readIdx < data.Length)
            {
                var count = data[readIdx++];

                for (int i = 0; i < count && readIdx + 2 < data.Length; i++)
                {
                    var category = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                    readIdx += 2;

                    var keyword = ReadIcqString(data, ref readIdx);

                    interests.Add(new { category, keyword });
                }
            }

            profile.InterestsJson = JsonSerializer.Serialize(interests);

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated interests for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task HandleSetAffiliationsInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var profile = Mind.Db.OscarGetProfile(session.ScreenName);

        if (profile == null)
        {
            Mind.Db.OscarEnsureProfileExists(session.ScreenName);
            profile = Mind.Db.OscarGetProfile(session.ScreenName);
        }

        var data = icqMetaReq.GetExtraData();

        if (data != null && data.Length > 0)
        {
            var readIdx = 0;

            // Current affiliations
            var affiliations = new List<object>();

            if (readIdx < data.Length)
            {
                var count = data[readIdx++];

                for (int i = 0; i < count && readIdx + 2 < data.Length; i++)
                {
                    var category = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                    readIdx += 2;

                    var keyword = ReadIcqString(data, ref readIdx);

                    affiliations.Add(new { category, keyword });
                }
            }

            // Past affiliations
            var pastAffiliations = new List<object>();

            if (readIdx < data.Length)
            {
                var count = data[readIdx++];

                for (int i = 0; i < count && readIdx + 2 < data.Length; i++)
                {
                    var category = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
                    readIdx += 2;

                    var keyword = ReadIcqString(data, ref readIdx);

                    pastAffiliations.Add(new { category, keyword });
                }
            }

            profile.AffiliationsJson = JsonSerializer.Serialize(affiliations);
            profile.PastAffiliationsJson = JsonSerializer.Serialize(pastAffiliations);

            Mind.Db.OscarInsertOrUpdateProfile(profile);

            Log.WriteLine(Log.LEVEL_INFO, nameof(OscarIcqService), $"Updated affiliations for {session.ScreenName}", session.Client.TraceId.ToString());
        }

        var ack = new MemoryStream();

        ack.WriteByte(0x0A);

        await SendMetaData(session, snac, icqMetaReq, META_SET_ACK, ack);
    }

    private async Task SendIcqOfflineMessage(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarOfflineMessage msg)
    {
        var mem = new MemoryStream();

        // Offline message header
        var senderUin = (uint)msg.FromScreenName.GetHashCode();

        mem.Write(BitConverter.GetBytes(senderUin));

        // Date/time
        mem.Write(BitConverter.GetBytes((ushort)msg.Timestamp.Year));
        mem.WriteByte((byte)msg.Timestamp.Month);
        mem.WriteByte((byte)msg.Timestamp.Day);
        mem.WriteByte((byte)msg.Timestamp.Hour);
        mem.WriteByte((byte)msg.Timestamp.Minute);

        // Message type (0x01 = plain text)
        mem.WriteByte(0x01);
        mem.WriteByte(0x00);

        // Message text (length-prefixed)
        var msgText = Encoding.ASCII.GetString(msg.MessageData);
        mem.Write(BitConverter.GetBytes((ushort)(msgText.Length + 1)));
        mem.Write(Encoding.ASCII.GetBytes(msgText));
        mem.WriteByte(0x00);

        var offlineTlvData = new MemoryStream();

        offlineTlvData.Write(BitConverter.GetBytes((ushort)(mem.Length + 8))); // data chunk size
        offlineTlvData.Write(BitConverter.GetBytes(icqMetaReq.ClientUin));
        offlineTlvData.Write(BitConverter.GetBytes((ushort)0x0041)); // SRV_OFFLINE_MESSAGE
        offlineTlvData.Write(BitConverter.GetBytes((ushort)0));      // sequence
        offlineTlvData.Write(mem.ToArray());

        var offlineTlv = new Tlv(0x01, offlineTlvData.ToArray());

        var offlineSnac = snac.NewReply(FAMILY_ID, SRV_META_REPLY);

        offlineSnac.WriteTlv(offlineTlv);

        await session.SendSnac(offlineSnac);
    }

    private async Task SendBasicUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var basicUserData = new MemoryStream();

        basicUserData.WriteByte(0x0A); // Success Byte

        basicUserData.Write(WriteIcqString(profile.Nickname));
        basicUserData.Write(WriteIcqString(profile.FirstName));
        basicUserData.Write(WriteIcqString(profile.LastName));
        basicUserData.Write(WriteIcqString(profile.Email));
        basicUserData.Write(WriteIcqString(profile.HomeCity));
        basicUserData.Write(WriteIcqString(profile.HomeState));
        basicUserData.Write(WriteIcqString(profile.HomePhone));
        basicUserData.Write(WriteIcqString(profile.HomeFax));
        basicUserData.Write(WriteIcqString(profile.HomeAddress));
        basicUserData.Write(WriteIcqString(profile.CellPhone));
        basicUserData.Write(WriteIcqString(profile.HomeZip));

        // Country Code
        basicUserData.Write(BitConverter.GetBytes(profile.HomeCountry));

        basicUserData.WriteByte(0xEC);
        basicUserData.WriteByte(0x01);
        basicUserData.WriteByte(0x00);
        basicUserData.WriteByte(0x00);
        basicUserData.WriteByte(0x00);

        await SendMetaData(session, snac, icqMetaReq, META_BASIC_USERINFO, basicUserData, false);
    }

    private async Task SendMoreUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var moreUserData = new MemoryStream();

        moreUserData.WriteByte(0x0A); // Success Byte

        moreUserData.Write(BitConverter.GetBytes(profile.Age));

        moreUserData.WriteByte(profile.Gender);

        moreUserData.Write(WriteIcqString(profile.Homepage));

        moreUserData.Write(BitConverter.GetBytes(profile.BirthYear));
        moreUserData.WriteByte(profile.BirthMonth);
        moreUserData.WriteByte(profile.BirthDay);

        moreUserData.WriteByte(profile.Language1);
        moreUserData.WriteByte(profile.Language2);
        moreUserData.WriteByte(profile.Language3);

        moreUserData.Write(BitConverter.GetBytes((ushort)0)); // Unknown

        moreUserData.Write(WriteIcqString(string.Empty)); // Original city
        moreUserData.Write(WriteIcqString(string.Empty)); // Original state

        // Country Code
        moreUserData.Write(BitConverter.GetBytes(profile.HomeCountry));

        moreUserData.WriteByte(0x00); // GMT offset

        await SendMetaData(session, snac, icqMetaReq, META_MORE_USERINFO, moreUserData, false);
    }

    private async Task SendEmailUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var emailUserData = new MemoryStream();

        emailUserData.WriteByte(0x0A); // Success Byte

        emailUserData.WriteByte(0x00); // No extra emails

        await SendMetaData(session, snac, icqMetaReq, META_EMAIL_USERINFO, emailUserData, false);
    }

    private async Task SendHomepageCatUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var homepageCatUserData = new MemoryStream();

        homepageCatUserData.WriteByte(0x0A); // Success Byte

        homepageCatUserData.WriteByte(0x00); // No homepage categories

        await SendMetaData(session, snac, icqMetaReq, META_HPAGECAT_USERINFO, homepageCatUserData, false);
    }

    private async Task SendWorkUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var workUserData = new MemoryStream();

        workUserData.WriteByte(0x0A); // Success Byte

        workUserData.Write(WriteIcqString(profile.WorkCity));
        workUserData.Write(WriteIcqString(profile.WorkState));
        workUserData.Write(WriteIcqString(profile.WorkPhone));
        workUserData.Write(WriteIcqString(profile.WorkFax));
        workUserData.Write(WriteIcqString(profile.WorkAddress));
        workUserData.Write(WriteIcqString(profile.WorkZip));

        // Country Code
        workUserData.Write(BitConverter.GetBytes(profile.WorkCountry));

        workUserData.Write(WriteIcqString(profile.WorkCompany));
        workUserData.Write(WriteIcqString(profile.WorkDepartment));
        workUserData.Write(WriteIcqString(profile.WorkPosition));

        workUserData.Write(BitConverter.GetBytes(profile.WorkOccupation));

        workUserData.Write(WriteIcqString(profile.WorkHomepage));

        await SendMetaData(session, snac, icqMetaReq, META_WORK_USERINFO, workUserData, false);
    }

    private async Task SendNotesUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var notesData = new MemoryStream();

        notesData.WriteByte(0x0A); // Success Byte

        notesData.Write(WriteIcqString(profile.Notes));

        await SendMetaData(session, snac, icqMetaReq, META_NOTES_USERINFO, notesData, false);
    }

    private async Task SendInterestsUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var interestsUserData = new MemoryStream();

        interestsUserData.WriteByte(0x0A); // Success Byte

        var interests = JsonSerializer.Deserialize<List<JsonElement>>(profile.InterestsJson);

        interestsUserData.WriteByte((byte)interests.Count);

        foreach (var interest in interests)
        {
            var category = interest.GetProperty("category").GetUInt16();
            var keyword = interest.GetProperty("keyword").GetString() ?? string.Empty;

            interestsUserData.Write(BitConverter.GetBytes(category));
            interestsUserData.Write(WriteIcqString(keyword));
        }

        await SendMetaData(session, snac, icqMetaReq, META_INTERESTS_USERINFO, interestsUserData, false);
    }

    private async Task SendAffiliationUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, OscarUserProfile profile)
    {
        var affiliationUserData = new MemoryStream();

        affiliationUserData.WriteByte(0x0A); // Success Byte

        var affiliations = JsonSerializer.Deserialize<List<JsonElement>>(profile.AffiliationsJson);

        affiliationUserData.WriteByte((byte)affiliations.Count);

        foreach (var aff in affiliations)
        {
            var category = aff.GetProperty("category").GetUInt16();
            var keyword = aff.GetProperty("keyword").GetString() ?? string.Empty;

            affiliationUserData.Write(BitConverter.GetBytes(category));
            affiliationUserData.Write(WriteIcqString(keyword));
        }

        var pastAffiliations = JsonSerializer.Deserialize<List<JsonElement>>(profile.PastAffiliationsJson);

        affiliationUserData.WriteByte((byte)pastAffiliations.Count);

        foreach (var aff in pastAffiliations)
        {
            var category = aff.GetProperty("category").GetUInt16();
            var keyword = aff.GetProperty("keyword").GetString() ?? string.Empty;

            affiliationUserData.Write(BitConverter.GetBytes(category));
            affiliationUserData.Write(WriteIcqString(keyword));
        }

        await SendMetaData(session, snac, icqMetaReq, META_AFFILATIONS_USERINFO, affiliationUserData, true);
    }

    private async Task SendMetaData(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq, ushort meta, MemoryStream userinfo, bool isFinal = false)
    {
        var tlvData = new MemoryStream();

        tlvData.Write(BitConverter.GetBytes((ushort)(userinfo.Length + DATA_CHUNK_HEADER_SIZE)));

        tlvData.Write(BitConverter.GetBytes(icqMetaReq.SearchUin));

        tlvData.Write(BitConverter.GetBytes(META_DATA));

        tlvData.Write(BitConverter.GetBytes(icqMetaReq.Sequence));

        tlvData.Write(BitConverter.GetBytes(meta));

        tlvData.Write(userinfo.ToArray());

        var replyTlv = new Tlv(0x01, tlvData.ToArray());

        var metaSnac = snac.NewReply(FAMILY_ID, SRV_META_REPLY, (ushort)(isFinal ? 0 : 1));

        metaSnac.WriteTlv(replyTlv);

        await session.SendSnac(metaSnac);
    }

    private byte[] WriteIcqString(string data)
    {
        var icqString = new MemoryStream();

        icqString.Write(BitConverter.GetBytes((ushort)(data.Length + 1)));
        icqString.Write(Encoding.ASCII.GetBytes(data));
        icqString.WriteByte(0x00); // NUL Terminated String

        return icqString.ToArray();
    }

    private string ReadIcqString(byte[] data, ref int readIdx)
    {
        if (readIdx + 2 > data.Length)
        {
            return string.Empty;
        }

        var length = BitConverter.ToUInt16(data[readIdx..(readIdx + 2)]);
        readIdx += 2;

        if (length <= 1 || readIdx + length > data.Length)
        {
            readIdx += length;
            return string.Empty;
        }

        // Length includes null terminator
        var str = Encoding.ASCII.GetString(data[readIdx..(readIdx + length - 1)]);
        readIdx += length;

        return str;
    }
}
