using Application.Interfaces;
using Domain.Dtos;
using Domain.Enums;

namespace Application.Services;

public class UserConnectionService : IUserConnectionService
{
    // <key,value> = <username, ConnectionID>
    private static readonly Dictionary<string, string> Users = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, OnlineUserState> OnlineUsers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> PrivateRooms = new(StringComparer.OrdinalIgnoreCase);

    public bool AddUserToList(string userToAdd)
    {
        lock (Users)
        {
            if (Users.ContainsKey(userToAdd))
                return false;

            Users.Add(userToAdd, null!);
            AddUserToOnlineUserList(userToAdd);
            return true;
        }
    }

    public void AddUserToOnlineUserList(string userToAdd)
    {
        lock (OnlineUsers)
        {
            OnlineUsers[userToAdd] = new OnlineUserState();
        }
    }

    public void SetOnlineUserInPrivateRoom(string user)
    {
        lock (OnlineUsers)
        {
            if (OnlineUsers.TryGetValue(user, out var state))
            {
                state.IsInPrivateRoom = true;
                state.StatusId = (int)UserAvailabilityStatus.Playing;
            }
        }
    }

    public void SetOnlineUserOutPrivateRoom(string user)
    {
        lock (OnlineUsers)
        {
            if (OnlineUsers.TryGetValue(user, out var state))
            {
                state.IsInPrivateRoom = false;

                if (state.StatusId == (int)UserAvailabilityStatus.Playing)
                {
                    state.StatusId = (int)UserAvailabilityStatus.Available;
                }
            }
        }
    }

    public void SetUserStatus(string user, UserAvailabilityStatus status)
    {
        lock (OnlineUsers)
        {
            if (!OnlineUsers.TryGetValue(user, out var state))
            {
                state = new OnlineUserState();
                OnlineUsers[user] = state;
            }

            state.StatusId = (int)status;
            if (status != UserAvailabilityStatus.Playing)
            {
                state.IsInPrivateRoom = false;
            }
        }
    }

    public void RemoveOnlineUserFromList(string user)
    {
        lock (OnlineUsers)
        {
            if (OnlineUsers.ContainsKey(user)) OnlineUsers.Remove(user);
        }
    }

    public void RemoveUserFromList(string user)
    {
        lock (Users)
        {
            if (Users.ContainsKey(user))
            {
                Users.Remove(user);
                RemoveOnlineUserFromList(user);
            }
        }
    }

    public void AddUserConnectionId(string user, string connectionId)
    {
        lock (Users)
        {
            if (!Users.ContainsKey(user))
            {
                Users[user] = connectionId;
                AddUserToOnlineUserList(user);
            }
            else
            {
                Users[user] = connectionId;
            }
        }
    }

    public KeyValuePair<string, bool>[] GetOnlineUsers()
    {
        lock (OnlineUsers)
        {
            return OnlineUsers
                .Select(x => new KeyValuePair<string, bool>(x.Key, x.Value.IsInPrivateRoom))
                .ToArray();
        }
    }

    public OnlineUserDto[] GetOnlineUsersWithStatus()
    {
        lock (OnlineUsers)
        {
            return OnlineUsers
                .Select(x =>
                {
                    var status = UserAvailabilityStatusExtensions.FromId(x.Value.StatusId);
                    return new OnlineUserDto
                    {
                        Username = x.Key,
                        IsInPrivateRoom = x.Value.IsInPrivateRoom,
                        StatusId = x.Value.StatusId,
                        Status = status.ToDisplayName()
                    };
                })
                .OrderBy(x => x.Username)
                .ToArray();
        }
    }

    public string? GetUserConnectionById(string connectionId)
    {
        lock (Users)
        {
            return Users.Where(x => x.Value == connectionId).Select(x => x.Key).FirstOrDefault();
        }
    }

    public string? GetUserConnectionByUser(string user)
    {
        lock (Users)
        {
            return Users.Where(x => x.Key == user).Select(x => x.Value).FirstOrDefault();
        }
    }

    public void SetPrivateRoom(string key, string[] users)
    {
        lock (PrivateRooms)
        {
            if (!PrivateRooms.ContainsKey(key))
                PrivateRooms[key] = new List<string>();

            foreach (var user in users) PrivateRooms[key].Add(user);
        }
    }

    public void RemovePrivateRoom(string key)
    {
        lock (PrivateRooms)
        {
            if (PrivateRooms.ContainsKey(key)) PrivateRooms.Remove(key);
        }
    }

    public string[] RemoveUserFromPrivateRoom(string user)
    {
        var affectedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (PrivateRooms)
        {
            foreach (var room in PrivateRooms.Keys.ToList())
            {
                if (PrivateRooms[room].Contains(user))
                {
                    foreach (var item in PrivateRooms[room])
                    {
                        SetOnlineUserOutPrivateRoom(item);
                        affectedUsers.Add(item);
                    }

                    PrivateRooms.Remove(room);
                }
            }
        }

        return affectedUsers.ToArray();
    }

    public bool IsUserInPrivateRoom(string user)
    {
        lock (OnlineUsers)
        {
            return OnlineUsers.TryGetValue(user, out var state) && state.IsInPrivateRoom;
        }
    }

    public UserAvailabilityStatus GetUserStatus(string user)
    {
        lock (OnlineUsers)
        {
            return OnlineUsers.TryGetValue(user, out var state)
                ? UserAvailabilityStatusExtensions.FromId(state.StatusId)
                : UserAvailabilityStatus.Available;
        }
    }

    private sealed class OnlineUserState
    {
        public bool IsInPrivateRoom { get; set; }
        public int StatusId { get; set; } = (int)UserAvailabilityStatus.Available;
    }
}
