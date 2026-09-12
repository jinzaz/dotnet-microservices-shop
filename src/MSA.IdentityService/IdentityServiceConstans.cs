namespace MSA.IdentityService;

internal static class IdentityServiceConstants
{
    internal static class RateLimits
    {
        internal const string Authentication = "identity-authentication";
        internal const string Registration = "identity-registration";
        internal const string SmsDispatch = "identity-sms-dispatch";
    }

    #region Scopes
    internal static class Scopes
    {
        internal const string User = "user";
        internal const string Catalog = "catalog";
        internal const string Inventory = "inventory";
        internal const string Notification = "notification";
        internal const string Basket = "basket";
        internal const string Order = "order";
        internal const string Payment = "payment";
    }
    #endregion

}
