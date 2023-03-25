using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Security;

internal class OpenSslException : Exception
{
    readonly List<uint> errors;

    readonly List<string> messages;

    readonly string message;

    public override string Message => message;

    public OpenSslException(string msg = null)
    {
        errors = new();
        messages = new();

        while (true)
        {
            var err = Native.ERR_get_error();

            if (err == 0)
            {
                break;
            }

            var buffer = new byte[240];

            Native.ERR_error_string_n(err, buffer, buffer.Length);

            var errStr = Encoding.ASCII.GetString(buffer).TrimEnd('\0');

            messages.Add(errStr);

            errors.Add(err);
        }

        message = msg ?? messages.FirstOrDefault() ?? "Oops, something went wrong.";
    }

    public static string GetOpenSslLibraryErrorMessage(uint errorCode)
    {
        return Native.ERR_lib_error_string(errorCode);
    }
}
