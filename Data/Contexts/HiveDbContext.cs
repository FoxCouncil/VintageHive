// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;
using System.Data;
using System.Dynamic;
using VintageHive.Network;
using VintageHive.Proxy.Oscar;

namespace VintageHive.Data.Contexts;

public class HiveDbContext : DbContextBase
{
    private const string TABLE_CONFIG = "config";
    private const string TABLE_CONFIGLOCAL = "config_local";

    private const string TABLE_CERTS = "certs";

    private const string TABLE_REQUESTS = "requests";
    private const string TABLE_LOGS = "logs";

    private const string TABLE_LINKS = "links";
    private const string TABLE_LINKSLOCAL = "links_local";

    private const string TABLE_DOWNLOADS = "downloads";

    private const string TABLE_WEBSESSION = "websession";

    private const string TABLE_VQD = "vqd";

    private const string TABLE_USER = "user";

    private const string TABLE_MAIL = "mail";

    private const string TABLE_USENET = "usenet";

    private const string TABLE_OSCARSESSION = "oscar_session";

    private const string TABLE_OSCARPROFILE = "oscar_profile";

    private const string TABLE_OSCAROFFLINEMSG = "oscar_offline_msg";

    private const string TABLE_OSCARSSI = "oscar_ssi";

    static readonly IReadOnlyDictionary<string, object> kDefaultGlobalSettings = new Dictionary<string, object>()
    {
        // Networking Settings
        { ConfigNames.IpAddress, IPAddress.Any.ToString() },
        { ConfigNames.PortHttp, 1990 },
        { ConfigNames.PortHttps, 9999 },
        { ConfigNames.PortFtp, 1971 },
        { ConfigNames.PortTelnet, 1969 },
        { ConfigNames.PortSocks5, 1996 },
        { ConfigNames.PortSmtp, 1980 },
        { ConfigNames.PortPop3, 1984 },
        { ConfigNames.PortUsenet, 1986 },
        { ConfigNames.PortIrc, 1988 },
        { ConfigNames.PortImap, 1985 },
        { ConfigNames.PortIpp, 631 },
        { ConfigNames.PortLpd, 515 },
        { ConfigNames.PortRawPrint, 9100 },
        { ConfigNames.PortDns, 1953 },
        { ConfigNames.PortIls, 1002 },
        { ConfigNames.PortRas, 1719 },
        { ConfigNames.PortH323, 1720 },
        { ConfigNames.PortT120, 1503 },

        // System Display Settings
        { ConfigNames.TemperatureUnits, WeatherUtils.TemperatureUnits.Celsius },
        { ConfigNames.DistanceUnits, WeatherUtils.DistanceUnits.Metric },

        // System Services Settings
        { ConfigNames.ServiceIntranet, true },
        { ConfigNames.ServiceDialnine, true },
        { ConfigNames.ServiceProtoWeb, true },
        { ConfigNames.ServiceInternetArchive, true },
        { ConfigNames.ServiceInternetArchiveYear, 1999 },
        { ConfigNames.ServiceSmtp, true },
        { ConfigNames.ServicePop3, true },
        { ConfigNames.ServiceUsenet, true },
        { ConfigNames.ServiceIrc, true },
        { ConfigNames.ServiceImap, true },
        { ConfigNames.ServicePrinter, true },
        { ConfigNames.ServiceDns, true },
        { ConfigNames.ServiceIls, true },
        { ConfigNames.ServiceRas, true },
        { ConfigNames.ServiceH323, true },
        { ConfigNames.ServiceT120, true },
    };

    static readonly IReadOnlyDictionary<string, string> kDefaultLinks = new Dictionary<string, string>()
    {
        { "Cool Links", "http://www.web-search.com/cool.html" },
        { "GatewayToTheNet.com", "http://www.gatewaytothenet.com/" },
        { "Top10Links", "http://www.toptenlinks.com/" },
        { "House Of Links", "http://www.ozemail.com.au/~krisp/button.html" },
        { "The BIG EYE", "http://www.bigeye.com/" },
        { "STARTING PAGE", "http://www.startingpage.com/" },
        { "Hotsheet.com", "http://www.hotsheet.com/" },
        { "Nerd World Media", "http://www.nerdworld.com/" },
        { "Suite101.com", "http://www.suite101.com" },
        { "RefDesk.com", "http://www.refdesk.com/" },
        { "WWW Virtual Library", "http://vlib.org/" },
        { "Yahoo!", "http://www.yahoo.com" },
        { "Yahoo! Canada", "http://www.yahoo.ca" },
        { "DogPile Open Directory", "http://opendir.dogpile.com/" }
    };

