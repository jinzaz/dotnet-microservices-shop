using MSA.Infrastrucute.Entities;
namespace MSA.InventoryService.Domain;
public sealed class StockItem : AggregateRoot<Guid>
{
    private StockItem() { }
    public StockItem(Guid id,string sku,int onHand){if(onHand<0)throw new ArgumentOutOfRangeException(nameof(onHand));Id=id;Sku=sku;OnHand=onHand;}
    public string Sku{get;private set;}=string.Empty; public int OnHand{get;private set;} public int Reserved{get;private set;} public int Available=>OnHand-Reserved; public long Version{get;private set;}
    public void Increase(int quantity){if(quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity));OnHand+=quantity;Version++;}
    public void Reserve(int quantity){if(quantity<=0||Available<quantity)throw new InvalidOperationException($"Insufficient inventory for {Sku}.");Reserved+=quantity;Version++;}
    public void Release(int quantity){if(quantity<=0||Reserved<quantity)throw new InvalidOperationException($"Invalid release for {Sku}.");Reserved-=quantity;Version++;}
    public void Commit(int quantity){if(quantity<=0||Reserved<quantity)throw new InvalidOperationException($"Invalid commit for {Sku}.");Reserved-=quantity;OnHand-=quantity;Version++;}
}
public sealed class InventoryReservation : AggregateRoot<Guid>
{
    private InventoryReservation() { }
    public InventoryReservation(Guid id,Guid orderId,IEnumerable<InventoryReservationLine> lines){Id=id;OrderId=orderId;Status=ReservationStatus.Reserved;CreatedOnUtc=DateTimeOffset.UtcNow;_lines.AddRange(lines);}
    private readonly List<InventoryReservationLine> _lines=[]; public Guid OrderId{get;private set;} public ReservationStatus Status{get;private set;} public DateTimeOffset CreatedOnUtc{get;private set;} public IReadOnlyCollection<InventoryReservationLine> Lines=>_lines;
    public void Release(){if(Status==ReservationStatus.Reserved)Status=ReservationStatus.Released;} public void Commit(){if(Status==ReservationStatus.Reserved)Status=ReservationStatus.Committed;}
}
public sealed class InventoryReservationLine { private InventoryReservationLine(){} public InventoryReservationLine(string sku,int quantity){Sku=sku;Quantity=quantity;} public long Id{get;private set;} public string Sku{get;private set;}=string.Empty; public int Quantity{get;private set;} }
public enum ReservationStatus{Reserved,Committed,Released}
