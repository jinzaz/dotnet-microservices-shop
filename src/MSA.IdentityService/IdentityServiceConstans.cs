namespace MSA.IdentityService;

internal static class IdentityServiceConstants
{
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

    internal static class Applications
    {
        internal const string MSA_Bff_Web = "MSA_Bff_Web";

        internal const string MSA_Bff_BM = "MSA_Bff_BM";
    }
}
