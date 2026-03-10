// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Data;

namespace VintageHive.Proxy.Oscar;

public class OscarUserProfile
{
    public string ScreenName { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string HomeCity { get; set; } = string.Empty;

    public string HomeState { get; set; } = string.Empty;

    public string HomePhone { get; set; } = string.Empty;

    public string HomeFax { get; set; } = string.Empty;

    public string HomeAddress { get; set; } = string.Empty;

    public string CellPhone { get; set; } = string.Empty;

    public string HomeZip { get; set; } = string.Empty;

    public ushort HomeCountry { get; set; } = 1;

    public ushort Age { get; set; }

    public byte Gender { get; set; }

    public string Homepage { get; set; } = string.Empty;

    public ushort BirthYear { get; set; }

    public byte BirthMonth { get; set; }

    public byte BirthDay { get; set; }

    public byte Language1 { get; set; } = 12; // English

    public byte Language2 { get; set; }

    public byte Language3 { get; set; }

    public string WorkCity { get; set; } = string.Empty;

    public string WorkState { get; set; } = string.Empty;

    public string WorkPhone { get; set; } = string.Empty;

    public string WorkFax { get; set; } = string.Empty;

    public string WorkAddress { get; set; } = string.Empty;

    public string WorkZip { get; set; } = string.Empty;

    public ushort WorkCountry { get; set; } = 1;

    public string WorkCompany { get; set; } = string.Empty;

    public string WorkDepartment { get; set; } = string.Empty;

    public string WorkPosition { get; set; } = string.Empty;

    public string WorkHomepage { get; set; } = string.Empty;

    public ushort WorkOccupation { get; set; }

    public string Notes { get; set; } = string.Empty;

    public string InterestsJson { get; set; } = "[]";

    public string AffiliationsJson { get; set; } = "[]";

    public string PastAffiliationsJson { get; set; } = "[]";

    public OscarUserProfile() { }

    public OscarUserProfile(IDataReader reader)
    {
        ScreenName = reader.GetString(0);
        Nickname = reader.GetString(1);
        FirstName = reader.GetString(2);
        LastName = reader.GetString(3);
        Email = reader.GetString(4);
        HomeCity = reader.GetString(5);
        HomeState = reader.GetString(6);
        HomePhone = reader.GetString(7);
        HomeFax = reader.GetString(8);
        HomeAddress = reader.GetString(9);
        CellPhone = reader.GetString(10);
        HomeZip = reader.GetString(11);
        HomeCountry = (ushort)reader.GetInt32(12);
        Age = (ushort)reader.GetInt32(13);
        Gender = (byte)reader.GetInt32(14);
        Homepage = reader.GetString(15);
        BirthYear = (ushort)reader.GetInt32(16);
        BirthMonth = (byte)reader.GetInt32(17);
        BirthDay = (byte)reader.GetInt32(18);
        Language1 = (byte)reader.GetInt32(19);
        Language2 = (byte)reader.GetInt32(20);
        Language3 = (byte)reader.GetInt32(21);
        WorkCity = reader.GetString(22);
        WorkState = reader.GetString(23);
        WorkPhone = reader.GetString(24);
        WorkFax = reader.GetString(25);
        WorkAddress = reader.GetString(26);
        WorkZip = reader.GetString(27);
        WorkCountry = (ushort)reader.GetInt32(28);
        WorkCompany = reader.GetString(29);
        WorkDepartment = reader.GetString(30);
        WorkPosition = reader.GetString(31);
        WorkHomepage = reader.GetString(32);
        WorkOccupation = (ushort)reader.GetInt32(33);
        Notes = reader.GetString(34);
        InterestsJson = reader.GetString(35);
        AffiliationsJson = reader.GetString(36);
        PastAffiliationsJson = reader.GetString(37);
    }
}
