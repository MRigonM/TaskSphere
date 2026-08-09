using AutoMapper;

namespace TaskSphere.Application.Mappings;

public static class MappingExtensions
{
    private static readonly string[] Lifecycle =
        ["Id", "IsDeleted", "DeletedAt", "CreatedAtUtc", "UpdatedAtUtc"];

    /// <summary>
    /// Ignores the <c>BaseEntity&lt;T&gt;</c> lifecycle members on a DTO -> entity map. A DTO has
    /// no business setting an entity's identity, soft-delete tombstone, or timestamps.
    /// </summary>
    /// <remarks>
    /// Applied per map rather than through a profile-wide ForAllMaps convention on purpose: a
    /// new map that forgets to call this still fails AssertConfigurationIsValid(), which is the
    /// entire point of having that assertion. A convention would silence such a map instead of
    /// flagging it.
    /// </remarks>
    public static IMappingExpression<TSource, TDestination> IgnoreLifecycle<TSource, TDestination>(
        this IMappingExpression<TSource, TDestination> map)
    {
        foreach (var name in Lifecycle)
            map.ForMember(name, opt => opt.Ignore());

        return map;
    }
}
