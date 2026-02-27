// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("autoconfig.hive.com")]
[Domain("autodiscover.hive.com")]
internal class AutodiscoverController : Controller
{
    [Route("/mail/config-v1.1.xml")]
    public async Task ThunderbirdAutoconfig()
    {
        await Task.Delay(0);

        var smtpPort = Mind.Db.ConfigGet<int>(ConfigNames.PortSmtp);
        var pop3Port = Mind.Db.ConfigGet<int>(ConfigNames.PortPop3);
        var imapPort = Mind.Db.ConfigGet<int>(ConfigNames.PortImap);

        var serverIp = Request.ListenerSocket.LocalIP;

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<clientConfig version=""1.1"">
  <emailProvider id=""hive.com"">
    <domain>hive.com</domain>
    <displayName>VintageHive Mail</displayName>
    <displayShortName>VintageHive</displayShortName>
    <incomingServer type=""imap"">
      <hostname>{serverIp}</hostname>
      <port>{imapPort}</port>
      <socketType>plain</socketType>
      <authentication>password-cleartext</authentication>
      <username>%EMAILLOCALPART%</username>
    </incomingServer>
    <incomingServer type=""pop3"">
      <hostname>{serverIp}</hostname>
      <port>{pop3Port}</port>
      <socketType>plain</socketType>
      <authentication>password-cleartext</authentication>
      <username>%EMAILLOCALPART%</username>
    </incomingServer>
    <outgoingServer type=""smtp"">
      <hostname>{serverIp}</hostname>
      <port>{smtpPort}</port>
      <socketType>plain</socketType>
      <authentication>password-cleartext</authentication>
      <username>%EMAILLOCALPART%</username>
    </outgoingServer>
  </emailProvider>
</clientConfig>";

        Response.SetBodyString(xml, "text/xml");
        Response.Handled = true;
    }

    [Route("/autodiscover/autodiscover.xml")]
    public async Task OutlookAutodiscover()
    {
        await Task.Delay(0);

        var smtpPort = Mind.Db.ConfigGet<int>(ConfigNames.PortSmtp);
        var pop3Port = Mind.Db.ConfigGet<int>(ConfigNames.PortPop3);
        var imapPort = Mind.Db.ConfigGet<int>(ConfigNames.PortImap);

        var serverIp = Request.ListenerSocket.LocalIP;

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Autodiscover xmlns=""http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006"">
  <Response xmlns=""http://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a"">
    <Account>
      <AccountType>email</AccountType>
      <Action>settings</Action>
      <Protocol>
        <Type>IMAP</Type>
        <Server>{serverIp}</Server>
        <Port>{imapPort}</Port>
        <LoginName>%EMAILLOCALPART%</LoginName>
        <SPA>off</SPA>
        <SSL>off</SSL>
        <AuthRequired>on</AuthRequired>
      </Protocol>
      <Protocol>
        <Type>POP3</Type>
        <Server>{serverIp}</Server>
        <Port>{pop3Port}</Port>
        <LoginName>%EMAILLOCALPART%</LoginName>
        <SPA>off</SPA>
        <SSL>off</SSL>
        <AuthRequired>on</AuthRequired>
      </Protocol>
      <Protocol>
        <Type>SMTP</Type>
        <Server>{serverIp}</Server>
        <Port>{smtpPort}</Port>
        <LoginName>%EMAILLOCALPART%</LoginName>
        <SPA>off</SPA>
        <SSL>off</SSL>
        <AuthRequired>on</AuthRequired>
      </Protocol>
    </Account>
  </Response>
</Autodiscover>";

        Response.SetBodyString(xml, "text/xml");
        Response.Handled = true;
    }
}
