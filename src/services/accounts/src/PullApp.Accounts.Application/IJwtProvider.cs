using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application; // TODO move to Abstractions?

public interface IJwtProvider
{
	string Generate(User user);
}
