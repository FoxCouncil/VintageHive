// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Security;

internal enum PrivateKeyType
{
	/// <summary>EVP_PKEY_RSA</summary>
	RSA = 6,
	/// <summary>EVP_PKEY_DSA</summary>
	DSA = 116,
	/// <summary>EVP_PKEY_DH</summary>
	DH = 28,
	/// <summary>EVP_PKEY_EC</summary>
	EC = 408
}
