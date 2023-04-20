// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding.RegisterProvider(new MacEncodingProvider());

Encoding.GetEncoding("ISO-8859-1");

await Mind.Init();

Mind.Start();