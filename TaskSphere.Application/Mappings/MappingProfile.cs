using AutoMapper;
using TaskSphere.Application.DataTransferObjects.Company;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CompanyDto, Company>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        CreateMap<Company, CompanyDto>();
    }
}