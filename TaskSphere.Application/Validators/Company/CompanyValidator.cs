using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Company;

namespace TaskSphere.Application.Validators.Company;

public class CompanyValidator : AbstractValidator<CompanyDto>
{
    public CompanyValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MinimumLength(3).WithMessage("Company name must be at least 3 characters.")
            .MaximumLength(100).WithMessage("Company name must be at most 100 characters.")
            .Matches(@"^[\p{L}\p{N}][\p{L}\p{N}\s\.\-&']*$")
            .WithMessage("Company name contains invalid characters.");
    }
}