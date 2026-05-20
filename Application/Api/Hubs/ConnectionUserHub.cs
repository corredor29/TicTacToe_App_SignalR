using Application.Interfaces;
using Domain.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

public class ConnectionUserHub : Hub
{
    private readonly IUserConnectionService _userConnectionService;

    public ConnectionUserHub(IUserConnectionService userConnectionService)
    {
        _userConnectionService = userConnectionService;
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
        }

        await DisplayOnlineUsers();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task AddUserConnectionId(string name)
    {
        _userConnectionService.AddUserConnectionId(name, Context.ConnectionId);
        await DisplayOnlineUsers();
    }

    public async Task DisplayOnlineUsers()
    {
        var onlineUsers = _userConnectionService.GetOnlineUsers();
        await Clients.Groups("TicTacToeHub").SendAsync("OnlineUsers", onlineUsers);
    }

    public async Task RequestPrivateRoom(MessageDto message)
    {
        var requestUserConnectionId = _userConnectionService.GetUserConnectionByUser(message.To!);
        if (requestUserConnectionId != null)
            await Clients.Client(requestUserConnectionId).SendAsync("RequestPrivateRoom", message);
    }

    public async Task RejectPrivateRoomRequest(MessageDto message)
    {
        if (message != null)
        {
            var requestUserConnectionId = _userConnectionService.GetUserConnectionByUser(message.To!);
            if (requestUserConnectionId != null)
                await Clients.Client(requestUserConnectionId).SendAsync("RejectPrivateRoomRequest", message);
        }
    }

    public async Task CreatePrivateRoom(MessageDto message)
    {
        if (message != null)
        {
            string privateRoomName = GetPrivateGroupRoom(message.From!, message.To!);
            await Groups.AddToGroupAsync(Context.ConnectionId, privateRoomName);

            var toConnectionId = _userConnectionService.GetUserConnectionByUser(message.To!);
            _userConnectionService.SetOnlineUserInPrivateRoom(message.From!);
            _userConnectionService.SetOnlineUserInPrivateRoom(message.To!);
            _userConnectionService.SetPrivateRoom(privateRoomName, new[] { message.From!, message.To! });

            await DisplayOnlineUsers();
            await Groups.AddToGroupAsync(toConnectionId!, privateRoomName);
            await Clients.Client(toConnectionId!).SendAsync("OpenPrivateRoom", message);
        }
    }

    public async Task ClosePrivateRoom(MessageDto message)
    {
        if (message != null)
        {
            string privateRoomName = GetPrivateGroupRoom(message.From!, message.To!);

            await Clients.Group(privateRoomName).SendAsync("ClosePrivateRoom", message);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, privateRoomName);

            _userConnectionService.SetOnlineUserOutPrivateRoom(message.From!);
            _userConnectionService.SetOnlineUserOutPrivateRoom(message.To!);
            _userConnectionService.RemovePrivateRoom(privateRoomName);

            if (message.Content == "ClosePrivateRoom")
                await DisplayOnlineUsers();

            var toConnectionId = _userConnectionService.GetUserConnectionByUser(message.To!);
            if (toConnectionId != null)
                await Groups.RemoveFromGroupAsync(toConnectionId, privateRoomName);
        }
    }

    public async Task SendPrivateRoomMessage(PrivateMessageDto message)
    {
        if (message != null)
        {
            string privateGroupName = GetPrivateGroupRoom(message.From!, message.To!);
            await Clients.Group(privateGroupName).SendAsync("NewPrivateMessage", message);
        }
    }

    private static string GetPrivateGroupRoom(string from, string to)
    {
        return string.CompareOrdinal(from, to) < 0 ? $"{from}-{to}" : $"{to}-{from}";
    }
}
