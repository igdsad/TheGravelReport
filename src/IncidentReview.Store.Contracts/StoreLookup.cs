namespace IncidentReview.Store.Contracts;

/// <summary>Represents a typed store lookup that may have no matching value.</summary>
public sealed class StoreLookup<T>
    where T : notnull
{
    internal StoreLookup(T? value) => Value = value;

    public bool IsFound => Value is not null;

    public T? Value { get; }

}

/// <summary>Creates typed store lookup outcomes without static members on generic types.</summary>
public static class StoreLookup
{
    public static StoreLookup<T> Missing<T>()
        where T : notnull => new(default);

    public static StoreLookup<T> Found<T>(T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        return new StoreLookup<T>(value);
    }
}
