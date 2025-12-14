using TaskSphere.Application.Interfaces;

namespace TaskSphere.Controllers;

public class CompanyController : ApiBaseController
{
    private readonly ICompanyService _companyService;

    public CompanyController(ICompanyService companyService)
    {
        _companyService = companyService;
    }
}