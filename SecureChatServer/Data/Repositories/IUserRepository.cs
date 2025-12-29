using SecureChatServer.Data.Entities;

namespace SecureChatServer.Data.Repositories;

/// <summary>
/// Repository interface for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by username.
    /// </summary>
    Task<UserEntity?> GetByUsernameAsync(string username);

    /// <summary>
    /// Gets a user by ID.
    /// </summary>
    Task<UserEntity?> GetByIdAsync(int id);

    /// <summary>
    /// Gets all online users.
    /// </summary>
    Task<List<UserEntity>> GetOnlineUsersAsync();

    /// <summary>
    /// Gets all usernames of online users.
    /// </summary>
    Task<List<string>> GetOnlineUsernamesAsync();

    /// <summary>
    /// Creates or updates a user when they join.
    /// </summary>
    Task<UserEntity> CreateOrUpdateOnJoinAsync(string username, string connectionId);

    /// <summary>
    /// Marks a user as offline when they disconnect.
    /// </summary>
    Task SetOfflineAsync(string connectionId);

    /// <summary>
    /// Checks if a username is currently taken by an online user.
    /// </summary>
    Task<bool> IsUsernameTakenAsync(string username);

    /// <summary>
    /// Gets the connection ID for a username.
    /// </summary>
    Task<string?> GetConnectionIdAsync(string username);
}
