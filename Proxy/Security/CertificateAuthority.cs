// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VintageHive.Proxy.Security;

public static class CertificateAuthority
{
    /// <summary>
    /// The subject the root was created with before the CN followed <see cref="Mind.ProductName"/>, and still
    /// the subject a stock VintageHive uses. Also the DB key every existing box has its root filed under, which
    /// is why <see cref="LoadOrCreateRoot"/> migrates from it rather than treating a miss as "generate a new one".
    /// </summary>
    public const string LegacyName = "VintageHive Dialnine Cert Authority";

    /// <summary>
    /// Where the root is stored, independent of what it is called.
    /// <para>
    /// The root used to be keyed by its own subject name, which made the DB key move whenever the branding did.
    /// A miss then reads as "no root yet" and mints a new one, which silently invalidates every certificate
    /// already installed in a member's browser - they all start warning at once. Keying on something that never
    /// changes means renaming the product can never do that, however many times it is renamed.
    /// </para>
    /// <para>
    /// The '!' prefix cannot appear in a hostname, so this can never collide with a per-domain leaf, which are
    /// keyed by their hostname in the same table.
    /// </para>
    /// </summary>
    public const string StorageKey = "!root-ca";

    /// <summary>
    /// Fallback key size, period-correct for 1999 clients. Overridable via
    /// <see cref="ConfigNames.CertificateKeySize"/>; see <see cref="KeySize"/> for what that does and does not
    /// cover.
    /// </summary>
    public const int DefaultKeySize = 512;

    // Anything smaller cannot carry a SHA-256 PKCS#1 v1.5 signature (32 byte hash + 19 byte digest info + 11
    // byte minimum padding needs 62 bytes), so a misconfiguration would fail at signing time rather than here.
    const int MinimumKeySize = 512;

    const int ValidityPeriodYearsCA = 100;

    const int ValidityPeriodYearsCERT = 25;

    // X.509 bounds the common name at 64 characters. The suffix is fixed, so the product portion is what gets
    // clipped, leaving the suffix intact rather than producing a truncated "... Certificate Autho".
    const string SubjectSuffix = " Certificate Authority";

    const int MaxCommonNameLength = 64;

    static X509Certificate2 caCertificate;

    /// <summary>
    /// The subject the root is created with. Follows the embedder's product name so a whitelabelled plane does
    /// not put VintageHive's name into its members' trust stores; falls back to <see cref="LegacyName"/> when
    /// no product name is configured, so a stock plane is byte-identical to before.
    /// <para>
    /// This only affects a root at the moment it is GENERATED. The subject is signed into the certificate, so an
    /// existing plane keeps the name it was created with until an operator deliberately rotates - setting a
    /// product name must never invalidate certificates members have already trusted.
    /// </para>
    /// </summary>
    public static string SubjectName
    {
        get
        {
            var configured = Mind.Db?.ConfigGet<string>(ConfigNames.ProductName);

            if (string.IsNullOrWhiteSpace(configured))
            {
                return LegacyName;
            }

            var product = SanitiseForCommonName(configured);

            if (product.Length == 0)
            {
                return LegacyName;
            }

            var budget = MaxCommonNameLength - SubjectSuffix.Length;

            if (product.Length > budget)
            {
                product = product[..budget].TrimEnd();
            }

            return product + SubjectSuffix;
        }
    }

    /// <summary>
    /// Key size for newly generated certificates. NOT per host: an embedder wanting a larger key only for hosts
    /// that modern clients reach needs a per-host policy, which this is not. This is a single global override
    /// for a plane that has no period clients at all. Existing certificates are unaffected, including the root -
    /// changing this does not re-key anything already issued.
    /// </summary>
    static int KeySize
    {
        get
        {
            var configured = Mind.Db?.ConfigGet<int>(ConfigNames.CertificateKeySize) ?? DefaultKeySize;

            return configured < MinimumKeySize ? DefaultKeySize : configured;
        }
    }

    public static void Init()
    {
        if (caCertificate != null)
        {
            throw new ApplicationException("No, no, no, don't run twice.");
        }

        caCertificate = LoadOrCreateRoot();
    }

    /// <summary>
    /// The root certificate's PEM, for the download endpoint members install from. Prefers the loaded root and
    /// falls back to the store, so it works whether or not <see cref="Init"/> has run.
    /// <para>
    /// Never carries the private key, on any path. This is the "publish the root" accessor and it is reachable
    /// from a web endpoint, so it must not be possible for a caller to get the signing key out of it by
    /// accident - the store returns both halves together.
    /// </para>
    /// </summary>
    public static SslCertificate GetRootCertificate()
    {
        if (caCertificate != null)
        {
            return new SslCertificate(new string(PemEncoding.Write("CERTIFICATE", caCertificate.RawData)), string.Empty);
        }

        var stored = Mind.Db?.CertGet(StorageKey);

        if (!IsUsable(stored))
        {
            stored = FindMigratableRoot().Certificate;
        }

        return stored == null ? null : new SslCertificate(stored.Certificate, string.Empty);
    }

