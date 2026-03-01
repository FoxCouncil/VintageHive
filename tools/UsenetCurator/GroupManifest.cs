// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using UsenetCurator.Sources;

namespace UsenetCurator;

internal class GroupDefinition
{
    public string Name { get; init; }

    public string Description { get; init; }

    public string Collection { get; init; }

    public int MaxArticles { get; init; }
}

internal static class GroupManifest
{
    public static readonly GroupDefinition[] Groups =
    [
        // comp.* hierarchy
        new() { Name = "comp.os.minix",              Description = "MINIX operating system discussions",        Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.lang.c",                Description = "The C programming language",                Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.lang.python",           Description = "Python programming language",               Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.lang.perl.misc",        Description = "Perl programming language",                 Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.unix.wizards",          Description = "Questions for Unix wizards",                Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.arch",                  Description = "Computer architecture discussions",         Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.sys.ibm.pc.hardware",   Description = "IBM PC hardware discussions",               Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.os.linux.misc",         Description = "Linux operating system miscellaneous",      Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.os.ms-windows.win95",   Description = "Microsoft Windows 95 discussions",          Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.sys.mac.hardware",      Description = "Macintosh hardware discussions",            Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.infosystems.www.misc",  Description = "World Wide Web discussions",                Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.security.misc",         Description = "Computer security discussions",             Collection = "usenet-comp", MaxArticles = 2000 },
        new() { Name = "comp.dcom.modems",           Description = "Modem and dial-up discussions",             Collection = "usenet-comp", MaxArticles = 2000 },

        // rec.* hierarchy
        new() { Name = "rec.games.computer.doom",    Description = "DOOM game discussions",                     Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.arts.movies",            Description = "Movie discussions and reviews",             Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.music.makers",           Description = "Music creation and performance",            Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.humor",                  Description = "Jokes and humorous articles",               Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.arts.startrek.misc",     Description = "Star Trek discussions",                     Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.games.video.classic",    Description = "Classic video game discussions",            Collection = "usenet-rec",  MaxArticles = 2000 },
        new() { Name = "rec.autos",                  Description = "Automobile discussions",                    Collection = "usenet-rec",  MaxArticles = 1500 },

        // alt.* hierarchy
        new() { Name = "alt.folklore.computers",     Description = "Stories and strife of computing",           Collection = "usenet-alt",  MaxArticles = 2000 },
        new() { Name = "alt.hackers",                Description = "Hacker culture discussions",                Collection = "usenet-alt",  MaxArticles = 2000 },
        new() { Name = "alt.fan.bill-gates",         Description = "Discussion of Bill Gates",                  Collection = "usenet-alt",  MaxArticles = 2000 },
        new() { Name = "alt.bbs",                    Description = "Bulletin board system culture",             Collection = "usenet-alt",  MaxArticles = 2000 },
        new() { Name = "alt.internet.services",      Description = "Internet services and resources",           Collection = "usenet-alt",  MaxArticles = 2000 },

        // sci.* hierarchy
        new() { Name = "sci.space",                  Description = "Space science and exploration",             Collection = "usenet-sci",  MaxArticles = 2000 },
        new() { Name = "sci.electronics",            Description = "Electronic circuits and components",        Collection = "usenet-sci",  MaxArticles = 2000 },
        new() { Name = "sci.space.shuttle",          Description = "Space Shuttle program discussions",         Collection = "usenet-sci",  MaxArticles = 2000 },

        // news.* hierarchy
        new() { Name = "news.announce.newusers",     Description = "Explanatory postings for new users",       Collection = "usenet-news", MaxArticles = 1000 },
        new() { Name = "news.groups",                Description = "Discussions and lists of newsgroups",       Collection = "usenet-news", MaxArticles = 2000 },

        // misc.* hierarchy
        new() { Name = "misc.forsale.computers",     Description = "Computers and parts for sale",              Collection = "usenet-misc", MaxArticles = 2000 },

        // soc.* hierarchy
        new() { Name = "soc.culture.usa",            Description = "American culture and society",              Collection = "usenet-soc",  MaxArticles = 2000 },

        // talk.* hierarchy
        new() { Name = "talk.bizarre",               Description = "The unusual, strstrstrstrsting bizarre",    Collection = "usenet-talk", MaxArticles = 1500 },
    ];

    public static GroupDefinition FromDiscovered(DiscoveredGroup discovered)
    {
        return new GroupDefinition
        {
            Name = discovered.Name,
            Description = $"{discovered.Name} discussions",
            Collection = discovered.Collection,
            MaxArticles = 2000,
        };
    }

    public static string GetDownloadUrl(GroupDefinition group)
    {
        return $"https://archive.org/download/{group.Collection}/{group.Name}.mbox.zip";
    }
}
