using AutoMapper;
using Microsoft.Extensions.Logging;
using TaskSphere.Application.DataTransferObjects.Company;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CompanyService> _logger;
    private readonly IMapper _mapper;

    public CompanyService(
        ICompanyRepository companyRepository,
        IUnitOfWork unitOfWork,
        ILogger<CompanyService> logger,
        IMapper mapper)
    {
        _companyRepository = companyRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _mapper = mapper;
    }
    
    public async Task<Result<CompanyDto>> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating Company: {Name}", dto.Name);
            
            var company = _mapper.Map<Company>(dto);

            await _companyRepository.AddAsync(company, cancellationToken);
            var saved = await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;

            if (!saved)
                return Result<CompanyDto>.Failure(EntityError.CreationFailed);

            _logger.LogInformation("Successfully created Company with Id: {Id}", company.Id);

            var createdCompany = await _companyRepository.GetByIdAsync(company.Id, cancellationToken);

            return Result<CompanyDto>.Success(
                _mapper.Map<CompanyDto>(createdCompany!));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Company: {Name}", dto.Name);
            return Result<CompanyDto>.Failure(EntityError.CreationUnexpectedError);
        }
    }
}