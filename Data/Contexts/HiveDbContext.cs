// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Microsoft.Data.Sqlite;
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
    }

    #region Log Methods

    public void WriteLog(LogItem log)
    {
        WithContext(context =>
        {
            using var insertCommand = context.CreateCommand();

            insertCommand.CommandText = $"INSERT INTO {TABLE_LOGS} VALUES(@ts, @level, @sys, @msg, @traceid)";

            insertCommand.Parameters.Add(new SqliteParameter("@ts", log.Timestamp));
            insertCommand.Parameters.Add(new SqliteParameter("@level", log.Level));
            insertCommand.Parameters.Add(new SqliteParameter("@sys", log.System));
            insertCommand.Parameters.Add(new SqliteParameter("@msg", log.Message));
            insertCommand.Parameters.Add(new SqliteParameter("@traceid", log.TraceId));

            insertCommand.ExecuteNonQuery();
        });
    }

    public List<LogItem> GetLogItems()
    {
        return WithContext<List<LogItem>>(context =>
        {
            var list = new List<LogItem>();

            var command = context.CreateCommand();

            command.CommandText = $"SELECT * FROM {TABLE_LOGS} ORDER BY timestamp DESC LIMIT 100"; // TODO: Pagination

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
