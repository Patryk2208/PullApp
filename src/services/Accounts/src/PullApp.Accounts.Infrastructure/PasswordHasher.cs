using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Infrastructure;

public class PasswordHasher : IPasswordHasher
{
	public string Hash(string password) => 
		BCrypt.Net.BCrypt.EnhancedHashPassword(password);

	public bool Verify(string password, string hash) => 
		BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
}