using System.Text;
using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

internal class X509Name : NativeRef
{
    public string Common
    {
        get { return GetTextByName("CN"); }
        set { AddEntry("CN", value); }
    }

    public X509Name() : base(CheckResultSuccess(X509_NAME_new())) { }

    public X509Name(IntPtr other) : base(CheckResultSuccess(X509_NAME_dup(other))) { }

    public X509Name(string name) : this()
    {
        if (name.Contains('/') && name.Contains('='))
        {
            var parts = name.Split('/');

            foreach(var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                var nvp = part.Split('=');

                AddEntry(nvp[0], nvp[1]);
            }
        }
        else
        {
            Common = name;
        }
    }

    public void AddEntry(string name, string value)
    {
        AddEntryByNid(TextToNID(name), value);
    }

    public void AddEntryByNid(int nid, string value)
    {
        var buf = Encoding.ASCII.GetBytes(value);

        CheckResultSuccess(X509_NAME_add_entry_by_NID(this, nid, MBSTRING_ASC, buf, buf.Length, -1, 0));
    }

    public string GetTextByName(string name)
    {
        var nid = TextToNID(name);

        return GetTextByNid(nid);
    }

    private string GetTextByNid(int nid)
    {
        if (GetIndexByNid(nid, -1) == -1)
        {
            return null;
        }

        var buf = new byte[1024];

        var len = X509_NAME_get_text_by_NID(this, nid, buf, buf.Length);

        if (len <= 0)
        {
            throw new OpenSslException();
        }

        return Encoding.ASCII.GetString(buf, 0, len);
    }

    private int GetIndexByNid(int nid, int lastpos)
    {
        var ret = X509_NAME_get_index_by_NID(this, nid, lastpos);

        if (ret == lastpos)
        {
            return lastpos;
        }

        if (ret < 0)
        {
            throw new OpenSslException();
        }

        return ret;
    }

    private static int TextToNID(string name)
    {
        var nid = OBJ_txt2nid(name);

        if (nid == NID_UNDEFINED)
        {
            throw new OpenSslException();
        }

        return nid;
    }

    public override void Dispose()
    {
        X509_NAME_free(this);
    }
}
