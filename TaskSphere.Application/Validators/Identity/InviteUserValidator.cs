using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

/// <summary>
/// CreateUser used to post a RegisterDto and inherit RegisterValidator. It posts an InviteUserDto
/// now, so without this the endpoint would accept an empty name and a malformed address.
/// </summary>
public class InviteUserValidator : AbstractValidator<InviteUserDto>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(50);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .Matches(@".+\@.+\..{2,}$")
            .WithMessage("Email must include a valid domain (e.g., name@example.com).");
    }
}
