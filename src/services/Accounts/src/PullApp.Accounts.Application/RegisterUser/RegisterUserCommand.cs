using MediatR;

namespace PullApp.Accounts.Application.RegisterUser;

public record class RegisterUserResponse(
	int UserId);

public record class RegisterUserCommand(
	string Name,
	string Surname,
	string Email,
	string Password,
	DateOnly BirthDate)
	: IRequest<RegisterUserResponse>;
