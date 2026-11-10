namespace SchoolAdmissionSystem.Api.Models;

/// <summary>
/// Represents an academic class or grade offering admission seats.
/// </summary>
public class AdmissionClass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique name of the class (e.g., "Class 1", "Grade 6").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of available admission seats. Must be strictly positive (> 0).
    /// </summary>
    public int TotalSeats { get; set; }

    /// <summary>
    /// Timestamp when this admission class was created (in UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation collection of holds associated with this class.
    /// </summary>
    public ICollection<AdmissionHold> Holds { get; set; } = new List<AdmissionHold>();
}
