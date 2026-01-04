using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Sprint;

namespace TaskSphere.Application.Validators.Sprint;

public class CreateSprintValidator : AbstractValidator<CreateSprintDto>
{
    public CreateSprintValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100);

        RuleFor(x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId is required.");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("StartDate is required.");

        RuleFor(x => x.EndDate)
            .NotEmpty().WithMessage("EndDate is required.")
            .GreaterThan(x => x.StartDate).WithMessage("EndDate must be after StartDate.");
    }
}