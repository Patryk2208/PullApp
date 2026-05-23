using System.Reflection;
using System.Runtime.CompilerServices;

namespace TripPlanner.Infrastructure.Postgres;

// Reflection helper that sets init/private properties on domain entities.
// Domain entities are not owned by Infrastructure so we cannot add constructors to them.
// At runtime, init setters are regular setters — the restriction is compiler-only.
internal static class EntityMapper
{
    private static readonly BindingFlags All =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Creates an uninitialized instance (bypasses constructors).
    public static T New<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // Sets any property — works on both `init` and `private set`.
    public static T Set<T>(this T instance, string name, object? value) where T : class
    {
        var prop = typeof(T).GetProperty(name, All)
            ?? throw new InvalidOperationException($"{typeof(T).Name}.{name} not found");
        prop.SetValue(instance, value);
        return instance;
    }
}
