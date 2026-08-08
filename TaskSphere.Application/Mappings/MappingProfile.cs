using TaskSphere.Domain.DataTransferObjects.Task;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using AutoMapper;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.DataTransferObjects.Company;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<AuditLog, AuditLogDto>();

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
        
        CreateMap<GitHubInstallation, GitHubInstallationDto>();
        CreateMap<GitHubRepository, GitHubRepositoryDto>();

        // FullName is flattened off the repository so the client gets something displayable
        // without a second round trip. Requires .Include(l => l.Repository) on the read path.
        // ForCtorParam, not ForMember: the DTO is a positional record, so AutoMapper binds
        // through the constructor and a ForMember on FullName would never run.
        CreateMap<ProjectRepositoryLink, ProjectRepositoryLinkDto>()
            .ForCtorParam("FullName", opt => opt.MapFrom(src =>
                src.Repository != null ? src.Repository.FullName : ""));

        CreateMap<TaskEntity, TaskDto>()
            .ForMember(dest => dest.Key, opt => opt.MapFrom(src =>
                TaskKeyFormatter.Format(src.ProjectId, src.Project, src.Number)));

        CreateMap<CreateTaskDto, TaskEntity>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore())
            .ForMember(dest => dest.Sprint, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAtUtc, opt => opt.Ignore())
            .ForMember(dest => dest.Number, opt => opt.Ignore());

        CreateMap<UpdateTaskDto, TaskEntity>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyId, opt => opt.Ignore())
            .ForMember(dest => dest.Company, opt => opt.Ignore())
            .ForMember(dest => dest.ProjectId, opt => opt.Ignore())
            .ForMember(dest => dest.Project, opt => opt.Ignore())
            .ForMember(dest => dest.Sprint, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAtUtc, opt => opt.Ignore())
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title.Trim()))
            .ForMember(dest => dest.Number, opt => opt.Ignore());
    }
}