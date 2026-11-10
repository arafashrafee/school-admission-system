namespace SchoolAdmissionSystem.Api.Configuration;

/// <summary>
/// Operational settings for seat holds and admissions.
/// </summary>
public class AdmissionOptions
{
    public const string SectionName = "Admission";

    /// <summary>
    /// Duration in seconds a hold remains valid before expiring (default: 600s / 10 minutes).
    /// </summary>
    public int HoldDurationSeconds { get; set; } = 600;

    /// <summary>
    /// Interval in seconds between background expiration scans (default: 15s).
    /// </summary>
    public int ExpirationSweepIntervalSeconds { get; set; } = 15;
}
