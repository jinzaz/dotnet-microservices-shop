using MSA.Infrastrucute.Events;

namespace MSA.CatalogService.Domain;

public sealed record CategoryChangedDomainEvent(
    Category Category,
    long Version,
    string Change) : DomainEvent;

public sealed record ProductSnapshotChangedDomainEvent(
    Product Product,
    long Revision,
    string Change) : DomainEvent;

public sealed record PromotionSnapshotChangedDomainEvent(
    Promotion Promotion,
    long Revision,
    string Change) : DomainEvent;

public sealed record CouponSnapshotChangedDomainEvent(
    Coupon Coupon,
    long Revision,
    string Change) : DomainEvent;
