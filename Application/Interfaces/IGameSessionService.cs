using Domain.Dtos;

namespace Application.Interfaces;

public interface IGameSessionService
{
    Task<GameStateDto> CreateGameAsync(string roomName, string playerX, string playerO);
    Task<GameStateDto?> GetGameStateAsync(string roomName);
    Task<MoveAttemptResultDto> MakeMoveAsync(string roomName, string player, int position);
    Task<GameStateDto> RestartGameAsync(string roomName);
    Task CloseGameAsync(string roomName);
    Task CloseGamesForUserAsync(string user);
}
