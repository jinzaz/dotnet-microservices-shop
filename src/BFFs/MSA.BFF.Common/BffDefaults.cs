namespace MSA.BFF.Common;

internal static class BffDefaults
{
    internal const string CookieScheme = "BffCookie";
    internal const string EndpointPrefix = "/bff";
}

internal sealed record BffApplicationOptions(string Name)
{
    internal string SessionCookieName => $"__Host-{Name}Session";
    internal string AntiforgeryCookieName => $"__Host-{Name}Session.XSRF-TOKEN";
    internal string RedisInstanceName => $"{Name}Bff:";
    internal string TicketProtectionPurpose => $"{Name}.BFF.RedisTicketStore.v1";
}
