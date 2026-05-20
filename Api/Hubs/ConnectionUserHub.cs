using Application.Interfaces;
using Domain.Dtos;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

[Authorize]
public class ConnectionUserHub : Hub
{
    private readonly IUserConnectionService _userConnectionService;
    private readonly IGameSessionService _gameSessionService;
    private readonly IDbContextFactory<TicTacToeDbContext> _dbContextFactory;

    public ConnectionUserHub(
        IUserConnectionService userConnectionService,
        IGameSessionService gameSessionService,
        IDbContextFactory<TicTacToeDbContext> dbContextFactory)
    {
        _userConnectionService = userConnectionService;
        _gameSessionService = gameSessionService;
        _dbContextFactory = dbContextFactory;
    }

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "TicTacToeHub");
        await Clients.Caller.SendAsync("UserConnected");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "TicTacToeHub");

        var user = _userConnectionService.GetUserConnectionById(Context.ConnectionId);
        if (user != null)
        {
            _userConnectionService.RemoveUserFromList(user);
            _userConnectionService.RemoveOnlineUserFromList(user);
            var affectedUsers = _userConnectionService.RemoveUserFromPrivateRoom(user);
            await _gameSessionService.CloseGamesForUserAsync(user);
            await SetUsersStatusAsync(
                affectedUsers.Where(x => !string.Equals(x, user, StringComparison.OrdinalIgnoreCase)).ToArray(),
                UserAvailabilityStatus.Available);
        }

        await DisplayOnlineUsers();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task AddUserConnectionId(string name)
    {
        var username = GetCurrentUsername();
        if (username == null)
        {
            throw new HubException("Unauthorized user.");
        }

        _userConnectionService.AddUserConnectionId(username, Context.ConnectionId);
        await EnsurePersistedUserStatusAsync(username);
        await DisplayOnlineUsers();
    }

    public async Task DisplayOnlineUsers()
    {
        var onlineUsers = _userConnectionService.GetOnlineUsers();
        var onlineUsersWithStatus = _userConnectionService.GetOnlineUsersWithStatus();
        await Clients.Groups("TicTacToeHub").SendAsync("OnlineUsers", onlineUsers);
        await Clients.Groups("TicTacToeHub").SendAsync("OnlineUsersPresenceUpdated", onlineUsersWithStatus);
    }

    public async Task RequestPrivateRoom(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || string.IsNullOrWhiteSpace(message?.To))
        {
            throw new HubException("Invalid room request.");
        }

        message.From = username;
        ValidateUsersCanPlay(username, message.To);
        var requestUserConnectionId = _userConnectionService.GetUserConnectionByUser(message.To);
        if (requestUserConnectionId != null)
            await Clients.Client(requestUserConnectionId).SendAsync("RequestPrivateRoom", message);
    }

    public async Task RejectPrivateRoomRequest(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || message == null || string.IsNullOrWhiteSpace(message.To))
        {
            throw new HubException("Invalid room rejection.");
        }

        message.From = username;
        var requestUserConnectionId = _userConnectionService.GetUserConnectionByUser(message.To);
        if (requestUserConnectionId != null)
            await Clients.Client(requestUserConnectionId).SendAsync("RejectPrivateRoomRequest", message);
    }

    public async Task CreatePrivateRoom(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || message == null || string.IsNullOrWhiteSpace(message.To))
        {
            throw new HubException("Invalid private room.");
        }

        string privateRoomName = GetPrivateGroupRoom(username, message.To);
        await Groups.AddToGroupAsync(Context.ConnectionId, privateRoomName);

        var toConnectionId = _userConnectionService.GetUserConnectionByUser(message.To);
        if (toConnectionId == null)
        {
            throw new HubException("Target user is not connected.");
        }

        message.From = username;
        ValidateUsersCanPlay(username, message.To);
        _userConnectionService.SetOnlineUserInPrivateRoom(username);
        _userConnectionService.SetOnlineUserInPrivateRoom(message.To);
        await SetUsersStatusAsync([username, message.To], UserAvailabilityStatus.Playing);
        _userConnectionService.SetPrivateRoom(privateRoomName, new[] { username, message.To });
        var gameState = await _gameSessionService.CreateGameAsync(privateRoomName, username, message.To);

        await DisplayOnlineUsers();
        await Groups.AddToGroupAsync(toConnectionId, privateRoomName);
        await Clients.Client(toConnectionId).SendAsync("OpenPrivateRoom", message);
        await Clients.Group(privateRoomName).SendAsync("GameStateUpdated", gameState);
    }

    public async Task ClosePrivateRoom(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || message == null || string.IsNullOrWhiteSpace(message.To))
        {
            throw new HubException("Invalid private room close request.");
        }

        message.From = username;
        string privateRoomName = GetPrivateGroupRoom(username, message.To);

        await Clients.Group(privateRoomName).SendAsync("ClosePrivateRoom", message);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, privateRoomName);

        _userConnectionService.SetOnlineUserOutPrivateRoom(username);
        _userConnectionService.SetOnlineUserOutPrivateRoom(message.To);
        await SetUsersStatusAsync([username, message.To], UserAvailabilityStatus.Available);
        _userConnectionService.RemovePrivateRoom(privateRoomName);
        await _gameSessionService.CloseGameAsync(privateRoomName);

        if (message.Content == "ClosePrivateRoom")
            await DisplayOnlineUsers();

        var toConnectionId = _userConnectionService.GetUserConnectionByUser(message.To);
        if (toConnectionId != null)
            await Groups.RemoveFromGroupAsync(toConnectionId, privateRoomName);
    }

    public async Task SendPrivateRoomMessage(PrivateMessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || message == null || string.IsNullOrWhiteSpace(message.To))
        {
            throw new HubException("Invalid move message.");
        }

        message.From = username;
        string privateGroupName = GetPrivateGroupRoom(username, message.To);
        var moveResult = await _gameSessionService.MakeMoveAsync(privateGroupName, username, message.Position);

        if (!moveResult.Success)
        {
            await Clients.Caller.SendAsync("GameError", moveResult.Error, moveResult.State);
            return;
        }

        await Clients.Group(privateGroupName).SendAsync("NewPrivateMessage", message);
        await Clients.Group(privateGroupName).SendAsync("GameStateUpdated", moveResult.State);
    }

    public async Task GetCurrentGameState(string otherUser)
    {
        var username = GetCurrentUsername();
        if (username == null || string.IsNullOrWhiteSpace(otherUser))
        {
            throw new HubException("Invalid game state request.");
        }

        string privateGroupName = GetPrivateGroupRoom(username, otherUser);
        var gameState = await _gameSessionService.GetGameStateAsync(privateGroupName);
        if (gameState == null)
        {
            await Clients.Caller.SendAsync("GameError", "Game not found.", null);
            return;
        }

        await Clients.Caller.SendAsync("GameStateUpdated", gameState);
    }

    public async Task SetAvailabilityStatus(int statusId)
    {
        var username = GetCurrentUsername();
        if (username == null)
        {
            throw new HubException("Unauthorized user.");
        }

        if (!UserAvailabilityStatusExtensions.IsValid(statusId))
        {
            throw new HubException("Invalid status. Use 1=Disponible, 2=Jugando, 3=No molestar.");
        }

        if (_userConnectionService.IsUserInPrivateRoom(username) && statusId != (int)UserAvailabilityStatus.Playing)
        {
            throw new HubException("No puedes cambiar el estado mientras la partida sigue activa.");
        }

        var status = (UserAvailabilityStatus)statusId;
        _userConnectionService.SetUserStatus(username, status);
        await SetUsersStatusAsync([username], status);
        await DisplayOnlineUsers();
    }

    public async Task RequestRematch(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || message == null || string.IsNullOrWhiteSpace(message.To))
        {
            throw new HubException("Invalid rematch request.");
        }

        message.From = username;
        var requestUserConnectionId = _userConnectionService.GetUserConnectionByUser(message.To);
        if (requestUserConnectionId == null)
        {
            throw new HubException("Target user is not connected.");
        }

        await Clients.Client(requestUserConnectionId).SendAsync("RematchRequested", message);
    }

    public async Task RespondRematch(RematchDecisionDto decision)
    {
        var username = GetCurrentUsername();
        if (username == null || decision == null || string.IsNullOrWhiteSpace(decision.To))
        {
            throw new HubException("Invalid rematch response.");
        }

        decision.From = username;
        var roomName = GetPrivateGroupRoom(username, decision.To);
        var otherConnectionId = _userConnectionService.GetUserConnectionByUser(decision.To);
        if (otherConnectionId == null)
        {
            throw new HubException("Target user is not connected.");
        }

        var currentState = await _gameSessionService.GetGameStateAsync(roomName);
        if (currentState == null)
        {
            throw new HubException("Game not found.");
        }

        if (currentState.Status == "InProgress")
        {
            throw new HubException("La partida actual todavía no termina.");
        }

        if (!decision.Accepted)
        {
            await Clients.Client(otherConnectionId).SendAsync("RematchRejected", decision);
            return;
        }

        _userConnectionService.SetOnlineUserInPrivateRoom(username);
        _userConnectionService.SetOnlineUserInPrivateRoom(decision.To);
        await SetUsersStatusAsync([username, decision.To], UserAvailabilityStatus.Playing);
        var restartedGame = await _gameSessionService.RestartGameAsync(roomName);

        await Groups.AddToGroupAsync(otherConnectionId, roomName);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).SendAsync("RematchAccepted", decision);
        await Clients.Group(roomName).SendAsync("GameStateUpdated", restartedGame);
        await DisplayOnlineUsers();
    }

    private static string GetPrivateGroupRoom(string from, string to)
    {
        return string.CompareOrdinal(from, to) < 0 ? $"{from}-{to}" : $"{to}-{from}";
    }

    private void ValidateUsersCanPlay(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException("No puedes iniciar una partida contigo mismo.");
        }

        var targetConnectionId = _userConnectionService.GetUserConnectionByUser(to);
        if (string.IsNullOrWhiteSpace(targetConnectionId))
        {
            throw new HubException("Target user is not connected.");
        }

        var targetStatus = _userConnectionService.GetUserStatus(to);
        if (targetStatus == UserAvailabilityStatus.DoNotDisturb)
        {
            throw new HubException("El usuario no molestar no puede recibir invitaciones.");
        }

        if (_userConnectionService.IsUserInPrivateRoom(to) || targetStatus == UserAvailabilityStatus.Playing)
        {
            throw new HubException("El usuario ya se encuentra jugando.");
        }
    }

    private async Task EnsurePersistedUserStatusAsync(string username)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (user == null)
        {
            return;
        }

        var status = UserAvailabilityStatusExtensions.FromId(user.StatusId);
        _userConnectionService.SetUserStatus(username, status);
    }

    private async Task SetUsersStatusAsync(IEnumerable<string> usernames, UserAvailabilityStatus status)
    {
        var normalizedUsers = usernames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedUsers.Length == 0)
        {
            return;
        }

        foreach (var username in normalizedUsers)
        {
            _userConnectionService.SetUserStatus(username, status);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var users = await dbContext.Users
            .Where(x => normalizedUsers.Contains(x.Username!))
            .ToListAsync();

        foreach (var user in users)
        {
            user.StatusId = (int)status;
        }

        await dbContext.SaveChangesAsync();
    }

    private string? GetCurrentUsername()
    {
        return Context.User?.Identity?.Name;
    }
}
