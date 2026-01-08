using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Task;

namespace TaskSphere.Application.Validators.Task;

public class UpdateTaskValidator : AbstractValidator<UpdateTaskDto>
{
    private static readonly string[] AllowedStatuses = { "Open", "InProgress", "Blocked", "Done" };
    private static readonly string[] AllowedPriorities = { "Low", "Medium", "High", "Critical" };

    public UpdateTaskValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(5000);

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

        RuleFor(x => x.Priority)
            .Must(p => p == null || AllowedPriorities.Contains(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", AllowedPriorities)}.");

        RuleFor(x => x.StoryPoints)
            .InclusiveBetween(0, 100)
            .When(x => x.StoryPoints.HasValue);
    }
}