namespace PullApp.Accounts.Domain;

public class User
{
	public          Guid     Id             { get; set; } = Guid.NewGuid();
	public required string   Name           { get; set; }
	public required string   Surname        { get; set; }
	public required string   Email          { get; set; }
	public required string   PasswordHash   { get; set; } // TODO: delete setter?
	public          string?  ProfilePicture { get; set; } // URI
	public required DateOnly BirthDate      { get; set; }
	public          string   Bio            { get; set; } = string.Empty;
	public          UserRole Role           { get; set; } = UserRole.RegularUser;
}
