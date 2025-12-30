using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

public class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    private const string PasswordPattern =
        @"^(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,20}$";

    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(50).WithMessage("Name must be at most 50 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.")
            .MaximumLength(100).WithMessage("Email must be at most 100 characters.");

        When(x => !string.IsNullOrWhiteSpace(x.NewPassword) || !string.IsNullOrWhiteSpace(x.ConfirmNewPassword), () =>
        {
            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("NewPassword is required.")
                .Matches(PasswordPattern)
                .WithMessage("Password must be 8-20 characters long, with at least one uppercase letter, one digit, and one special character.");

            RuleFor(x => x.ConfirmNewPassword)
                .NotEmpty().WithMessage("ConfirmNewPassword is required.")
                .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
        });
    }
}