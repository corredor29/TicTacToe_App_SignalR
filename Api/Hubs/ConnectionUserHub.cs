using Application.Interfaces;
using Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

[Authorize]
public class ConnectionUserHub : Hub
{
    private readonly IUserConnectionService _userConnectionService;
    private readonly IGameSessionService _gameSessionService;

    public ConnectionUserHub(IUserConnectionService userConnectionService, IGameSessionService gameSessionService)
    {
        _userConnectionService = userConnectionService;
        _gameSessionService = gameSessionService;
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
            _userConnectionService.RemoveUserFromPrivateRoom(user);
            await _gameSessionService.CloseGamesForUserAsync(user);
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
        await DisplayOnlineUsers();
    }

    public async Task DisplayOnlineUsers()
    {
        var onlineUsers = _userConnectionService.GetOnlineUsers();
        await Clients.Groups("TicTacToeHub").SendAsync("OnlineUsers", onlineUsers);
    }

    public async Task RequestPrivateRoom(MessageDto message)
    {
        var username = GetCurrentUsername();
        if (username == null || string.IsNullOrWhiteSpace(message?.To))
        {
            throw new HubException("Invalid room request.");
        }

        message.From = username;
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
        _userConnectionService.SetOnlineUserInPrivateRoom(username);
        _userConnectionService.SetOnlineUserInPrivateRoom(message.To);
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

    private static string GetPrivateGroupRoom(string from, string to)
    {
        return string.CompareOrdinal(from, to) < 0 ? $"{from}-{to}" : $"{to}-{from}";
    }

    private string? GetCurrentUsername()
    {
        return Context.User?.Identity?.Name;
    }
}
