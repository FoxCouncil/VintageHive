using System.Net;
using System.Text;
using VintageHive;
using VintageHive.Utilities;

ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding.RegisterProvider(new MacEncodingProvider());

Encoding.GetEncoding("ISO-8859-1");

Mind.Instance.Init();

Mind.Instance.Start();