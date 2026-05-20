namespace Domain.Enums;

public enum UserAvailabilityStatus
{
    Available = 1,
    Playing = 2,
    DoNotDisturb = 3
}

public static class UserAvailabilityStatusExtensions
{
    public static string ToDisplayName(this UserAvailabilityStatus status)
    {
        return status switch
        {
            UserAvailabilityStatus.Available => "Disponible",
            UserAvailabilityStatus.Playing => "Jugando",
            UserAvailabilityStatus.DoNotDisturb => "No molestar",
            _ => "Desconocido"
        };
    }

    public static bool IsValid(int statusId)
    {
        return Enum.IsDefined(typeof(UserAvailabilityStatus), statusId);
    }

    public static UserAvailabilityStatus FromId(int statusId)
    {
        return IsValid(statusId)
            ? (UserAvailabilityStatus)statusId
            : UserAvailabilityStatus.Available;
    }
}
