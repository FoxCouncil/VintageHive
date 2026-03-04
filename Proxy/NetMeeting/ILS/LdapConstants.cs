// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.NetMeeting.ILS;

/// <summary>
/// LDAP wire protocol constants: application tag numbers, result codes, filter tags.
/// Tag numbers are the 5-bit values used with BerTag.Application() / BerTag.Context().
/// Full tag bytes are pre-computed for switch dispatch.
/// </summary>
internal static class LdapConstants
{
    // Application tag numbers (for building responses)
    public const int OP_BIND_REQUEST = 0;
    public const int OP_BIND_RESPONSE = 1;
    public const int OP_UNBIND_REQUEST = 2;
    public const int OP_SEARCH_REQUEST = 3;
    public const int OP_SEARCH_RESULT_ENTRY = 4;
    public const int OP_SEARCH_RESULT_DONE = 5;
    public const int OP_MODIFY_REQUEST = 6;
    public const int OP_MODIFY_RESPONSE = 7;
    public const int OP_ADD_REQUEST = 8;
    public const int OP_ADD_RESPONSE = 9;
    public const int OP_DELETE_REQUEST = 10;
    public const int OP_DELETE_RESPONSE = 11;

    // Full tag bytes (for reading/dispatch)
    public const byte TAG_BIND_REQUEST = 0x60;           // APPLICATION CONSTRUCTED 0
    public const byte TAG_BIND_RESPONSE = 0x61;          // APPLICATION CONSTRUCTED 1
    public const byte TAG_UNBIND_REQUEST = 0x42;         // APPLICATION PRIMITIVE 2
    public const byte TAG_SEARCH_REQUEST = 0x63;         // APPLICATION CONSTRUCTED 3
    public const byte TAG_SEARCH_RESULT_ENTRY = 0x64;    // APPLICATION CONSTRUCTED 4
    public const byte TAG_SEARCH_RESULT_DONE = 0x65;     // APPLICATION CONSTRUCTED 5
    public const byte TAG_MODIFY_REQUEST = 0x66;         // APPLICATION CONSTRUCTED 6
    public const byte TAG_MODIFY_RESPONSE = 0x67;        // APPLICATION CONSTRUCTED 7
    public const byte TAG_ADD_REQUEST = 0x68;            // APPLICATION CONSTRUCTED 8
    public const byte TAG_ADD_RESPONSE = 0x69;           // APPLICATION CONSTRUCTED 9
    public const byte TAG_DELETE_REQUEST = 0x4A;         // APPLICATION PRIMITIVE 10
    public const byte TAG_DELETE_RESPONSE = 0x6B;        // APPLICATION CONSTRUCTED 11

    // LDAP result codes (RFC 4511 Appendix A)
    public const int RESULT_SUCCESS = 0;
    public const int RESULT_OPERATIONS_ERROR = 1;
    public const int RESULT_PROTOCOL_ERROR = 2;
    public const int RESULT_NO_SUCH_OBJECT = 32;
    public const int RESULT_INVALID_DN_SYNTAX = 34;
    public const int RESULT_ENTRY_ALREADY_EXISTS = 68;
    public const int RESULT_OTHER = 80;

    // Search filter context-specific tags
    public const byte FILTER_AND = 0xA0;          // CONTEXT CONSTRUCTED [0]
    public const byte FILTER_OR = 0xA1;           // CONTEXT CONSTRUCTED [1]
    public const byte FILTER_NOT = 0xA2;          // CONTEXT CONSTRUCTED [2]
    public const byte FILTER_EQUALITY = 0xA3;     // CONTEXT CONSTRUCTED [3]
    public const byte FILTER_SUBSTRINGS = 0xA4;   // CONTEXT CONSTRUCTED [4]
    public const byte FILTER_PRESENT = 0x87;      // CONTEXT PRIMITIVE [7]

    // Substring choice tags (context-specific primitive)
    public const byte SUBSTRING_INITIAL = 0x80;
    public const byte SUBSTRING_ANY = 0x81;
    public const byte SUBSTRING_FINAL = 0x82;

    // LDAP Modify operation types
    public const int MOD_ADD = 0;
    public const int MOD_DELETE = 1;
    public const int MOD_REPLACE = 2;

    // ILS defaults
    public const int DEFAULT_TTL_MINUTES = 10;
    public const int MAX_MESSAGE_SIZE = 1024 * 1024; // 1 MB sanity limit
}
