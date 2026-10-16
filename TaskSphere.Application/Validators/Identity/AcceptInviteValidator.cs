using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

public class AcceptInviteValidator : AbstractValidator<AcceptInviteDto>
{
    public AcceptInviteValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();

        // Copied verbatim from RegisterValidator: a member setting their first password must
        // meet the same rule as someone who registered, or the two doors have different locks.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Matches(@"^(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,20}$")
            .WithMessage("Password must be 8-20 characters long, with at least one uppercase letter, one digit, and one special character.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required.")
            .Equal(x => x.Password).WithMessage("Passwords do not match.");
    }
}
