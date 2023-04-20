// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using VintageHive.Data.Types;

namespace VintageHive.Proxy.Security;

public static class CertificateAuthority
{
    public const string Name = "VintageHive Dialnine Cert Authority";

    const int KeySize = 512;

    const int ValidityPeriodYearsCA = 100;

    const int ValidityPeriodYearsCERT = 25;

    static X509Certificate2 _caCertificate;

    // private readonly string _caCertificatePem;

    // private readonly RSA _caPrivateKey;

    public static void Init()
    {
        if (_caCertificate != null)
        {
            throw new ApplicationException("No, no, no, don't run twice.");
        }

        var caCert = Mind.Db.CertGet(Name);
        
        if (caCert != null && !string.IsNullOrEmpty(caCert.Certificate) && !string.IsNullOrEmpty(caCert.Key))
        {
            //_caCertificatePem = File.ReadAllText(FILE_PEM_CA_CERT);
            //var keyPem = File.ReadAllText(FILE_PEM_CA_KEY);

            _caCertificate = X509Certificate2.CreateFromPem(caCert.Certificate, caCert.Key);

            //_caPrivateKey = RSA.Create();
            //_caPrivateKey.ImportFromPem(keyPem);
        }
        else
        {
            using var rsa = RSA.Create(KeySize);

            var request = new CertificateRequest($"CN={Name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, false));

            _caCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(ValidityPeriodYearsCA));

            var certPem = new string(PemEncoding.Write("CERTIFICATE", _caCertificate.RawData));
            var keyPem = new string(PemEncoding.Write("PRIVATE KEY", _caCertificate.GetRSAPrivateKey().ExportPkcs8PrivateKey()));

            caCert = new SslCertificate(certPem, keyPem);

            Mind.Db.CertSet(Name, caCert);
        }
    }

    public static SslCertificate GetOrCreateDomainCertificate(string domain)
    {
        var domainCert = Mind.Db.CertGet(domain);

        if (domainCert != null && !string.IsNullOrEmpty(domainCert.Certificate) && !string.IsNullOrEmpty(domainCert.Key))
        {
            return domainCert;
        }

        using var rsaKey = RSA.Create(KeySize);

        var request = new CertificateRequest($"CN={domain}", rsaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();

        sanBuilder.AddDnsName(domain);

        request.CertificateExtensions.Add(sanBuilder.Build());

        var serialNumber = GenerateSerialNumber();

        var domainCertificate = request.Create(_caCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(ValidityPeriodYearsCERT), serialNumber);

        var certPem = new string(PemEncoding.Write("CERTIFICATE", domainCertificate.RawData));
        var keyPem = new string(PemEncoding.Write("PRIVATE KEY", rsaKey.ExportPkcs8PrivateKey()));

        domainCert = new SslCertificate(certPem, keyPem);

        Mind.Db.CertSet(domain, domainCert);

        return domainCert;
    }

    private static byte[] GenerateSerialNumber()
    {
        using var rng = RandomNumberGenerator.Create();

        var serialNumber = new byte[12];

        rng.GetBytes(serialNumber);

        return serialNumber;
    }
}
