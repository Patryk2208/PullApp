namespace PullApp.Accounts.Domain;

public interface IUserRepository
{
	public Task AddAsync(User user, CancellationToken ct);
	public Task<bool> IsEmailUniqueAsync(string email, CancellationToken ct);
	public Task<User?> GetByEmailAsync(string email, CancellationToken ct);
}
