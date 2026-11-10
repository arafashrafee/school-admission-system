namespace SchoolAdmissionSystem.Api.Models;

/// <summary>
/// Represents a seat reservation or confirmed admission for a student in a specific class.
/// </summary>
public class AdmissionHold
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identifier of the admission class where seat is held.
    /// </summary>
    public Guid ClassId { get; set; }

    /// <summary>
    /// Student identifier consisting of exactly 17 decimal digits with leading zeroes preserved.
    /// </summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>
    /// Current lifecycle status: HELD, CONFIRMED, EXPIRED, CANCELLED.
    /// </summary>
    public HoldStatus Status { get; set; } = HoldStatus.HELD;

    /// <summary>
    /// Timestamp when this hold was created (in UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this reservation expires unless confirmed (in UTC).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Timestamp when this hold was confirmed (in UTC). Null until confirmed.
    /// </summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Navigation reference to the associated class.
    /// </summary>
    public AdmissionClass? Class { get; set; }
}
