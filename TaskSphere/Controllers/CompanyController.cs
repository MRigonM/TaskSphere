using Microsoft.AspNetCore.Authorization;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.Company)]
[RequireCompany]
public class CompanyController : ApiBaseController
{
    private readonly ICompanyService _companyService;

    public CompanyController(ICompanyService companyService)
    {
        _companyService = companyService;
    }
}