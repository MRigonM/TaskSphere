using FluentValidation;
using TaskSphere.Domain.DataTransferObjects.Identity;

namespace TaskSphere.Application.Validators.Identity;

public class EmailOnlyValidator : AbstractValidator<EmailOnlyDto>
{
    public EmailOnlyValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
