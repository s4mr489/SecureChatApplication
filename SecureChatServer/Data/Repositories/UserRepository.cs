using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data.Entities;

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
            // Create new user
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
            // Update existing user
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
}
