namespace Pss.Contracts.Booking;

// Ordinals from the 4.5.2 GraphQL SDL (blueprint §11). Serialized as integers; accepted as int or name.

public enum BookingStatus { Default = 0, Hold = 1, Confirmed = 2, Closed = 3, HoldCanceled = 4, PendingArchive = 5, Archived = 6, ArchivedPrivate = 7 }
public enum PaidStatus { UnderPaid = 0, PaidInFull = 1, OverPaid = 2 }
public enum PriceStatus { Invalid = 0, Override = 1, Valid = 2 }
public enum ChannelType { Default = 0, Direct = 1, Web = 2, Gds = 3, Api = 4, DigitalApi = 5, DigitalWeb = 6, Ndc = 7 }
public enum Gender { XX = 0, Male = 1, Female = 2 }
public enum WeightCategory { Unassigned = 0, Male = 1, Female = 2, Child = 3 }
public enum FlightType { None = 0, NonStop = 1, Through = 2, Direct = 3, Connect = 4, All = 5 }
public enum SegmentStatus
{
    UnknownStatus = 0, UnableToConfirmSegment = 1, UnableToSellSegment = 2, HeldPending = 3, WaitListConfirmed = 4,
    HeldUnconfirmed = 5, HeldConfirmed = 6, HeldCancelled = 7, Confirming = 8, ConfirmingWaitList = 9, ConfirmedTimeChange = 10
}
public enum LegStatus { Normal = 0, Closed = 1, Canceled = 2, Suspended = 3, ClosedPending = 5, BlockAllActivities = 6, Mishap = 7 }
public enum LiftStatus { Default = 0, CheckedIn = 1, Boarded = 2, NoShow = 3 }
public enum ChargeType
{
    FarePrice = 0, Discount = 1, IncludedTravelFee = 2, IncludedTax = 3, TravelFee = 4, Tax = 5, ServiceCharge = 6,
    PromotionDiscount = 7, ConnectionAdjustmentAmount = 8, AddOnsPrice = 9, FarePoints = 10, DiscountPoints = 11,
    IncludedAddOnsFee = 16, AddOnsFee = 17, AddOnsMarkup = 18, FareSurcharge = 19, AddOnsCancelFee = 21, Calculated = 22,
    Note = 23, Points = 24, DynamicFareAdjustment = 25
}
public enum FeeType
{
    All = 0, Tax = 1, TravelFee = 2, ServiceFee = 3, PaymentFee = 4, PenaltyFee = 5, SsrFee = 6, NonFlightServiceFee = 7,
    UpgradeFee = 8, SeatFee = 9, BaseFare = 10, SpoilageFee = 11, NameChangeFee = 12, ConvenienceFee = 13, BaggageFee = 14,
    FareSurcharge = 15, PromotionDiscount = 16, ServiceBundle = 17, ExtraBagFee = 18, ATPCOBagFee = 19
}
public enum PaymentMethodType { ExternalAccount = 0, PrePaid = 1, AgencyAccount = 2, CustomerAccount = 3, Voucher = 4, Loyalty = 5 }
public enum BookingPaymentStatus { New = 0, Received = 1, Pending = 2, Approved = 3, Declined = 4, Unknown = 5, PendingCustomerAction = 6 }
public enum AuthorizationStatus
{
    Unknown = 0, Acknowledged = 1, Pending = 2, InProcess = 3, Approved = 4, Declined = 5, Referral = 6, PickUpCard = 7,
    HotCard = 8, Voided = 9, Retrieval = 10, ChargedBack = 11, Error = 12, ValidationFailed = 13, Address = 14,
    VerificationCode = 15, FraudPrevention = 16, ForcedApproval = 17, ForcedDecline = 18, NoResponse = 19, BadResponse = 20
}
public enum PhoneNumberType { Other = 0, Home = 1, Work = 2, Mobile = 3, Fax = 4 }
public enum CommentType { Default = 0, Itinerary = 1, Manifest = 2, Alert = 3, Archive = 4 }
public enum DistributionOption { None = 0, Mail = 1, Email = 2, Fax = 3 }
public enum TaxesAndFeesRollupMode { None = 0, Taxes = 1, TaxesAndFees = 2 }

/// <summary>Token user type (blueprint §11: 1 Customer, 2 Agent, 3 Anonymous [INFERRED]).</summary>
public enum UserType { Customer = 1, Agent = 2, Anonymous = 3 }

/// <summary>Endpoint permission levels (Swagger tags Auth-None / Auth-Customer / Auth-Agent).</summary>
public enum PermissionLevel { Anonymous = 0, Customer = 1, Agent = 2 }
