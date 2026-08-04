namespace Domain.Enums;

/// <summary>
/// Bitmask of selected weekdays for RecurrenceFrequency.Custom (e.g. Mon/Wed/Fri).
/// Stored as a nullable int on RecurringTransaction — only populated when
/// Frequency == Custom, ignored otherwise.
/// </summary>
[Flags]
public enum RecurrenceDayOfWeek
{
    None      = 0,
    Monday    = 1,
    Tuesday   = 2,
    Wednesday = 4,
    Thursday  = 8,
    Friday    = 16,
    Saturday  = 32,
    Sunday    = 64
}
