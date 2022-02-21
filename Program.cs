using MonkeyCache;
using MonkeyCache.LiteDB;
using System.Text;
using VintageHive;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

BarrelUtils.SetBaseCachePath("./");

Barrel.ApplicationId = "VintageHiveCache";

var hiveMind = new Mind();

hiveMind.Start();