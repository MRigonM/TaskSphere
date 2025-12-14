using FluentValidation;
using TaskSphere.Application.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

public class RegisterValidator  : AbstractValidator<RegisterDto>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(50);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Matches(@"^(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,20}$")
            .WithMessage("Password must be 8-20 characters long, with at least one uppercase letter, one digit, and one special character.");
        
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}