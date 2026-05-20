using Domain.Enums;

namespace Domain.Dtos;

public class OnlineUserDto
{
    public string Username { get; set; } = string.Empty;
    public bool IsInPrivateRoom { get; set; }
    public int StatusId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class UpdateUserStatusDto
{
    public int StatusId { get; set; }
}

public class UserStatusDto
{
    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsInPrivateRoom { get; set; }
    public int StatusId { get; set; }
    public string Status { get; set; } = string.Empty;

    public static UserStatusDto Create(string username, bool isOnline, bool isInPrivateRoom, UserAvailabilityStatus status)
    {
        return new UserStatusDto
        {
            Username = username,
            IsOnline = isOnline,
            IsInPrivateRoom = isInPrivateRoom,
            StatusId = (int)status,
            Status = status.ToDisplayName()
        };
    }
}

public class RankingEntryDto
{
    public string Username { get; set; } = string.Empty;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int GamesPlayed { get; set; }
    public int Score { get; set; }
    public decimal WinRate { get; set; }
}
