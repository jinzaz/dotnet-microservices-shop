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
        internal const string Auth = "auth";

        internal const string AuthAdminRead = "auth.admin.read";
        internal const string AuthAdminWrite = "auth.admin.write";

        internal const string User = "user";
        internal const string UserAdminRead = "user.admin.read";
        internal const string UserAdminWrite = "user.admin.write";


        internal const string Catalog = "catalog";
        internal const string CatalogAdminRead = "catalog.admin.read";
        internal const string CatalogAdminWrite = "catalog.admin.write";


        internal const string Inventory = "inventory";
        internal const string InventoryAdminRead = "inventory.admin.read";
        internal const string InventoryAdminWrite = "inventory.admin.write";


        internal const string Notification = "notification";
        internal const string NotificationAdminRead = "notification.admin.read";
        internal const string NotificationAdminWrite = "notification.admin.write";

        internal const string Basket = "basket";
        internal const string BasketAdminRead = "basket.admin.read";
        internal const string BasketAdminWrite = "basket.admin.write";


        internal const string Order = "order";
        internal const string OrderAdminRead = "order.admin.read";
        internal const string OrderAdminWrite = "order.admin.write";


        internal const string Payment = "payment";
        internal const string PaymentAdminRead = "payment.admin.read";
        internal const string PaymentAdminWrite = "payment.admin.write";

        internal const string CertificateAdminRead = "certificate.admin.read";
        internal const string CertificateAdminWrite = "certificate.admin.write";
    }
    #endregion


    #region Resources
    internal class Resources
    {
        internal const string Auth = "auth-api";
        internal const string User = "user-api";
        internal const string Catalog = "catalog-api";
        internal const string Inventory = "inventory-api";
        internal const string Notification = "notification-api";
        internal const string Basket = "basket-api";
        internal const string Order = "order-api";
        internal const string Payment = "payment-api";
        internal const string Certificate = "certificate-api";
    }
    #endregion
}
