namespace Rfid.Domain;

public enum UserRole { Admin, Operator, Viewer, Device }

public enum PartyKind { Person, Employee, Customer, Supplier, Department, Vehicle, Patient, Other }

public enum LocationKind
{
    Site, Building, Floor, Room, Zone, Area, Rack, Shelf, Bin, Dock, Gate,
    Vehicle, Vessel, Cabinet, Locker, Yard, Customer, External, MusterPoint, Checkpoint
}

public enum ItemCategory { Serialized, Quantity }

public enum ItemStatus { Active, Missing, Disposed, Retired }

public enum TagTechnology { UhfGen2, HfNfc, Lf, Ble, Active, Barcode }

public enum TagStatus { Unassigned, Active, Retired }

/// <summary>Gateway = an edge agent that drives readers on site and forwards batches (store-and-forward).</summary>
public enum DeviceKind { Handheld, Fixed, Portal, Gate, Cabinet, Shelf, Locker, Tunnel, Vehicle, Printer, Gateway }

public enum AntennaDirection { None, In, Out }

public enum OperationType
{
    Receive, Transfer, Issue, Return, Count, Dispatch, Inspect, Maintain, Dispose,
    Pack, Unpack, ProcessStage, Commission, Adjust,
    /// <summary>Quantity items: move part of a lot to another location (split/merge lot rows).</summary>
    MoveQuantity
}

public enum OperationStatus { Draft, Completed, Cancelled }

public enum LineResult { Ok, Unknown, Unexpected, Rejected }

public enum ItemEventType
{
    Created, Seen, Moved, CustodyChanged, StateChanged, Counted, QuantityChanged,
    Packed, Unpacked, Inspected, Maintained, Disposed, Commissioned, Alert, LabelPrinted, Imported,
    GeofenceEntered, GeofenceExited, Positioned
}

public enum ReadSource { Handheld, Fixed, Manual }

public enum StocktakeStatus { Open, Reconciled, Applied, Cancelled }

public enum StocktakeResult { Pending, Found, Missing, Unexpected, Unknown }

public enum RuleAction { CreateAlert, SetState, Webhook, Notify, RunOperation }

/// <summary>Event rules react to one item event; schedule rules sweep items on an interval (not seen for N hours, inspection due …).</summary>
public enum RuleKind { Event, Schedule }

public enum Severity { Info, Warning, Critical }

public enum AlertStatus { Open, Acknowledged, Closed }

public enum IntegrationFormat { Generic, SapAssetManagement, Dynamics365, Maximo }

public enum IntegrationAuth { None, Bearer, Basic, OAuth2ClientCredentials }

public enum PrintJobStatus { Queued, Printing, Printed, Failed, Cancelled }

public enum PrintReason { Initial, Reprint, Replacement, Batch }

public enum DeviceHealth { Unknown, Online, Degraded, Offline, Updating }

public enum FirmwareRolloutStatus { Pending, Sent, Downloading, Installing, Done, Failed, Cancelled }

public enum GeoFenceKind { Circle, Polygon }

public enum GeoFenceTrigger { Enter, Exit, Both }

public enum NotificationKind { Email, Sms, Teams, Slack, Webhook }

public enum NotificationStatus { Sent, Failed, Skipped, Queued }

public enum AnomalyKind { ReadRateSpike, ReadRateDrop, OffHoursActivity, UnknownTagSurge, ItemFlapping, ExcessiveMovement }

public enum AnomalyStatus { Open, Confirmed, Dismissed }

public enum EpcScheme { Sgtin96, Sscc96, Grai96, Giai96 }

public enum LedgerKind { Deposit, DepositRefund, CycleFee, DailyFee, LateFee, LossFee, Adjustment, Payment }

public enum InvoiceStatus { Draft, Issued, Paid, Void }
