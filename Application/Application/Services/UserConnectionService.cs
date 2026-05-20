using Application.Interfaces;

namespace Application.Services;

public class UserConnectionService : IUserConnectionService
{
    // <key,value> = <username, ConnectionID>
    private static readonly Dictionary<string, string> Users = new();
    private static readonly Dictionary<string, bool> OnlineUsers = new();
    private static readonly Dictionary<string, List<string>> PrivateRooms = new();

    public bool AddUserToList(string userToAdd)
    {
        lock (Users)
        {
            foreach (var user in Users)
                if (user.Key.ToLower() == userToAdd.ToLower())
                    return false;

            Users.Add(userToAdd, null!);
            AddUserToOnlineUserList(userToAdd);
            return true;
        }
    }

    public void AddUserToOnlineUserList(string userToAdd)
    {
        lock (OnlineUsers) { OnlineUsers.Add(userToAdd, false); }
    }

    public void SetOnlineUserInPrivateRoom(string user)
    {
        lock (OnlineUsers)
        {
            if (OnlineUsers.ContainsKey(user)) OnlineUsers[user] = true;
        }
    }

    public void SetOnlineUserOutPrivateRoom(string user)
    {
        lock (OnlineUsers)
        {
            if (OnlineUsers.ContainsKey(user)) OnlineUsers[user] = false;
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
            if (Users.ContainsKey(user)) Users[user] = connectionId;
        }
    }

    public KeyValuePair<string, bool>[] GetOnlineUsers()
    {
        lock (OnlineUsers) { return OnlineUsers.ToArray(); }
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
        if (!PrivateRooms.ContainsKey(key))
            PrivateRooms[key] = new List<string>();

        lock (PrivateRooms)
        {
            foreach (var user in users) PrivateRooms[key].Add(user);
        }
    }

    public void RemovePrivateRoom(string key)
    {
        if (PrivateRooms.ContainsKey(key)) PrivateRooms.Remove(key);
    }

    public void RemoveUserFromPrivateRoom(string user)
    {
        foreach (var room in PrivateRooms.Keys.ToList())
        {
            if (PrivateRooms[room].Contains(user))
            {
                foreach (var item in PrivateRooms[room])
                    SetOnlineUserOutPrivateRoom(item);

                PrivateRooms.Remove(room);
            }
        }
    }
}
