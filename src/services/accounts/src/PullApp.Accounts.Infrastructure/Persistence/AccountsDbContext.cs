using Microsoft.EntityFrameworkCore;
using PullApp.Accounts.Domain;

namespace PullApp.Accounts.Infrastructure.Persistence;

public class AccountsDbContext : DbContext
{
	public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
		: base(options)
	{ }

	public DbSet<User> Users
		=> Set<User>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<User>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedNever();
			
			entity.Property(e => e.Email).IsRequired();
			entity.HasIndex(e => e.Email).IsUnique();
            
			entity.Property(e => e.Bio)
				.HasDefaultValue(string.Empty)
				.HasMaxLength(500);
		});
	}
}
