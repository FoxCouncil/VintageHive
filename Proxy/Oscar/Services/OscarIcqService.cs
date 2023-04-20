// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics;

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

    public dynamic FakeUser = new
    {
        Nickname = "Fox",
        Firstname = "Fox",
        Lastname = "Fox",
        Email = "fox@world.com",
        HomeCity = "Seattle",
        HomeState = "WA",
        HomePhone = "1 (555) 555-1212",
        HomeFax = "1 (555) 555-1213",
        HomeAddress = "123 Yiff Court",
        CellPhone = "1 (555) 555-9999",
        HomeZip = "90210",
        Age = (ushort)69,
        Gender = (byte)1,
        Homepage = "http://foxcouncil.com/",
        BirthYear = (ushort)1969,
        BirthMonth = (byte)12,
        BirthDay = (byte)25,
        WorkCity = "Redmond",
        WorkState = "WA",
        WorkPhone = "1 (555) 555-0069",
        WorkFax = "1 (555) 555-0420",
        WorkAddress = "1 Fox Street",
        WorkZip = "98052-6399",
        WorkCompany = "Macrohard",
        WorkDepartment = "Executive",
        WorkPosition = "Chief Executive Officer",
        WorkHomepage = "http://macrohard.com/",
        Notes = "Really, really, really, gay."
    };

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
                        await Task.Delay(0);

                        Debugger.Break();
                    }
                    break;
                }
            }
            break;

            default:
            {
                await Task.Delay(0);

                Debugger.Break();
            }
            break;
        }
    }

    private async Task HandleMetaInfoRequest(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        switch (icqMetaReq.RequestSubType)
        {
            case CLI_FULLINFO_REQUEST2:
            {
                // TODO: User Retrival.
                await SendBasicUserInfo(session, snac, icqMetaReq);
                await SendMoreUserInfo(session, snac, icqMetaReq);
                await SendEmailUserInfo(session, snac, icqMetaReq);
                await SendHomepageCatUserInfo(session, snac, icqMetaReq);
                await SendWorkUserInfo(session, snac, icqMetaReq);
                await SendNotesUserInfo(session, snac, icqMetaReq);
                await SendInterestsUserInfo(session, snac, icqMetaReq);
                await SendAffiliationUserInfo(session, snac, icqMetaReq);
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
                        Debugger.Break();
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

            case CLI_FIND_BY_UIN:
            {
                var uinUserFound = new MemoryStream();

                uinUserFound.Write(BitConverter.GetBytes(icqMetaReq.SearchUin));

                var dataArray = new string[]
                {
                    FakeUser.Nickname,
                    FakeUser.Firstname,
                    FakeUser.Lastname,
                    FakeUser.Email
                };

                foreach (var data in dataArray)
                {
                    uinUserFound.Write(WriteIcqString(data));
                }

                uinUserFound.WriteByte(0x01);

                uinUserFound.Write(BitConverter.GetBytes((ushort)1));

                uinUserFound.WriteByte(0x02);

                uinUserFound.Write(BitConverter.GetBytes((ushort)69));

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
                // NOOP
                Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Unknown ICQ Meta Request SubTYPE {icqMetaReq.RequestSubType:X4}", session.Client.TraceId.ToString());
            }
            break;

            default:
            {
                await Task.Delay(0);

                // Debugger.Break();

                Log.WriteLine(Log.LEVEL_INFO, GetType().Name, $"Unknown ICQ Meta Request SubTYPE {icqMetaReq.RequestSubType:X4}", session.Client.TraceId.ToString());
            }
            break;
        }
    }    

    private async Task SendBasicUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var basicUserData = new MemoryStream();

        basicUserData.WriteByte(0x0A); // Success Byte

        var dataArray = new string[]
        {
            FakeUser.Nickname,
            FakeUser.Firstname,
            FakeUser.Lastname,
            FakeUser.Email,
            FakeUser.HomeCity,
            FakeUser.HomeState,
            FakeUser.HomePhone,
            FakeUser.HomeFax,
            FakeUser.HomeAddress,
            FakeUser.CellPhone,
            FakeUser.HomeZip
        };

        foreach (var data in dataArray)
        {
            basicUserData.Write(WriteIcqString(data));
        }

        // Country Code (Telephone Style)
        basicUserData.Write(BitConverter.GetBytes((ushort)1));

        basicUserData.WriteByte(0xEC);
        basicUserData.WriteByte(0x01);
        basicUserData.WriteByte(0x00);
        basicUserData.WriteByte(0x00);
        basicUserData.WriteByte(0x00);

        await SendMetaData(session, snac, icqMetaReq, META_BASIC_USERINFO, basicUserData, false);
    }

    private async Task SendMoreUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var moreUserData = new MemoryStream();

        moreUserData.WriteByte(0x0A); // Success Byte

        moreUserData.Write(BitConverter.GetBytes((ushort)69)); // AGE

        moreUserData.WriteByte(2); // GENDER

        var dataArray = new string[]
        {
            FakeUser.Homepage,
            "FUCK1",
            "FUCK2",
        };

        moreUserData.Write(WriteIcqString(dataArray[0]));

        moreUserData.Write(BitConverter.GetBytes((ushort)FakeUser.BirthYear));
        moreUserData.WriteByte(FakeUser.BirthMonth);
        moreUserData.WriteByte(FakeUser.BirthDay);

        moreUserData.WriteByte(12); // English
        moreUserData.WriteByte(17); // French
        moreUserData.WriteByte(27); // Japoneses

        moreUserData.Write(BitConverter.GetBytes((ushort)0)); // Unknown

        foreach (var data in dataArray[1..])
        {
            moreUserData.Write(WriteIcqString(data));
        }

        // Country Code (Telephone Style)
        moreUserData.Write(BitConverter.GetBytes((ushort)0x01));

        moreUserData.WriteByte(0x00);

        await SendMetaData(session, snac, icqMetaReq, META_MORE_USERINFO, moreUserData, false);
    }

    private async Task SendEmailUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var emailUserData = new MemoryStream();

        emailUserData.WriteByte(0x0A); // Success Byte

        emailUserData.WriteByte(0x00); // No extra emails, haha, no... TODO: support multiple email addresses

        await SendMetaData(session, snac, icqMetaReq, META_EMAIL_USERINFO, emailUserData, false);
    }

    private async Task SendHomepageCatUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var homepageCatUserData = new MemoryStream();

        homepageCatUserData.WriteByte(0x0A); // Success Byte

        homepageCatUserData.WriteByte(0x00); // No homepage categories, haha, no... TODO: support whatever the fuck this is?

        await SendMetaData(session, snac, icqMetaReq, META_HPAGECAT_USERINFO, homepageCatUserData, false);
    }

    private async Task SendWorkUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var workUserData = new MemoryStream();

        workUserData.WriteByte(0x0A); // Success Byte

        var dataArray = new string[]
        {
            FakeUser.WorkCity,
            FakeUser.WorkState,
            FakeUser.WorkPhone,
            FakeUser.WorkFax,
            FakeUser.WorkAddress,
            FakeUser.WorkZip,
            FakeUser.WorkCompany,
            FakeUser.WorkDepartment,
            FakeUser.WorkPosition,
            FakeUser.WorkHomepage
        };

        foreach (var data in dataArray[..6])
        {
            workUserData.Write(WriteIcqString(data));
        }

        // Country Code (Telephone Style)
        workUserData.Write(BitConverter.GetBytes((ushort)1));

        foreach (var data in dataArray[6..9])
        {
            workUserData.Write(WriteIcqString(data));
        }

        workUserData.Write(BitConverter.GetBytes((ushort)0x05)); // Occupation: Computers

        workUserData.Write(WriteIcqString(dataArray[9]));

        await SendMetaData(session, snac, icqMetaReq, META_WORK_USERINFO, workUserData, false);
    }

    private async Task SendNotesUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var workUserData = new MemoryStream();

        workUserData.WriteByte(0x0A); // Success Byte

        workUserData.Write(WriteIcqString(FakeUser.Notes));      

        await SendMetaData(session, snac, icqMetaReq, META_NOTES_USERINFO, workUserData, false);
    }

    private async Task SendInterestsUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var interestsUserData = new MemoryStream();

        interestsUserData.WriteByte(0x0A); // Success Byte

        interestsUserData.WriteByte(0x04);

        interestsUserData.Write(BitConverter.GetBytes((ushort)0x64));
        interestsUserData.Write(WriteIcqString("interest1_key"));

        interestsUserData.Write(BitConverter.GetBytes((ushort)0x67));
        interestsUserData.Write(WriteIcqString("interest2_key"));

        interestsUserData.Write(BitConverter.GetBytes((ushort)0x68));
        interestsUserData.Write(WriteIcqString("interest3_key"));

        interestsUserData.Write(BitConverter.GetBytes((ushort)0x6F));
        interestsUserData.Write(WriteIcqString("interest4_key"));

        await SendMetaData(session, snac, icqMetaReq, META_INTERESTS_USERINFO, interestsUserData, false);
    }

    private async Task SendAffiliationUserInfo(OscarSession session, Snac snac, IcqUserMetaRequest icqMetaReq)
    {
        var affiliationUserData = new MemoryStream();

        affiliationUserData.WriteByte(0x0A); // Success Byte

        affiliationUserData.WriteByte(0x03);

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0x012E));
        affiliationUserData.Write(WriteIcqString("aff1 keyword"));

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0x012C));
        affiliationUserData.Write(WriteIcqString("aff2 keyword"));

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0x012D));
        affiliationUserData.Write(WriteIcqString("aff3 keyword"));

        affiliationUserData.WriteByte(0x03);

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0xC8));
        affiliationUserData.Write(WriteIcqString("past1keyword"));

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0xCA));
        affiliationUserData.Write(WriteIcqString("past2keyword"));

        affiliationUserData.Write(BitConverter.GetBytes((ushort)0xCB));
        affiliationUserData.Write(WriteIcqString("past3keyword"));

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
        icqString.WriteByte(0x00); // NUL Terminated String!?

        return icqString.ToArray();
    }
}
