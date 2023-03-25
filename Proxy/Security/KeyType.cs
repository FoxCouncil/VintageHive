namespace VintageHive.Proxy.Security;

public enum KeyType
{ 
    RSA = 6, // EVP_PKEY_RSA
    DSA = 116, // EVP_PKEY_DSA
    DH = 28, // EVP_PKEY_DH
    EC = 408 // EVP_PKEY_EC
}
