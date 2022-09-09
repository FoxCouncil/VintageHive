using Spectre.Console;
using System.Text;
using VintageHive;
using VintageHive.Utilities;




Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding.RegisterProvider(new MacEncodingProvider());

Encoding.GetEncoding("ISO-8859-1");

await Mind.Instance.Init();

Mind.Instance.Start();