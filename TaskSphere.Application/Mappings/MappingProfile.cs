using TaskSphere.Domain.DataTransferObjects.Task;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using AutoMapper;
using TaskSphere.Domain.DataTransferObjects.Company;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CompanyDto, Company>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());
        CreateMap<Company, CompanyDto>();

        CreateMap<Sprint, SprintDto>();

        CreateMap<CreateSprintDto, Sprint>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore());

        CreateMap<UpdateSprintDto, Sprint>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectId, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.Ignore());
        
        CreateMap<TaskEntity, TaskDto>();

        CreateMap<CreateTaskDto, TaskEntity>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore())
            .ForMember(dest => dest.Sprint, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAtUtc, opt => opt.Ignore());

        CreateMap<UpdateTaskDto, TaskEntity>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectId, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore())
            .ForMember(dest => dest.Sprint, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAtUtc, opt => opt.Ignore())
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title.Trim()));
    }
}