﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities
{
    internal class MacEncodingProvider : EncodingProvider
    {
        public override Encoding? GetEncoding(int codepage)
        {
            return null;
        }

        public override Encoding? GetEncoding(string name)
        {
            if (name == "maccentraleurope")
            {
                return Encoding.GetEncoding(10029);
            }

            return null;
        }
    }
}
