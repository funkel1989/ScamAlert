namespace ScamAlert.Api.Services.Pairing;

public sealed class PairingOptions
{
    public const string SectionName = "Pairing";

    public int CodeExpiryMinutes { get; set; } = 15;

    /// <summary>Maximum failed redeem attempts per IP per minute (enforced via rate limiting).</summary>
    public int RedeemPermitLimitPerMinute { get; set; } = 10;
}