    /// <summary>
    /// Loads the existing root, migrating it off a legacy key if that is where it still lives, and only mints a
    /// new one when there genuinely is not one. Separate from <see cref="Init"/> so the migration is testable
    /// without the once-per-process static.
    /// </summary>
    internal static X509Certificate2 LoadOrCreateRoot()
    {
        var stored = Mind.Db.CertGet(StorageKey);

        if (IsUsable(stored))
        {
            return X509Certificate2.CreateFromPem(stored.Certificate, stored.Key);
        }

        var migratable = FindMigratableRoot();

        if (migratable.Certificate != null)
        {
            // Same key material, new home. The certificate itself is untouched, so every copy already sitting
            // in a member's browser stays valid and every leaf already issued still chains.
            Mind.Db.CertSet(StorageKey, migratable.Certificate);
            Mind.Db.CertSet(migratable.Key, null);

            Log.WriteLine(Log.LEVEL_INFO, nameof(CertificateAuthority), $"Migrated the existing root certificate from '{migratable.Key}' to the branding-independent store key; no rotation performed.", string.Empty);

            return X509Certificate2.CreateFromPem(migratable.Certificate.Certificate, migratable.Certificate.Key);
        }

        return CreateRoot();
    }

    // Where an older build would have filed the root: under its own subject name. The configured name is tried
    // first, then the stock literal, matching the order those builds would have written them in.
    static (string Key, SslCertificate Certificate) FindMigratableRoot()
    {
        foreach (var key in new[] { SubjectName, LegacyName }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(key, StorageKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = Mind.Db?.CertGet(key);

            if (IsUsable(candidate))
            {
                return (key, candidate);
            }
        }

        return default;
    }

    static X509Certificate2 CreateRoot()
    {
        using var rsa = RSA.Create(KeySize);

        var request = new CertificateRequest($"CN={SubjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, false));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(ValidityPeriodYearsCA));

        var certPem = new string(PemEncoding.Write("CERTIFICATE", certificate.RawData));
        var keyPem = new string(PemEncoding.Write("PRIVATE KEY", certificate.GetRSAPrivateKey().ExportPkcs8PrivateKey()));

        Mind.Db.CertSet(StorageKey, new SslCertificate(certPem, keyPem));

        Log.WriteLine(Log.LEVEL_INFO, nameof(CertificateAuthority), $"Generated a new root certificate authority: CN={SubjectName}", string.Empty);

        return certificate;
    }

    static bool IsUsable(SslCertificate certificate)
    {
        return certificate != null && !string.IsNullOrEmpty(certificate.Certificate) && !string.IsNullOrEmpty(certificate.Key);
    }

    /// <summary>
    /// Strips the characters that are structural inside a distinguished name. An unescaped comma or equals sign
    /// in the product name would make <c>CN=Acme, Inc. Certificate Authority</c> parse as a CN plus a second,
    /// invalid relative name, and throw at startup.
    /// <para>
    /// Stripping rather than backslash-escaping, and rather than building the name through
    /// X500DistinguishedNameBuilder, on purpose: both of those change how the subject is ENCODED (escaped text,
    /// and UTF8String instead of PrintableString respectively), and the clients this exists for are 1999
    /// browsers parsing a root out of a DER blob. The encoding stock VintageHive produces today is the one that
    /// is known to work, so it is left alone.
    /// </para>
    /// </summary>
    internal static string SanitiseForCommonName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var cleaned = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            // Structural in a DN, plus anything non-printable that a period client has no way to render.
            if (character is ',' or '+' or '=' or '"' or '<' or '>' or ';' or '\\' or '#' || char.IsControl(character))
            {
                continue;
            }

            cleaned.Append(character);
        }

        // Collapse the runs a removal can leave behind ("Acme, Inc" -> "Acme Inc", not "Acme  Inc").
        return string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static SslCertificate GetOrCreateDomainCertificate(string domain)
    {
        return GetOrCreateDomainCertificate(domain, caCertificate);
    }

    // Issuer passed in rather than read off the static, so the "a leaf still chains to the migrated root" claim
    // is testable without Init's once-per-process cache.
    internal static SslCertificate GetOrCreateDomainCertificate(string domain, X509Certificate2 issuer)
    {
        var domainCert = Mind.Db.CertGet(domain);

        if (IsUsable(domainCert))
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

        var domainCertificate = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(ValidityPeriodYearsCERT), serialNumber);

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
