using MediatR;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Application.GetUser;

public static class GetUser
{
	public record Query(string Email) : IRequest<Response>;
	
	public record Response(
		int      Id,            
		string   Name,          
		string   Surname,       
		string   Email,
		string?  ProfilePicture,
		DateOnly BirthDate,     
		string   Bio,           
		UserRole Role);

	public class Handler : IRequestHandler<Query, Response>
	{
		private readonly IUserRepository _userRepository;
		public Handler(IUserRepository userRepository)
			=> _userRepository = userRepository;
		
		public async Task<Response> Handle(Query query, CancellationToken ct)
		{
			var user = await _userRepository.GetByEmailAsync(query.Email, ct);
            
			return user is null
				? throw new KeyNotFoundException($"User with email {query.Email} not found.")
				: new Response(user.Id, user.Name, user.Surname, user.Email, user.ProfilePicture, user.BirthDate, user.Bio, user.Role);
		}
	}
}