    public HiveDbContext() : base()
    {
        // Configuration
        CreateTable(TABLE_CONFIG, "key TEXT UNIQUE, value BLOB");
        CreateTable(TABLE_CONFIGLOCAL, "address text, key TEXT, value BLOB");

        // For Tracking Purposes
        CreateTable(TABLE_REQUESTS, "timestamp TEXT, address TEXT, localaddress TEXT, useragent TEXT, type TEXT, request TEXT, processor TEXT, traceid TEXT");
        CreateTable(TABLE_LOGS, "timestamp TEXT, level TEXT, sys TEXT, msg TEXT, traceid TEXT");

        // Certificate Authority
        CreateTable(TABLE_CERTS, "name TEXT UNIQUE, cert TEXT, key TEXT");

        // Global Top Links
        CreateTable(TABLE_LINKS, "name TEXT UNIQUE, link TEXT");
        CreateTable(TABLE_LINKSLOCAL, "address text, name TEXT UNIQUE, link TEXT");

        if (IsNewDb)
        {
            foreach (var link in kDefaultLinks)
            {
                LinksAdd(link.Key, link.Value);
            }
        }

        // Download Locations
        CreateTable(TABLE_DOWNLOADS, "name TEXT, location TEXT");

        // Webserver Sessions
        CreateTable(TABLE_WEBSESSION, "key TEXT UNIQUE, ttl TEXT, ip TEXT, ua TEXT, value BLOB");

        // For Search Feature
        CreateTable(TABLE_VQD, "key TEXT UNIQUE, vqd TEXT");

        // User Accounts
        CreateTable(TABLE_USER, "username TEXT UNIQUE COLLATE NOCASE, password TEXT");

        // ICQ (OSCAR) Server
        CreateTable(TABLE_OSCARSESSION, "cookie TEXT UNIQUE, screenname TEXT, status TEXT, awaymime TEXT, away TEXT, profilemime TEXT, profile TEXT, buddies TEXT, capabilities TEXT, useragent TEXT, clientip TEXT, timestamp TEXT");

        // Oscar User Profiles
        CreateTable(TABLE_OSCARPROFILE, "screenname TEXT UNIQUE COLLATE NOCASE, nickname TEXT, firstname TEXT, lastname TEXT, email TEXT, homecity TEXT, homestate TEXT, homephone TEXT, homefax TEXT, homeaddress TEXT, cellphone TEXT, homezip TEXT, homecountry INTEGER, age INTEGER, gender INTEGER, homepage TEXT, birthyear INTEGER, birthmonth INTEGER, birthday INTEGER, language1 INTEGER, language2 INTEGER, language3 INTEGER, workcity TEXT, workstate TEXT, workphone TEXT, workfax TEXT, workaddress TEXT, workzip TEXT, workcountry INTEGER, workcompany TEXT, workdepartment TEXT, workposition TEXT, workhomepage TEXT, workoccupation INTEGER, notes TEXT, interests TEXT, affiliations TEXT, pastaffiliations TEXT");

        // Oscar Offline Messages
        CreateTable(TABLE_OSCAROFFLINEMSG, "id INTEGER PRIMARY KEY AUTOINCREMENT, fromscreenname TEXT, toscreenname TEXT, channel INTEGER, messagedata BLOB, timestamp TEXT");

        // Oscar SSI (Server-Side Information)
        CreateTable(TABLE_OSCARSSI, "screenname TEXT COLLATE NOCASE, name TEXT, groupid INTEGER, itemid INTEGER, itemtype INTEGER, tlvdata BLOB");
    }

    #region Log Methods

    public void WriteLog(LogItem log)
    {
        WithContext(context =>
        {
            using var insertCommand = context.CreateCommand();

            insertCommand.CommandText = $"INSERT INTO {TABLE_LOGS} VALUES(@ts, @level, @sys, @msg, @traceid)";

            insertCommand.Parameters.Add(new SqliteParameter("@ts", log.Timestamp));
            insertCommand.Parameters.Add(new SqliteParameter("@level", log.Level ?? string.Empty));
            insertCommand.Parameters.Add(new SqliteParameter("@sys", log.System ?? string.Empty));
            insertCommand.Parameters.Add(new SqliteParameter("@msg", log.Message ?? string.Empty));
            insertCommand.Parameters.Add(new SqliteParameter("@traceid", log.TraceId ?? string.Empty));

            insertCommand.ExecuteNonQuery();
        });
    }

