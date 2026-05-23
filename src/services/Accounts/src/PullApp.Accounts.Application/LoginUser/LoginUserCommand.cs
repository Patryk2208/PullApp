using MediatR;

namespace PullApp.Accounts.Application.LoginUser;

public record class LoginUserResponse(
	string Token);

public record class LoginUserCommand(
	string Email,
	string Password) 
	: IRequest<LoginUserResponse>;
