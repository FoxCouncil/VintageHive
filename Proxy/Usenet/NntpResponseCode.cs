// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Usenet;

internal enum NntpResponseCode
{
    HelpTextFollows = 100,

    ServerReadyPostingAllowed = 200,
    ServerReadyNoPosting = 201,
    QuitGoodbye = 205,

    GroupSelected = 211,
    ListOfNewsgroupsFollows = 215,

    ArticleFollows = 220,
    HeadFollows = 221,
    BodyFollows = 222,
    ArticleExists = 223,
    OverviewFollows = 224,

    ListOfNewArticlesFollows = 230,
    ListOfNewGroupsFollows = 231,

    ArticlePosted = 240,

    AuthenticationAccepted = 281,

    SendArticle = 335,
    SendArticleToPost = 340,

    NoSuchGroup = 411,
    NoGroupSelected = 412,
    NoCurrentArticle = 420,
    NoNextArticle = 421,
    NoPreviousArticle = 422,
    NoSuchArticleNumber = 423,
    NoSuchArticleId = 430,
    PostingNotAllowed = 440,

    CommandNotRecognized = 500,
    SyntaxError = 501,
    AccessDenied = 502,
    InternalFault = 503
}
