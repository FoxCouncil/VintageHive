namespace VintageHive.Proxy.Security
{
    public class BigNumber : NativeRef
    {
        public static BigNumber Rsa3 => new(3);

        public static BigNumber Rsa7 => new(7);

        public static BigNumber Rsa65537 => new(65537);

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
