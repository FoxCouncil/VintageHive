﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Oscar;

public enum OscarAuthError : ushort
{
    InvalidScreenNameOrPassword = 0x0001,
    ServiceTemporarilyUnavailable,
    Other,
    IncorrectScreenNameOrPassword,
    MismatchScreenNameOrPassword,
    InternalClientError,
    InvalidAccount,
    DeletedAccount,
    ExpiredAccount,
    NoAccessToDatabase,
    NoAccessToResolver,
    InvalidDatabaseFields,
    BadDatabaseStatus,
    BadResolverStatus,
    InternalError,
    ServiceTemporarilyOffline,
    SuspendedAccount,
    DBSendError,
    DBLinkError,
    ReservationMapError,
    ReservationLinkError,
    MaximumUsersFromIp,
    MaximumUsersFromIpReservation,
    RateLimitReservationExceeded,
    TooManyWarnings,
    ReservationTimeout,
    OlderClientVersionUpgradeRequired,
    OlderClientVersionUpgradeRecommended,
    RateLimitExceeded,
    CantRegisterICQ,
    InvalidSecurID,
    AccountSuspendedDueToAge
}
