using Microsoft.EntityFrameworkCore;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Infrastructure;

public class UserRepository : IUserRepository
{
	private readonly AccountsDbContext _context;

	public UserRepository(AccountsDbContext context)
		=> _context = context;

	public async Task AddAsync(User user, CancellationToken ct)
	{
		await _context.Users.AddAsync(user, ct);
		await _context.SaveChangesAsync(ct);
	}

	public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
	{
		return await _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
	}
}
