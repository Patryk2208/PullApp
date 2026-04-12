namespace PullApp.Accounts.Domain;

public class User
{
	public          int      Id             { get; set; }
	public required string   Name           { get; set; }
	public required string   Surname        { get; set; }
	public required string   Email          { get; set; }
	public required byte[]   PasswordHash   { get; set; }
	public          string?  ProfilePicture { get; set; } // URI
	public required DateOnly BirthDate      { get; set; }
	public          string   Bio            { get; set; } = string.Empty;
	public          UserRole Role           { get; set; } = UserRole.RegularUser;
}
