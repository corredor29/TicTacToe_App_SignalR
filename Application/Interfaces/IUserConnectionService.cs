using Domain.Dtos;
using Domain.Enums;

namespace Application.Interfaces;

public interface IUserConnectionService
{
    bool AddUserToList(string userToAdd);
    void AddUserToOnlineUserList(string userToAdd);
    void SetOnlineUserInPrivateRoom(string user);
    void SetOnlineUserOutPrivateRoom(string user);
    void SetUserStatus(string user, UserAvailabilityStatus status);
    void RemoveOnlineUserFromList(string user);
    void RemoveUserFromList(string user);
    void AddUserConnectionId(string user, string connectionId);
    KeyValuePair<string, bool>[] GetOnlineUsers();
    OnlineUserDto[] GetOnlineUsersWithStatus();
    string? GetUserConnectionById(string connectionId);
    string? GetUserConnectionByUser(string user);
    void SetPrivateRoom(string key, string[] users);
    void RemovePrivateRoom(string key);
    string[] RemoveUserFromPrivateRoom(string user);
    bool IsUserInPrivateRoom(string user);
    UserAvailabilityStatus GetUserStatus(string user);
}
