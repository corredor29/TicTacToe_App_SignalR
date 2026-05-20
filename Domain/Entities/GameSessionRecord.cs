using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

public class GameSessionRecord
{
    [Key]
    public int Id { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string PlayerX { get; set; } = string.Empty;
    public string PlayerO { get; set; } = string.Empty;
    public string CurrentTurnUser { get; set; } = string.Empty;
    public string CurrentTurnSymbol { get; set; } = string.Empty;
    public string BoardState { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Winner { get; set; }
    public string? WinningSymbol { get; set; }
    public string WinningPositions { get; set; } = string.Empty;
    public string? LastMoveBy { get; set; }
    public int? LastMovePosition { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
