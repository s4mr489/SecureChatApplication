using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data.Entities;
using SecureChatServer.Security;

namespace SecureChatServer.Data.Repositories;

/// <summary>
/// Repository implementation for user operations.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly SecureChatDbContext _context;

    public UserRepository(SecureChatDbContext context)
    {
        _context = context;
    }

    public async Task<UserEntity?> GetByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        return await _context.Users.FindAsync(id);
    }

    public async Task<UserEntity?> GetByConnectionIdAsync(string connectionId)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.ConnectionId == connectionId && u.IsOnline);
    }

    public async Task<List<UserEntity>> GetOnlineUsersAsync()
    {
        return await _context.Users
            .Where(u => u.IsOnline)
            .ToListAsync();
    }

    public async Task<List<string>> GetOnlineUsernamesAsync()
    {
        return await _context.Users
            .Where(u => u.IsOnline)
            .Select(u => u.Username)
            .ToListAsync();
    }

    public async Task<UserEntity> CreateOrUpdateOnJoinAsync(string username, string connectionId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null)
        {
            user = new UserEntity
            {
                Username = username,
                ConnectionId = connectionId,
                IsOnline = true,
                LastLoginAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
        }
        else
        {
            user.ConnectionId = connectionId;
            user.IsOnline = true;
            user.LastLoginAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return user;
    }

    public async Task SetOfflineAsync(string connectionId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.ConnectionId == connectionId);

        if (user != null)
        {
            user.IsOnline = false;
            user.ConnectionId = null;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> IsUsernameTakenAsync(string username)
    {
        return await _context.Users
            .AnyAsync(u => u.Username == username && u.IsOnline);
    }

    public async Task<string?> GetConnectionIdAsync(string username)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsOnline);
        return user?.ConnectionId;
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _context.Users.AnyAsync(u => u.Username == username);
    }

    public async Task<UserEntity> RegisterUserAsync(string username, string password, string connectionId)
    {
        if (await UsernameExistsAsync(username))
            throw new InvalidOperationException($"Username '{username}' is already taken.");

        var user = new UserEntity
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            ConnectionId = connectionId,
            IsOnline = true,
            LastLoginAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<UserEntity?> ValidateAndJoinAsync(string username, string password, string connectionId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user == null || user.PasswordHash == null)
            return null;

        if (!PasswordHasher.Verify(password, user.PasswordHash))
            return null;

        user.ConnectionId = connectionId;
        user.IsOnline = true;
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return user;
    }
}
