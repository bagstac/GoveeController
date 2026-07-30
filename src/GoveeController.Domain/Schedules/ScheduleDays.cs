namespace GoveeController.Domain.Schedules;

/// <summary>
/// Bitmask of the days of the week a recurring <see cref="Schedule"/> fires on. A value of
/// <see cref="None"/> is not "no days" for a recurring schedule — it is the discriminator that
/// marks a <see cref="Schedule"/> as one-time instead (see <see cref="Schedule.DaysOfWeekMask"/>).
/// Each bit is <c>1 &lt;&lt; (int)DayOfWeek</c>, so converting to/from <see cref="DateTime.DayOfWeek"/>
/// is a shift rather than a lookup table.
/// </summary>
[Flags]
public enum ScheduleDays
{
    /// <summary>No recurring days selected. Marks a <see cref="Schedule"/> as one-time.</summary>
    None = 0,

    /// <summary>Sunday.</summary>
    Sunday = 1 << 0,

    /// <summary>Monday.</summary>
    Monday = 1 << 1,

    /// <summary>Tuesday.</summary>
    Tuesday = 1 << 2,

    /// <summary>Wednesday.</summary>
    Wednesday = 1 << 3,

    /// <summary>Thursday.</summary>
    Thursday = 1 << 4,

    /// <summary>Friday.</summary>
    Friday = 1 << 5,

    /// <summary>Saturday.</summary>
    Saturday = 1 << 6
}
