using SupportDesk.Domain.Common;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// Gives an entity the id the database would have assigned, for tests that never save it.
/// </summary>
public static class EntityIds
{
    public static T WithId<T>(this T entity, int id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }
}
