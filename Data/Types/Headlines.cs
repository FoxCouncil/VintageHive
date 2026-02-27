// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types
{
    public class Headlines
    {
        public string Id { get; set; }

        public string Source { get; set; }

        public string Title { get; set; }

        public string Summary { get; set; }

        public DateTimeOffset Published { get; set; }
    }
}
