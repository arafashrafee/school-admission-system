namespace SchoolAdmissionSystem.Api.Models;

/// <summary>
/// Represents the lifecycle status of an admission hold.
/// </summary>
public enum HoldStatus
{
    /// <summary>
    /// Temporary reservation holding a seat pending confirmation.
    /// </summary>
    HELD,

    /// <summary>
    /// Successfully confirmed admission; permanently occupies a seat.
    /// </summary>
    CONFIRMED,

    /// <summary>
    /// Hold has passed its expiration time without confirmation; seat is released.
    /// </summary>
    EXPIRED,

    /// <summary>
    /// Hold was explicitly cancelled by the student or administrator; seat is released.
    /// </summary>
    CANCELLED
}
