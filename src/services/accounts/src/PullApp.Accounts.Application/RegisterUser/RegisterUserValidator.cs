using FluentValidation;

namespace PullApp.Accounts.Application.RegisterUser;

public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        // TODO: Maybe localization?
        
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Imię jest wymagane.")
            .MaximumLength(50).WithMessage("Imię nie może przekraczać 50 znaków.");

        RuleFor(x => x.Surname)
            .NotEmpty().WithMessage("Nazwisko jest wymagane.")
            .MaximumLength(50).WithMessage("Nazwisko nie może przekraczać 50 znaków.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email jest wymagany.")
            .EmailAddress().WithMessage("To nie jest poprawny format adresu email.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Hasło nie może być puste.")
            .MinimumLength(8).WithMessage("Hasło musi mieć co najmniej 8 znaków.");
            // .Matches("[A-Z]").WithMessage("Hasło musi zawierać co najmniej jedną wielką literę.")
            // .Matches("[a-z]").WithMessage("Hasło musi zawierać co najmniej jedną małą literę.")
            // .Matches("[0-9]").WithMessage("Hasło musi zawierać co najmniej jedną cyfrę.")
            // .Matches(@"[\!\?\*\.]").WithMessage("Hasło musi zawierać znak specjalny (!, ?, *, .).");

        RuleFor(x => x.BirthDate)
            .NotEmpty().WithMessage("Data urodzenia jest wymagana.")
            .Must(BeAtLeast18).WithMessage("Musisz mieć ukończone 18 lat, aby zarejestrować się w PullApp.");
    }

    private bool BeAtLeast18(DateOnly birthDate)
    {
        // TODO: Maybe `ITimeProvider`?
        var today = DateOnly.FromDateTime(DateTime.Now);
        return birthDate.AddYears(18) <= today;
    }
}