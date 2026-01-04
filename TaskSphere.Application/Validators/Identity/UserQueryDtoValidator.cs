using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

public class UserQueryDtoValidator : AbstractValidator<UserQueryDto>
{
    public UserQueryDtoValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Name)
            .MaximumLength(50).WithMessage("Name filter must be at most 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));

        RuleFor(x => x.Email)
            .MaximumLength(100).WithMessage("Email filter must be at most 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}