using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MongoDB.Migration;

internal static class EnumerableExtensions
{
    public static IReadOnlyDictionary<TKey, ImmutableArray<T>> ToImmutableMap<T, TKey>(
        this IEnumerable<T> sequence,
        Func<T, TKey> keySelector)
    where TKey : notnull
    {
        return ToImmutableMap(sequence, keySelector, null, static item => item);
    }

    public static IReadOnlyDictionary<TKey, ImmutableArray<TValue>> ToImmutableMap<T, TKey, TValue>(
        this IEnumerable<T> sequence,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector)
        where TKey : notnull
    {
        return ToImmutableMap(sequence, keySelector, null, valueSelector);
    }

    public static ImmutableDictionary<TKey, ImmutableArray<TValue>> ToImmutableMap<T, TKey, TValue>(
        this IEnumerable<T> sequence,
        Func<T, TKey> keySelector,
        EqualityComparer<TKey>? keyComparer,
        Func<T, TValue> valueSelector)
        where TKey : notnull
    {
        // immutable dictionary is optimized for many keys.
        // we assume many values few keys here.
        // use a array dictionary instead.
        var dict = ImmutableDictionary.CreateBuilder<TKey, ImmutableArray<TValue>>(keyComparer);
        foreach (var item in sequence)
        {
            var key = keySelector(item);
            var value = valueSelector(item);
            if (dict.TryGetValue(key, out var items))
            {
                var insertionIndex = items.Length;
                ref var itemsArray = ref Unsafe.As<ImmutableArray<TValue>, TValue[]>(ref items);
                Array.Resize(ref itemsArray, insertionIndex + 1);
                itemsArray[insertionIndex] = value;

                // we dont have a pointer to the items bucket, so we have to manually update the value.
                dict[key] = items;
            }
            else
            {
                var itemsArray = new[] { value };
                dict[key] = Unsafe.As<TValue[], ImmutableArray<TValue>>(ref itemsArray);
            }
        }

        return dict.ToImmutable();
    }

    public static IEnumerable<TOut> SelectTruthy<TIn, TOut>(this IEnumerable<TIn> sequence, Func<TIn, TOut?> filterPredicate)
        where TOut : class
    {
        foreach (var item in sequence)
        {
            if (filterPredicate(item) is { } result)
            {
                yield return result;
            }
        }
    }

    public static IEnumerable<TOut> SelectTruthy<TIn, TOut>(this IEnumerable<TIn> sequence, Func<TIn, TOut?> filterPredicate)
        where TOut : struct
    {
        foreach (var item in sequence)
        {
            if (filterPredicate(item) is { } result)
            {
                yield return result;
            }
        }
    }

    public static void ForAll<T>(this IEnumerable<T> sequence, Action<T> action)
    {
        foreach (var item in sequence)
        {
            action(item);
        }
    }
}
