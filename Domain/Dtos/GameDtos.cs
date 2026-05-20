namespace Domain.Dtos;

public class GameStateDto
{
    public string RoomName { get; set; } = string.Empty;
    public string PlayerX { get; set; } = string.Empty;
    public string PlayerO { get; set; } = string.Empty;
    public string CurrentTurnUser { get; set; } = string.Empty;
    public string CurrentTurnSymbol { get; set; } = string.Empty;
    public string[] Board { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string? Winner { get; set; }
    public string? WinningSymbol { get; set; }
    public int[] WinningPositions { get; set; } = [];
    public string? LastMoveBy { get; set; }
    public int? LastMovePosition { get; set; }
}

public class MoveAttemptResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public GameStateDto? State { get; set; }
}
