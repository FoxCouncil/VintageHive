using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Security
{
    internal class BigNumber : NativeRef
    {
        public static BigNumber Rsa3 => new(3);

        public BigNumber() : base(Native.BN_new()) { }

        public BigNumber(uint val) : base(Native.BN_new())
        {
            Value = val;
        }

        public uint Value
        {
            get { return Native.BN_get_word(this); }
            set { Native.CheckResultSuccess(Native.BN_set_word(this, value)); }
        }

        public override void Dispose()
        {
            Native.BN_free(this);
        }
    }
}
