namespace OpenOTSTray.Services;

public static class TtlFormatter
{
    public static string Format(int seconds)
    {
        if (seconds >= 86400 && seconds % 86400 == 0)
        {
            var days = seconds / 86400;
            return days == 1 ? "1 day" : $"{days} days";
        }
        if (seconds >= 3600 && seconds % 3600 == 0)
        {
            var hours = seconds / 3600;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        if (seconds >= 60 && seconds % 60 == 0)
        {
            var minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
