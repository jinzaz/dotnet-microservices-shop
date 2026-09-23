using System.Security.Cryptography;
using System.Text;

namespace MSA.BFF.BM.Authorization;

internal static class PbacSeedData
{
    internal static readonly Guid SystemAdministratorRoleId = StableGuid("role:system-administrator");
    internal static readonly Guid SystemAdministratorPolicyId = StableGuid("policy:system-administrators");

    private static readonly (string Code, string DisplayName)[] Modules =
    [
        ("system.authorization", "权限管理"),
        ("identity.users", "用户账号"),
        ("user.loyalty-points", "会员积分"),
        ("catalog.categories", "商品分类"),
        ("catalog.products", "商品"),
        ("catalog.promotions", "促销活动"),
        ("catalog.coupons", "优惠券"),
        ("inventory.stock", "库存"),
        ("inventory.allocations", "活动库存配额"),
        ("order.fulfillment", "订单履约"),
        ("payment.transactions", "支付交易"),
        ("notification.messages", "用户通知"),
        ("system.messaging", "消息运维")
    ];

    internal static readonly BmRole[] SystemAdministratorRole =
    [
        new(
            SystemAdministratorRoleId,
            "system-administrator",
            "系统管理员",
            true,
            new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero))
    ];

    internal static readonly PbacPermission[] Permissions = CreatePermissions();

    private static readonly IReadOnlyDictionary<string, Guid> PermissionIds = Permissions
        .ToDictionary(permission => permission.Code, permission => permission.Id, StringComparer.Ordinal);

    internal static readonly PbacRoutePermission[] Routes = CreateRoutes();

    internal static readonly PbacPolicy[] SystemAdministratorPolicy =
    [
        new(
            SystemAdministratorPolicyId,
            "System administrators",
            PbacEffect.Allow,
            new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero))
    ];

    internal static readonly PbacPolicyPermission[] SystemAdministratorPermissions = Permissions
        .Select(permission => new PbacPolicyPermission(SystemAdministratorPolicyId, permission.Id))
        .ToArray();

    internal static readonly PbacRolePolicy[] SystemAdministratorRolePolicy =
    [
        new(SystemAdministratorPolicyId, SystemAdministratorRoleId)
    ];

    private static PbacPermission[] CreatePermissions()
    {
        var actionNames = new Dictionary<PbacAction, string>
        {
            [PbacAction.Query] = "查询",
            [PbacAction.Create] = "创建",
            [PbacAction.Edit] = "编辑",
            [PbacAction.Delete] = "删除"
        };

        return Modules
            .SelectMany(module => Enum.GetValues<PbacAction>().Select(action =>
            {
                var code = $"{module.Code}.{action.ToString().ToLowerInvariant()}";
                return new PbacPermission(
                    StableGuid($"permission:{code}"),
                    code,
                    module.Code,
                    action,
                    $"{module.DisplayName}-{actionNames[action]}");
            }))
            .ToArray();
    }

    private static PbacRoutePermission[] CreateRoutes()
    {
        (string Permission, string Method, string Path)[] definitions =
        [
            ("identity.users.query", HttpMethods.Get, "/api/Auth/users/admin"),
            ("identity.users.edit", HttpMethods.Post, "/api/Auth/users/admin/{id}/roles/{role}"),
            ("identity.users.edit", HttpMethods.Post, "/api/Auth/users/admin/{id}/activation"),
            ("user.loyalty-points.query", HttpMethods.Get, "/api/user/admin/points/{userId}"),
            ("user.loyalty-points.query", HttpMethods.Get, "/api/user/admin/points/{userId}/entries"),
            ("user.loyalty-points.query", HttpMethods.Get, "/api/user/admin/points/{userId}/entries/{entryId}"),
            ("user.loyalty-points.edit", HttpMethods.Post, "/api/user/admin/points/{userId}/adjustments"),
            ("catalog.categories.query", HttpMethods.Get, "/api/catalog/admin/categories"),
            ("catalog.categories.create", HttpMethods.Post, "/api/catalog/admin/categories"),
            ("catalog.categories.edit", HttpMethods.Put, "/api/catalog/admin/categories/{id}/name"),
            ("catalog.categories.edit", HttpMethods.Post, "/api/catalog/admin/categories/{id}/activate"),
            ("catalog.categories.edit", HttpMethods.Post, "/api/catalog/admin/categories/{id}/archive"),
            ("catalog.products.query", HttpMethods.Get, "/api/catalog/admin/products"),
            ("catalog.products.query", HttpMethods.Get, "/api/catalog/admin/products/{id}"),
            ("catalog.products.create", HttpMethods.Post, "/api/catalog/admin/products"),
            ("catalog.products.edit", HttpMethods.Put, "/api/catalog/admin/products/{id}/details"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/price"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/publish"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/unpublish"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/archive"),
            ("catalog.promotions.query", HttpMethods.Get, "/api/catalog/admin/promotions"),
            ("catalog.promotions.query", HttpMethods.Get, "/api/catalog/admin/promotions/{id}"),
            ("catalog.promotions.create", HttpMethods.Post, "/api/catalog/admin/promotions"),
            ("catalog.promotions.edit", HttpMethods.Put, "/api/catalog/admin/promotions/{id}"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/publish"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/pause"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/resume"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/end"),
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupons"),
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupons/{id}"),
            ("catalog.coupons.create", HttpMethods.Post, "/api/catalog/admin/coupons"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupons/{id}/activate"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupons/{id}/disable"),
            ("inventory.stock.query", HttpMethods.Get, "/api/inventory/admin/stock/{sku}/movements"),
            ("inventory.stock.create", HttpMethods.Post, "/api/inventory/admin/stock"),
            ("inventory.stock.edit", HttpMethods.Post, "/api/inventory/admin/stock/adjustments"),
            ("inventory.allocations.query", HttpMethods.Get, "/api/inventory/admin/allocations/{allocationId}"),
            ("inventory.allocations.create", HttpMethods.Post, "/api/inventory/admin/allocations"),
            ("inventory.allocations.edit", HttpMethods.Post, "/api/inventory/admin/allocations/{allocationId}/close"),
            ("order.fulfillment.edit", HttpMethods.Post, "/api/order/admin/orders/{orderId}/fulfillment/ship"),
            ("order.fulfillment.edit", HttpMethods.Post, "/api/order/admin/orders/{orderId}/fulfillment/in-transit"),
            ("order.fulfillment.edit", HttpMethods.Post, "/api/order/admin/orders/{orderId}/fulfillment/delivered"),
            ("payment.transactions.query", HttpMethods.Get, "/api/payment/admin/orders/{orderId}"),
            ("notification.messages.query", HttpMethods.Get, "/api/notification/admin/users/{userId}"),
            ("identity.users.query", HttpMethods.Get, "/api/Auth/users/admin/roles"),
            ("identity.users.query", HttpMethods.Get, "/api/Auth/users/admin/{id}"),
            ("identity.users.edit", HttpMethods.Delete, "/api/Auth/users/admin/{id}/roles/{role}"),
            ("inventory.stock.query", HttpMethods.Get, "/api/inventory/admin/stock"),
            ("order.fulfillment.query", HttpMethods.Get, "/api/order/admin/orders"),
            ("order.fulfillment.query", HttpMethods.Get, "/api/order/admin/orders/{id}"),
            ("payment.transactions.query", HttpMethods.Get, "/api/payment/admin/payments"),
            ("notification.messages.query", HttpMethods.Get, "/api/notification/admin/messages"),
            ("notification.messages.query", HttpMethods.Get, "/api/notification/admin/templates"),
            ("notification.messages.create", HttpMethods.Post, "/api/notification/admin/messages"),
            ("notification.messages.edit", HttpMethods.Post, "/api/notification/admin/messages/{notificationId}/retry"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/variants"),
            ("catalog.products.edit", HttpMethods.Put, "/api/catalog/admin/products/{id}/variants/{variantId}"),
            ("catalog.products.edit", HttpMethods.Delete, "/api/catalog/admin/products/{id}/variants/{variantId}"),
            ("catalog.products.edit", HttpMethods.Post, "/api/catalog/admin/products/{id}/media"),
            ("catalog.products.edit", HttpMethods.Put, "/api/catalog/admin/products/{id}/media/{mediaId}"),
            ("catalog.products.edit", HttpMethods.Delete, "/api/catalog/admin/products/{id}/media/{mediaId}"),
            ("inventory.allocations.query", HttpMethods.Get, "/api/inventory/admin/allocations"),
            ("order.fulfillment.query", HttpMethods.Get, "/api/order/admin/orders/summary"),
            ("payment.transactions.query", HttpMethods.Get, "/api/payment/admin/payments/summary"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupons/{id}/expire"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/revise"),
            ("catalog.promotions.edit", HttpMethods.Post, "/api/catalog/admin/promotions/{id}/archive"),
            ("catalog.coupons.create", HttpMethods.Post, "/api/catalog/admin/coupons/batch"),
            // Usage counters live in Ordering but belong to the coupon screen, so they share its query permission.
            ("catalog.coupons.query", HttpMethods.Get, "/api/order/admin/coupons/usage"),
            // Claim campaigns are how coupons are handed out, so they reuse the coupon permissions.
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupon-campaigns"),
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupon-campaigns/{id}"),
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupon-campaigns/{id}/stats"),
            ("catalog.coupons.query", HttpMethods.Get, "/api/catalog/admin/coupon-campaigns/{id}/claims"),
            ("catalog.coupons.create", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns"),
            ("catalog.coupons.edit", HttpMethods.Put, "/api/catalog/admin/coupon-campaigns/{id}"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns/{id}/publish"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns/{id}/pause"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns/{id}/resume"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns/{id}/end"),
            ("catalog.coupons.edit", HttpMethods.Post, "/api/catalog/admin/coupon-campaigns/{id}/quantity"),
            .. MessagingRoutes()
        ];

        return definitions
            .Select((route, order) => new PbacRoutePermission(
                StableGuid($"route:{route.Method}:{route.Path}"),
                PermissionIds[route.Permission],
                route.Method,
                route.Path,
                order))
            .ToArray();
    }

    /// <summary>
    /// 每个服务都有一组一致的消息运维接口（失败消息、卡住的 Saga、死信），统一归到 system.messaging 模块。
    /// </summary>
    private static (string Permission, string Method, string Path)[] MessagingRoutes() =>
        new[] { "catalog", "order", "inventory", "payment", "basket", "notification" }
            .SelectMany(service => new (string Permission, string Method, string Path)[]
            {
                ("system.messaging.query", HttpMethods.Get, $"/api/{service}/admin/messaging/outbox/failed"),
                ("system.messaging.query", HttpMethods.Get, $"/api/{service}/admin/messaging/sagas/stalled"),
                ("system.messaging.query", HttpMethods.Get, $"/api/{service}/admin/messaging/dead-letters"),
                ("system.messaging.edit", HttpMethods.Post, $"/api/{service}/admin/messaging/outbox/failed/requeue"),
                ("system.messaging.edit", HttpMethods.Post, $"/api/{service}/admin/messaging/dead-letters/replay")
            })
            .ToArray();

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
