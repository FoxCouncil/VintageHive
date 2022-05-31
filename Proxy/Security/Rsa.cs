using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Security
{
    public class Rsa : NativeRef
    {
        public int Size => Native.CheckResultSuccess(Native.RSA_size(this));

        public Rsa() : base(Native.RSA_new()) { }

        public void GenerateKey(int bits, int e)
        {
            var result = Native.RSA_generate_key_ex(this, bits, BigNumber.Rsa3, IntPtr.Zero);

            Native.CheckResultSuccess(result);
        }

        public string PEMPrivateKey()
        {
            var writeBio = new BasicInputOutput();

            Native.CheckResultSuccess(Native.PEM_write_bio_RSAPrivateKey(writeBio, this, IntPtr.Zero, null, 0, IntPtr.Zero, IntPtr.Zero));

            return writeBio.ToString();
        }

        public override void Dispose()
        {
            Native.RSA_free(this);
        }
    }
}
