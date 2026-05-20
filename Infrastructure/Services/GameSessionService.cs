using Application.Interfaces;
using Domain.Dtos;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class GameSessionService : IGameSessionService
{
    private static readonly Dictionary<string, GameSession> Games = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDbContextFactory<TicTacToeDbContext> _dbContextFactory;

    public GameSessionService(IDbContextFactory<TicTacToeDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<GameStateDto> CreateGameAsync(string roomName, string playerX, string playerO)
    {
        GameStateDto state;

        lock (Games)
        {
            var game = new GameSession(roomName, playerX, playerO);
            Games[roomName] = game;
            state = game.ToDto();
        }

        await UpsertGameAsync(state, isActive: true);
        return state;
    }

    public async Task<GameStateDto?> GetGameStateAsync(string roomName)
    {
        lock (Games)
        {
            if (Games.TryGetValue(roomName, out var game))
            {
                return game.ToDto();
            }
        }

        var record = await GetLatestGameRecordAsync(roomName);
        if (record == null)
        {
            return null;
        }

        var restoredGame = GameSession.FromRecord(record);
        var state = restoredGame.ToDto();

        if (record.IsActive)
        {
            lock (Games)
            {
                Games[roomName] = restoredGame;
            }
        }

        return state;
    }

    public async Task<MoveAttemptResultDto> MakeMoveAsync(string roomName, string player, int position)
    {
        await EnsureGameLoadedAsync(roomName);

        MoveAttemptResultDto result;
        lock (Games)
        {
            if (!Games.TryGetValue(roomName, out var game))
            {
                return new MoveAttemptResultDto
                {
                    Success = false,
                    Error = "Game not found."
                };
            }

            result = game.MakeMove(player, position);
        }

        if (result.State != null)
        {
            await UpsertGameAsync(result.State, isActive: result.State.Status == "InProgress");
        }

        return result;
    }

    public async Task CloseGameAsync(string roomName)
    {
        lock (Games)
        {
            Games.Remove(roomName);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var record = await dbContext.GameSessions
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.RoomName == roomName && x.IsActive);

        if (record == null)
        {
            return;
        }

        record.IsActive = false;
        record.UpdatedAtUtc = DateTime.UtcNow;
        record.CompletedAtUtc ??= DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task CloseGamesForUserAsync(string user)
    {
        lock (Games)
        {
            var roomsToRemove = Games
                .Where(x => x.Value.HasPlayer(user))
                .Select(x => x.Key)
                .ToList();

            foreach (var room in roomsToRemove)
            {
                Games.Remove(room);
            }
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var records = await dbContext.GameSessions
            .Where(x => x.IsActive && (x.PlayerX == user || x.PlayerO == user))
            .ToListAsync();

        foreach (var record in records)
        {
            record.IsActive = false;
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.CompletedAtUtc ??= DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task EnsureGameLoadedAsync(string roomName)
    {
        lock (Games)
        {
            if (Games.ContainsKey(roomName))
            {
                return;
            }
        }

        var record = await GetLatestGameRecordAsync(roomName);
        if (record == null || !record.IsActive)
        {
            return;
        }

        var restoredGame = GameSession.FromRecord(record);
        lock (Games)
        {
            Games[roomName] = restoredGame;
        }
    }

    private async Task<GameSessionRecord?> GetLatestGameRecordAsync(string roomName)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.GameSessions
            .Where(x => x.RoomName == roomName)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
    }

    private async Task UpsertGameAsync(GameStateDto state, bool isActive)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var record = await dbContext.GameSessions
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.RoomName == state.RoomName && x.IsActive);

        var now = DateTime.UtcNow;
        if (record == null)
        {
            record = new GameSessionRecord
            {
                RoomName = state.RoomName,
                CreatedAtUtc = now
            };

            await dbContext.GameSessions.AddAsync(record);
        }

        record.PlayerX = state.PlayerX;
        record.PlayerO = state.PlayerO;
        record.CurrentTurnUser = state.CurrentTurnUser;
        record.CurrentTurnSymbol = state.CurrentTurnSymbol;
        record.BoardState = SerializeBoard(state.Board);
        record.Status = state.Status;
        record.Winner = state.Winner;
        record.WinningSymbol = state.WinningSymbol;
        record.WinningPositions = SerializePositions(state.WinningPositions);
        record.LastMoveBy = state.LastMoveBy;
        record.LastMovePosition = state.LastMovePosition;
        record.IsActive = isActive;
        record.UpdatedAtUtc = now;
        record.CompletedAtUtc = isActive ? null : now;

        await dbContext.SaveChangesAsync();
    }

    private static string SerializeBoard(IEnumerable<string> board)
    {
        return string.Concat(board.Select(cell => string.IsNullOrEmpty(cell) ? "." : cell));
    }

    private static string SerializePositions(IEnumerable<int> positions)
    {
        return string.Join(",", positions);
    }

    private static string[] DeserializeBoard(string boardState)
    {
        return boardState
            .Select(cell => cell == '.' ? string.Empty : cell.ToString())
            .ToArray();
    }

    private static int[] DeserializePositions(string positions)
    {
        if (string.IsNullOrWhiteSpace(positions))
        {
            return [];
        }

        return positions
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
    }

    private sealed class GameSession
    {
        private static readonly int[][] WinningLines =
        [
            [0, 1, 2],
            [3, 4, 5],
            [6, 7, 8],
            [0, 3, 6],
            [1, 4, 7],
            [2, 5, 8],
            [0, 4, 8],
            [2, 4, 6]
        ];

        public GameSession(string roomName, string playerX, string playerO)
        {
            RoomName = roomName;
            PlayerX = playerX;
            PlayerO = playerO;
            CurrentTurnUser = playerX;
            CurrentTurnSymbol = "X";
            Status = "InProgress";
            Board = Enumerable.Repeat(string.Empty, 9).ToArray();
        }

        public string RoomName { get; }
        public string PlayerX { get; }
        public string PlayerO { get; }
        public string CurrentTurnUser { get; private set; }
        public string CurrentTurnSymbol { get; private set; }
        public string[] Board { get; }
        public string Status { get; private set; }
        public string? Winner { get; private set; }
        public string? WinningSymbol { get; private set; }
        public int[] WinningPositions { get; private set; } = [];
        public string? LastMoveBy { get; private set; }
        public int? LastMovePosition { get; private set; }

        public static GameSession FromRecord(GameSessionRecord record)
        {
            var session = new GameSession(record.RoomName, record.PlayerX, record.PlayerO)
            {
                CurrentTurnUser = record.CurrentTurnUser,
                CurrentTurnSymbol = record.CurrentTurnSymbol,
                Status = record.Status,
                Winner = record.Winner,
                WinningSymbol = record.WinningSymbol,
                WinningPositions = DeserializePositions(record.WinningPositions),
                LastMoveBy = record.LastMoveBy,
                LastMovePosition = record.LastMovePosition
            };

            var board = DeserializeBoard(record.BoardState);
            for (var i = 0; i < board.Length; i++)
            {
                session.Board[i] = board[i];
            }

            return session;
        }

        public bool HasPlayer(string user)
        {
            return string.Equals(PlayerX, user, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(PlayerO, user, StringComparison.OrdinalIgnoreCase);
        }

        public MoveAttemptResultDto MakeMove(string player, int position)
        {
            if (Status != "InProgress")
            {
                return Fail("The game is already finished.");
            }

            if (!HasPlayer(player))
            {
                return Fail("The player is not part of this game.");
            }

            if (!string.Equals(CurrentTurnUser, player, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("It is not your turn.");
            }

            if (position < 0 || position > 8)
            {
                return Fail("Invalid board position.");
            }

            if (!string.IsNullOrEmpty(Board[position]))
            {
                return Fail("That cell is already occupied.");
            }

            var symbol = GetSymbol(player);
            Board[position] = symbol;
            LastMoveBy = player;
            LastMovePosition = position;

            if (TryResolveWinner(symbol, player))
            {
                return Succeed();
            }

            if (Board.All(x => !string.IsNullOrEmpty(x)))
            {
                Status = "Draw";
                CurrentTurnUser = string.Empty;
                CurrentTurnSymbol = string.Empty;
                return Succeed();
            }

            CurrentTurnUser = string.Equals(player, PlayerX, StringComparison.OrdinalIgnoreCase) ? PlayerO : PlayerX;
            CurrentTurnSymbol = CurrentTurnUser == PlayerX ? "X" : "O";

            return Succeed();
        }

        public GameStateDto ToDto()
        {
            return new GameStateDto
            {
                RoomName = RoomName,
                PlayerX = PlayerX,
                PlayerO = PlayerO,
                CurrentTurnUser = CurrentTurnUser,
                CurrentTurnSymbol = CurrentTurnSymbol,
                Board = [.. Board],
                Status = Status,
                Winner = Winner,
                WinningSymbol = WinningSymbol,
                WinningPositions = [.. WinningPositions],
                LastMoveBy = LastMoveBy,
                LastMovePosition = LastMovePosition
            };
        }

        private bool TryResolveWinner(string symbol, string player)
        {
            foreach (var line in WinningLines)
            {
                if (line.All(index => Board[index] == symbol))
                {
                    Status = "Won";
                    Winner = player;
                    WinningSymbol = symbol;
                    WinningPositions = [.. line];
                    CurrentTurnUser = string.Empty;
                    CurrentTurnSymbol = string.Empty;
                    return true;
                }
            }

            return false;
        }

        private string GetSymbol(string player)
        {
            return string.Equals(player, PlayerX, StringComparison.OrdinalIgnoreCase) ? "X" : "O";
        }

        private MoveAttemptResultDto Fail(string error)
        {
            return new MoveAttemptResultDto
            {
                Success = false,
                Error = error,
                State = ToDto()
            };
        }

        private MoveAttemptResultDto Succeed()
        {
            return new MoveAttemptResultDto
            {
                Success = true,
                State = ToDto()
            };
        }
    }
}
