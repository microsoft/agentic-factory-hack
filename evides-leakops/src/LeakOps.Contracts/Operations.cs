namespace LeakOps.Contracts;

/// <summary>Written by reserve-stock. One reservation per incident and SKU.</summary>
public record StockReservation(
    string Id,                       // "{incidentId}-{sku}"
    string IncidentId,
    string Sku,
    string Name,
    string Kind,                     // part | equipment
    int Quantity,
    string WarehouseId,
    DateTimeOffset ReservedAtUtc);

/// <summary>Written by create-purchase-order.</summary>
public record PurchaseOrder(
    string Id,                       // "PO-{incidentId}-{sku}"
    string IncidentId,
    string Sku,
    string Name,
    int Quantity,
    double UnitPriceEur,
    string SupplierId,
    string SupplierName,
    bool Express,
    double TotalEur,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset ExpectedDeliveryUtc,
    string Status,                   // ordered | pending_approval
    bool ApprovalRequired,
    string Reason);

public record WorkOrderLine(string Sku, string Name, string Kind, int Quantity, string WarehouseId);

/// <summary>Written by create-work-order: the complete job package for the technician's mobile device.</summary>
public record WorkOrder(
    string Id,                       // "WO-{incidentId}"
    string IncidentId,
    string SegmentId,
    string Street,
    double ChainageM,
    GeoPoint Location,
    string BreachType,
    string Severity,
    string Summary,
    string TechnicianId,
    string TechnicianName,
    string ProcedureId,
    IReadOnlyList<string> RequiredCertifications,
    IReadOnlyList<WorkOrderLine> Lines,                 // reserved parts and equipment
    IReadOnlyList<string> PurchaseOrderIds,
    DateTimeOffset ScheduledStartUtc,                   // when the technician leaves
    int TravelMinutes,
    int IsolationMinutes,
    int PartsWaitMinutes,                               // extra waiting for a part that is still on order; 0 when the package is complete
    double RepairHours,
    DateTimeOffset ExpectedArrivalUtc,
    DateTimeOffset ExpectedCompletionUtc,
    string Status,                   // scheduled
    DateTimeOffset DispatchedToMobileUtc,
    string? Notes,
    DateTimeOffset CreatedUtc);
