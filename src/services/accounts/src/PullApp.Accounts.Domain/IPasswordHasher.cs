namespace PullApp.Accounts.Domain;

public interface IPasswordHasher
{
	public string Hash(string password);
	public bool Verify(string password, string hash);
}