    public List<LogItem> GetLogItems(int page = 1, int pageSize = 100)
    {
        return WithContext<List<LogItem>>(context =>
        {
            var list = new List<LogItem>();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_LOGS} ORDER BY timestamp DESC LIMIT @limit OFFSET @offset";

            command.Parameters.Add(new SqliteParameter("@limit", pageSize));
            command.Parameters.Add(new SqliteParameter("@offset", (page - 1) * pageSize));

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new LogItem
                {
                    Timestamp = DateTimeOffset.Parse(reader.GetString(0)),
                    Level = reader.GetString(1),
                    System = reader.GetString(2),
                    Message = reader.GetString(3)
                });
            }

            return list;
        });
    }

    #endregion

    #region Config Methods

    public T ConfigLocalGet<T>(string address, string key)
    {
        return WithContext<T>(context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT value FROM {TABLE_CONFIGLOCAL} WHERE address = @address AND key = @key";

            command.Parameters.Add(new SqliteParameter("@address", address));
            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return ConfigGet<T>(key);
            }

            if (typeof(T) == typeof(int))
            {
                return (T)(object)reader.GetInt32(0);
            }

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)reader.GetBoolean(0);
            }

            return (T)reader.GetValue(0);
        });
    }

    public void ConfigLocalSet<T>(string address, string key, T value)
    {
        WithContext(context =>
        {
            key = key.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            if (value == null)
            {
                using var deleteCommand = context.CreateCommand();

                deleteCommand.CommandText = $"DELETE FROM {TABLE_CONFIGLOCAL} WHERE address = @address AND key = @key";

                deleteCommand.Parameters.Add(new SqliteParameter("@address", address));
                deleteCommand.Parameters.Add(new SqliteParameter("@key", key));

                deleteCommand.ExecuteNonQuery();
            }
            else
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_CONFIGLOCAL} SET value = @value WHERE address = @address AND key = @key";

                updateCommand.Parameters.Add(new SqliteParameter("@value", value));
                updateCommand.Parameters.Add(new SqliteParameter("@address", address));
                updateCommand.Parameters.Add(new SqliteParameter("@key", key));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_CONFIGLOCAL} (address, key, value) VALUES(@address, @key, @value)";

                    insertCommand.Parameters.Add(new SqliteParameter("@address", address));
                    insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                    insertCommand.Parameters.Add(new SqliteParameter("@value", value));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }

    public T ConfigGet<T>(string key)
    {
        return WithContext<T>(context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT value FROM {TABLE_CONFIG} WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                if (kDefaultGlobalSettings.ContainsKey(key))
                {
                    // Todo: Make better err handling
                    var val = (T)kDefaultGlobalSettings[key];

                    ConfigSet(key, val);

                    return val;
                }

                return default;
            }

            if (typeof(T) == typeof(int))
            {
                return (T)(object)reader.GetInt32(0);
            }

            if (typeof(T) == typeof(bool))
            {
                return (T)(object)reader.GetBoolean(0);
            }

            if (typeof(T).FullName.Contains("Data.Types"))
            {
                return JsonSerializer.Deserialize<T>(reader.GetString(0));
            }

            return (T)reader.GetValue(0);
        });
    }

    public void ConfigSet<T>(string key, T value)
    {
        WithContext(context =>
        {
            var isJson = false;

            if (typeof(T).FullName.Contains("VintageHive.Data.Types"))
            {
                isJson = true;
            }

            key = key.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            if (value == null)
            {
                using var deleteCommand = context.CreateCommand();

                deleteCommand.CommandText = $"DELETE FROM {TABLE_CONFIG} WHERE key = @key";

                deleteCommand.Parameters.Add(new SqliteParameter("@key", key));

                deleteCommand.ExecuteNonQuery();
            }
            else
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_CONFIG} SET value = @value WHERE key = @key";

                updateCommand.Parameters.Add(new SqliteParameter("@key", key));
                updateCommand.Parameters.Add(new SqliteParameter("@value", isJson ? JsonSerializer.Serialize(value) : value));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_CONFIG} (key, value) VALUES(@key, @value)";

                    insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                    insertCommand.Parameters.Add(new SqliteParameter("@value", isJson ? JsonSerializer.Serialize(value) : value));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }
    #endregion

    #region Cert Methods

    public SslCertificate CertGet(string name)
    {
        return WithContext<SslCertificate>(context =>
        {
            name = name.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT cert, key FROM {TABLE_CERTS} WHERE name = @name";

            command.Parameters.Add(new SqliteParameter("@name", name));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            return new SslCertificate(reader.GetString(0), reader.GetString(1));
        });
    }

    public void CertSet(string name, SslCertificate cert)
    {
        WithContext(context =>
        {
            name = name.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            if (cert == null)
            {
                using var deleteCommand = context.CreateCommand();

                deleteCommand.CommandText = $"DELETE FROM {TABLE_CERTS} WHERE name = @name";

                deleteCommand.Parameters.Add(new SqliteParameter("@name", name));

                deleteCommand.ExecuteNonQuery();
            }
            else
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_CERTS} SET cert = @cert, key = @key WHERE name = @name";

                updateCommand.Parameters.Add(new SqliteParameter("@cert", cert.Certificate));
                updateCommand.Parameters.Add(new SqliteParameter("@key", cert.Key));
                updateCommand.Parameters.Add(new SqliteParameter("@name", name));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_CERTS} (name, cert, key) VALUES(@name, @cert, @key)";

                    insertCommand.Parameters.Add(new SqliteParameter("@name", name));
                    insertCommand.Parameters.Add(new SqliteParameter("@cert", cert.Certificate));
                    insertCommand.Parameters.Add(new SqliteParameter("@key", cert.Key));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }

    #endregion

    #region Request Methods
    public void RequestsTrack(ListenerSocket socket, string useragent, string type, string requestUrl, string processor)
    {
        if (requestUrl.StartsWith("http://web.archive.org/web/"))
        {
            requestUrl = requestUrl[(requestUrl.IndexOf('_') + 2)..];
        }

        WithContext(context =>
        {
            using var insertCommand = context.CreateCommand();

            insertCommand.CommandText = $"INSERT INTO {TABLE_REQUESTS} (timestamp, address, localaddress, useragent, type, request, processor, traceid) VALUES(@timestamp, @address, @localaddress, @useragent, @type, @request, @processor, @traceid)";

            insertCommand.Parameters.Add(new SqliteParameter("@timestamp", DateTime.UtcNow));
            insertCommand.Parameters.Add(new SqliteParameter("@address", socket.RemoteIP));
            insertCommand.Parameters.Add(new SqliteParameter("@localaddress", socket.LocalAddress));
            insertCommand.Parameters.Add(new SqliteParameter("@useragent", useragent));
            insertCommand.Parameters.Add(new SqliteParameter("@type", type));
            insertCommand.Parameters.Add(new SqliteParameter("@request", requestUrl));
            insertCommand.Parameters.Add(new SqliteParameter("@processor", processor));
            insertCommand.Parameters.Add(new SqliteParameter("@traceid", socket.TraceId));
            insertCommand.ExecuteNonQuery();
        });

        Log.WriteLine(Log.LEVEL_REQUEST, processor, requestUrl, socket.TraceId.ToString());
    }
    #endregion

    #region Link Methods

    public void LinksAdd(string name, string link)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            if (link == null)
            {
                using var deleteCommand = context.CreateCommand();

                deleteCommand.CommandText = $"DELETE FROM {TABLE_LINKS} WHERE name = @name";

                deleteCommand.Parameters.Add(new SqliteParameter("@name", name));

                deleteCommand.ExecuteNonQuery();
            }
            else
            {
                using var updateCommand = context.CreateCommand();

                updateCommand.CommandText = $"UPDATE {TABLE_LINKS} SET link = @link WHERE name = @name";

                updateCommand.Parameters.Add(new SqliteParameter("@name", name));
                updateCommand.Parameters.Add(new SqliteParameter("@link", link));

                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    using var insertCommand = context.CreateCommand();

                    insertCommand.CommandText = $"INSERT INTO {TABLE_LINKS} (name, link) VALUES(@name, @link)";

                    insertCommand.Parameters.Add(new SqliteParameter("@name", name));
                    insertCommand.Parameters.Add(new SqliteParameter("@link", link));

                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        });
    }

    public Dictionary<string, string> LinksGetAll()
    {
        return WithContext<Dictionary<string, string>>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT name, link FROM {TABLE_LINKS} ORDER BY name";

            using var reader = command.ExecuteReader();

            var links = new Dictionary<string, string>();

            while (reader.Read())
            {
                links.Add(reader.GetString(0), reader.GetString(1));
            }

            return links;
        });
    }
    #endregion

    #region Downloads Methods
    #endregion

    #region Websession Methods
    public dynamic WebSessionGet(Guid sessionId)
    {
        return WithContext<dynamic>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT value FROM {TABLE_WEBSESSION} WHERE key = @key AND ip = @ip AND ua = @ua";

            command.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
            command.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
            command.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return new ExpandoObject();
            }

            var jsonString = reader.GetString(0);

            return JsonSerializer.Deserialize<ExpandoObject>(jsonString);
        });
    }

    public void WebSessionSet(Guid sessionId, dynamic data)
    {
        WithContext(context =>
        {
            var jsonString = JsonSerializer.Serialize(data);

            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_WEBSESSION} SET value = @value WHERE key = @key AND ip = @ip AND ua = @ua";

            updateCommand.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));
            updateCommand.Parameters.Add(new SqliteParameter("@value", jsonString));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {TABLE_WEBSESSION} (key, ttl, ip, ua, value) VALUES(@key, datetime(), @ip, @ua, @value)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@ip", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@ua", sessionId.ToString()));
                insertCommand.Parameters.Add(new SqliteParameter("@value", jsonString));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }
    #endregion

    #region User Methods

    public bool UserCreate(string username, string password)
    {
        if (username == null || password == null)
        { 
            return false; 
        }

        if (username.Length is < 3 or > 8 || password.Length is < 3 or > 8)
        {
            return false;
        }

        if (UserExistsByUsername(username))
        {
            return false;
        }

        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"INSERT INTO {TABLE_USER} (username, password) VALUES(@username, @password)";

            command.Parameters.Add(new SqliteParameter("@username", username));
            command.Parameters.Add(new SqliteParameter("@password", password));

            return Convert.ToBoolean(command.ExecuteNonQuery());
        });
    }

    public bool UserDelete(string username)
    {
        if (!UserExistsByUsername(username))
        {
            return false;
        }

        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"DELETE FROM {TABLE_USER} WHERE username = @username";

            command.Parameters.Add(new SqliteParameter("@username", username));

            return Convert.ToBoolean(command.ExecuteNonQuery());
        });
    }

    public bool UserExistsByUsername(string username)
    {
        return WithContext(context =>
        {
            username = username.ToLower();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT username FROM {TABLE_USER} WHERE username = @username";

            command.Parameters.Add(new SqliteParameter("@username", username));

            using var reader = command.ExecuteReader();

            return reader.Read();
        });
    }

    public HiveUser UserFetch(string username, string password = "")
    {
        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_USER} WHERE username = @username";

            command.Parameters.Add(new SqliteParameter("@username", username));

            if (password != string.Empty)
            {
                command.CommandText += " AND password = @password";

                command.Parameters.Add(new SqliteParameter("@password", password));
            }

            using var reader = command.ExecuteReader();

            if (reader.Read())
            {
                return HiveUser.ParseSQL(reader);
            }
            else
            {
                return null;
            }
        });
    }

    public List<HiveUser> UserList()
    {
        return WithContext(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_USER}";

            using var reader = command.ExecuteReader();

            var users = new List<HiveUser>();

            while (reader.Read())
            {
                users.Add(HiveUser.ParseSQL(reader));
            }

            return users;
        });
    }

    #endregion

    #region Vpd Methods
    public string VqdGet(string key)
    {
        return WithContext<string>(context =>
        {
            key = key.ToLowerInvariant();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT vqd FROM {TABLE_VQD} WHERE key = @key";

            command.Parameters.Add(new SqliteParameter("@key", key));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return reader.GetString(0);
        });
    }

    public void VqdSet(string key, string vqd)
    {
        WithContext(context =>
        {
            key = key.ToLowerInvariant();

            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_VQD} SET vqd = @vqd WHERE key = @key";

            updateCommand.Parameters.Add(new SqliteParameter("@key", key));
            updateCommand.Parameters.Add(new SqliteParameter("@vqd", vqd));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {TABLE_VQD} (key, vqd) VALUES(@key, @vqd)";

                insertCommand.Parameters.Add(new SqliteParameter("@key", key));
                insertCommand.Parameters.Add(new SqliteParameter("@vqd", vqd));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }
    #endregion

    #region Oscar Profile Methods
    public OscarUserProfile OscarGetProfile(string screenName)
    {
        return WithContext<OscarUserProfile>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_OSCARPROFILE} WHERE screenname = @screenname";

            command.Parameters.Add(new SqliteParameter("@screenname", screenName));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            return new OscarUserProfile(reader);
        });
    }

    public void OscarInsertOrUpdateProfile(OscarUserProfile profile)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_OSCARPROFILE} SET nickname = @nickname, firstname = @firstname, lastname = @lastname, email = @email, homecity = @homecity, homestate = @homestate, homephone = @homephone, homefax = @homefax, homeaddress = @homeaddress, cellphone = @cellphone, homezip = @homezip, homecountry = @homecountry, age = @age, gender = @gender, homepage = @homepage, birthyear = @birthyear, birthmonth = @birthmonth, birthday = @birthday, language1 = @language1, language2 = @language2, language3 = @language3, workcity = @workcity, workstate = @workstate, workphone = @workphone, workfax = @workfax, workaddress = @workaddress, workzip = @workzip, workcountry = @workcountry, workcompany = @workcompany, workdepartment = @workdepartment, workposition = @workposition, workhomepage = @workhomepage, workoccupation = @workoccupation, notes = @notes, interests = @interests, affiliations = @affiliations, pastaffiliations = @pastaffiliations WHERE screenname = @screenname";

            AddProfileParameters(updateCommand, profile);

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {TABLE_OSCARPROFILE} (screenname, nickname, firstname, lastname, email, homecity, homestate, homephone, homefax, homeaddress, cellphone, homezip, homecountry, age, gender, homepage, birthyear, birthmonth, birthday, language1, language2, language3, workcity, workstate, workphone, workfax, workaddress, workzip, workcountry, workcompany, workdepartment, workposition, workhomepage, workoccupation, notes, interests, affiliations, pastaffiliations) VALUES (@screenname, @nickname, @firstname, @lastname, @email, @homecity, @homestate, @homephone, @homefax, @homeaddress, @cellphone, @homezip, @homecountry, @age, @gender, @homepage, @birthyear, @birthmonth, @birthday, @language1, @language2, @language3, @workcity, @workstate, @workphone, @workfax, @workaddress, @workzip, @workcountry, @workcompany, @workdepartment, @workposition, @workhomepage, @workoccupation, @notes, @interests, @affiliations, @pastaffiliations)";

                AddProfileParameters(insertCommand, profile);

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    private static void AddProfileParameters(IDbCommand command, OscarUserProfile profile)
    {
        command.Parameters.Add(new SqliteParameter("@screenname", profile.ScreenName));
        command.Parameters.Add(new SqliteParameter("@nickname", profile.Nickname ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@firstname", profile.FirstName ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@lastname", profile.LastName ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@email", profile.Email ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homecity", profile.HomeCity ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homestate", profile.HomeState ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homephone", profile.HomePhone ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homefax", profile.HomeFax ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homeaddress", profile.HomeAddress ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@cellphone", profile.CellPhone ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homezip", profile.HomeZip ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@homecountry", (int)profile.HomeCountry));
        command.Parameters.Add(new SqliteParameter("@age", (int)profile.Age));
        command.Parameters.Add(new SqliteParameter("@gender", (int)profile.Gender));
        command.Parameters.Add(new SqliteParameter("@homepage", profile.Homepage ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@birthyear", (int)profile.BirthYear));
        command.Parameters.Add(new SqliteParameter("@birthmonth", (int)profile.BirthMonth));
        command.Parameters.Add(new SqliteParameter("@birthday", (int)profile.BirthDay));
        command.Parameters.Add(new SqliteParameter("@language1", (int)profile.Language1));
        command.Parameters.Add(new SqliteParameter("@language2", (int)profile.Language2));
        command.Parameters.Add(new SqliteParameter("@language3", (int)profile.Language3));
        command.Parameters.Add(new SqliteParameter("@workcity", profile.WorkCity ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workstate", profile.WorkState ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workphone", profile.WorkPhone ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workfax", profile.WorkFax ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workaddress", profile.WorkAddress ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workzip", profile.WorkZip ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workcountry", (int)profile.WorkCountry));
        command.Parameters.Add(new SqliteParameter("@workcompany", profile.WorkCompany ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workdepartment", profile.WorkDepartment ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workposition", profile.WorkPosition ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workhomepage", profile.WorkHomepage ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@workoccupation", (int)profile.WorkOccupation));
        command.Parameters.Add(new SqliteParameter("@notes", profile.Notes ?? string.Empty));
        command.Parameters.Add(new SqliteParameter("@interests", profile.InterestsJson ?? "[]"));
        command.Parameters.Add(new SqliteParameter("@affiliations", profile.AffiliationsJson ?? "[]"));
        command.Parameters.Add(new SqliteParameter("@pastaffiliations", profile.PastAffiliationsJson ?? "[]"));
    }

    public void OscarEnsureProfileExists(string screenName)
    {
        var existing = OscarGetProfile(screenName);

        if (existing == null)
        {
            var profile = new OscarUserProfile
            {
                ScreenName = screenName,
                Nickname = screenName,
                Email = $"{screenName}@hive.com"
            };

            OscarInsertOrUpdateProfile(profile);
        }
    }
    #endregion

    #region Oscar Offline Message Methods
    public void OscarStoreOfflineMessage(string from, string to, ushort channel, byte[] messageData)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"INSERT INTO {TABLE_OSCAROFFLINEMSG} (fromscreenname, toscreenname, channel, messagedata, timestamp) VALUES (@from, @to, @channel, @data, datetime('now'))";

            command.Parameters.Add(new SqliteParameter("@from", from));
            command.Parameters.Add(new SqliteParameter("@to", to));
            command.Parameters.Add(new SqliteParameter("@channel", (int)channel));
            command.Parameters.Add(new SqliteParameter("@data", messageData));

            command.ExecuteNonQuery();
        });
    }

    public List<OscarOfflineMessage> OscarGetOfflineMessages(string screenName)
    {
        return WithContext<List<OscarOfflineMessage>>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_OSCAROFFLINEMSG} WHERE toscreenname = @to ORDER BY timestamp ASC";

            command.Parameters.Add(new SqliteParameter("@to", screenName));

            using var reader = command.ExecuteReader();

            var messages = new List<OscarOfflineMessage>();

            while (reader.Read())
            {
                messages.Add(new OscarOfflineMessage(reader));
            }

            return messages;
        });
    }

    public void OscarDeleteOfflineMessages(string screenName)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"DELETE FROM {TABLE_OSCAROFFLINEMSG} WHERE toscreenname = @to";

            command.Parameters.Add(new SqliteParameter("@to", screenName));

            command.ExecuteNonQuery();
        });
    }
    #endregion

    #region Oscar SSI Methods
    public List<OscarSsiItem> OscarGetSsiItems(string screenName)
    {
        return WithContext<List<OscarSsiItem>>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_OSCARSSI} WHERE screenname = @screenname ORDER BY groupid, itemid";

            command.Parameters.Add(new SqliteParameter("@screenname", screenName));

            using var reader = command.ExecuteReader();

            var items = new List<OscarSsiItem>();

            while (reader.Read())
            {
                items.Add(new OscarSsiItem(reader));
            }

            return items;
        });
    }

    public void OscarSsiAddItem(OscarSsiItem item)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"INSERT INTO {TABLE_OSCARSSI} (screenname, name, groupid, itemid, itemtype, tlvdata) VALUES (@screenname, @name, @groupid, @itemid, @itemtype, @tlvdata)";

            command.Parameters.Add(new SqliteParameter("@screenname", item.ScreenName));
            command.Parameters.Add(new SqliteParameter("@name", item.Name ?? string.Empty));
            command.Parameters.Add(new SqliteParameter("@groupid", (int)item.GroupId));
            command.Parameters.Add(new SqliteParameter("@itemid", (int)item.ItemId));
            command.Parameters.Add(new SqliteParameter("@itemtype", (int)item.ItemType));
            command.Parameters.Add(new SqliteParameter("@tlvdata", item.TlvData));

            command.ExecuteNonQuery();
        });
    }

    public void OscarSsiUpdateItem(OscarSsiItem item)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"UPDATE {TABLE_OSCARSSI} SET name = @name, tlvdata = @tlvdata WHERE screenname = @screenname AND groupid = @groupid AND itemid = @itemid AND itemtype = @itemtype";

            command.Parameters.Add(new SqliteParameter("@screenname", item.ScreenName));
            command.Parameters.Add(new SqliteParameter("@name", item.Name ?? string.Empty));
            command.Parameters.Add(new SqliteParameter("@groupid", (int)item.GroupId));
            command.Parameters.Add(new SqliteParameter("@itemid", (int)item.ItemId));
            command.Parameters.Add(new SqliteParameter("@itemtype", (int)item.ItemType));
            command.Parameters.Add(new SqliteParameter("@tlvdata", item.TlvData));

            command.ExecuteNonQuery();
        });
    }

    public void OscarSsiDeleteItem(string screenName, ushort groupId, ushort itemId, ushort itemType)
    {
        WithContext(context =>
        {
            using var command = context.CreateCommand();

            command.CommandText = $"DELETE FROM {TABLE_OSCARSSI} WHERE screenname = @screenname AND groupid = @groupid AND itemid = @itemid AND itemtype = @itemtype";

            command.Parameters.Add(new SqliteParameter("@screenname", screenName));
            command.Parameters.Add(new SqliteParameter("@groupid", (int)groupId));
            command.Parameters.Add(new SqliteParameter("@itemid", (int)itemId));
            command.Parameters.Add(new SqliteParameter("@itemtype", (int)itemType));

            command.ExecuteNonQuery();
        });
    }
    #endregion

    #region Oscar Methods
    public OscarSession OscarGetSessionByCookie(string cookie)
    {
        return WithContext<OscarSession>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_OSCARSESSION} WHERE cookie = @cookie";

            command.Parameters.Add(new SqliteParameter("@cookie", cookie));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            return new OscarSession(reader);
        });
    }
    // cookie TEXT UNIQUE, screenname TEXT, status TEXT, awaymime TEXT, away TEXT, profilemime TEXT, profile TEXT, buddies TEXT, capabilities TEXT, useragent TEXT, clientip TEXT, timestamp TEXT
    public void OscarInsertOrUpdateSession(OscarSession session)
    {
        WithContext(context =>
        {
            using var transaction = context.BeginTransaction();

            using var updateCommand = context.CreateCommand();

            updateCommand.CommandText = $"UPDATE {TABLE_OSCARSESSION} SET status = @status, awaymime = @awaymime, away = @away, profilemime = @profilemime, profile = @profile, buddies = @buddies, capabilities = @capabilities, useragent = @useragent, clientip = @clientip, timestamp = datetime('now') WHERE cookie = @cookie";

            updateCommand.Parameters.Add(new SqliteParameter("@status", session.Status));
            updateCommand.Parameters.Add(new SqliteParameter("@awaymime", session.AwayMessageMimeType));
            updateCommand.Parameters.Add(new SqliteParameter("@away", session.AwayMessage));
            updateCommand.Parameters.Add(new SqliteParameter("@profilemime", session.ProfileMimeType));
            updateCommand.Parameters.Add(new SqliteParameter("@profile", session.Profile));
            updateCommand.Parameters.Add(new SqliteParameter("@buddies", JsonSerializer.Serialize(session.Buddies)));
            updateCommand.Parameters.Add(new SqliteParameter("@capabilities", JsonSerializer.Serialize(session.Capabilities)));
            updateCommand.Parameters.Add(new SqliteParameter("@useragent", session.UserAgent));
            updateCommand.Parameters.Add(new SqliteParameter("@clientip", session.Client.RemoteIP));
            updateCommand.Parameters.Add(new SqliteParameter("@cookie", session.Cookie));

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                using var insertCommand = context.CreateCommand();

                insertCommand.CommandText = $"INSERT INTO {TABLE_OSCARSESSION} (cookie, screenname, status, awaymime, away, profilemime, profile, buddies, capabilities, useragent, clientip, timestamp) VALUES (@cookie, @screenname, @status, @awaymime, @away, @profilemime, @profile, @buddies, @capabilities, @useragent, @clientip, datetime('now'))";

                // SQLite parameters for INSERT command
                insertCommand.Parameters.Add(new SqliteParameter("@cookie", session.Cookie));
                insertCommand.Parameters.Add(new SqliteParameter("@screenname", session.ScreenName));
                insertCommand.Parameters.Add(new SqliteParameter("@status", session.Status));
                insertCommand.Parameters.Add(new SqliteParameter("@awaymime", session.AwayMessageMimeType));
                insertCommand.Parameters.Add(new SqliteParameter("@away", session.AwayMessage));
                insertCommand.Parameters.Add(new SqliteParameter("@profilemime", session.ProfileMimeType));
                insertCommand.Parameters.Add(new SqliteParameter("@profile", session.Profile));
                insertCommand.Parameters.Add(new SqliteParameter("@buddies", JsonSerializer.Serialize(session.Buddies)));
                insertCommand.Parameters.Add(new SqliteParameter("@capabilities", JsonSerializer.Serialize(session.Capabilities)));
                insertCommand.Parameters.Add(new SqliteParameter("@useragent", session.UserAgent));
                insertCommand.Parameters.Add(new SqliteParameter("@clientip", session.Client.RemoteIP));

                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    internal OscarSession OscarGetSessionByScreenameAndIp(string username, string remoteIpAddress)
    {
        return WithContext<OscarSession>(context =>
        {
            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_OSCARSESSION} WHERE screenname = @screenname AND clientip = @clientip";

            command.Parameters.Add(new SqliteParameter("@screenname", username));
            command.Parameters.Add(new SqliteParameter("@clientip", remoteIpAddress));

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return default;
            }

            return new OscarSession(reader);
        });
    }
    #endregion
}
