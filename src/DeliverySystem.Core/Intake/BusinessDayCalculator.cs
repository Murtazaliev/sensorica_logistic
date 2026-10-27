namespace DeliverySystem.Core.Intake;

public interface IBusinessDayCalculator
{
    DateOnly NextBusinessDay(DateOnly from);
}

public sealed class WeekendBusinessDayCalculator : IBusinessDayCalculator
{
    public DateOnly NextBusinessDay(DateOnly from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        return next;
    }
}
