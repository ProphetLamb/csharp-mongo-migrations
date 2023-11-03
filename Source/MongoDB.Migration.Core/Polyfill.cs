#pragma warning disable
#if !NET7_0_OR_GREATER
// Enable usage of the required modifier
namespace System.Runtime.CompilerServices
{
    /// <summary>Specifies that a type has required members or that a member is required.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    { }

    /// <summary>
    /// Indicates that compiler support for a particular feature is required for the location where this attribute is applied.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        /// <summary>
        /// The name of the compiler feature.
        /// </summary>
        public string FeatureName { get; }

        /// <summary>
        /// If true, the compiler can choose to allow access to the location where this attribute is applied if it does not understand <see cref="FeatureName"/>.
        /// </summary>
        public bool IsOptional { get; init; }

        /// <summary>
        /// The <see cref="FeatureName"/> used for the ref structs C# feature.
        /// </summary>
        public const string RefStructs = nameof(RefStructs);

        /// <summary>
        /// The <see cref="FeatureName"/> used for the required members C# feature.
        /// </summary>
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies that this constructor sets all required members for the current type, and callers
    /// do not need to set any required members themselves.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    { }
}
#endif

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// This dummy class is required to compile records when targeting .NET Standard
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif

#if !NET6_0_OR_GREATER
namespace System.Threading.Tasks
{
    internal static class PolyfillTaskExtensions
    {
        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public static Task WaitAsync(this Task innerTask, CancellationToken cancellationToken)
        {
            return Task.Run(() => innerTask, cancellationToken);
        }
        /// <summary>Gets a <see cref="Task"/> that will complete when this <see cref="Task"/> completes or when the specified <see cref="CancellationToken"/> has cancellation requested.</summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for a cancellation request.</param>
        /// <returns>The <see cref="Task"/> representing the asynchronous wait.  It may or may not be the same instance as the current instance.</returns>
        public static Task<T> WaitAsync<T>(this Task<T> innerTask, CancellationToken cancellationToken)
        {
            return Task.Run(() => innerTask, cancellationToken);
        }
    }
}
#endif


#if !NET6_0_OR_GREATER
namespace System.Linq
{
    using System.Diagnostics;
    using System.Collections;
    using System.Collections.Generic;

    internal static class PolyfillEnumerableExtensions
    {
        /// <summary>Returns the minimum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the minimum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>The value with the minimum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="System.IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MinBy(source, keySelector, comparer: null);

        /// <summary>Returns the minimum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the minimum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">The <see cref="IComparer{TKey}" /> to compare keys.</param>
        /// <returns>The value with the minimum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            comparer ??= Comparer<TKey>.Default;

            using IEnumerator<TSource> e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                if (default(TSource) is null)
                {
                    return default;
                }
                else
                {
                    throw new InvalidOperationException("Enumerable is empty");
                }
            }

            TSource value = e.Current;
            TKey key = keySelector(value);

            if (default(TKey) is null)
            {
                if (key == null)
                {
                    TSource firstValue = value;

                    do
                    {
                        if (!e.MoveNext())
                        {
                            // All keys are null, surface the first element.
                            return firstValue;
                        }

                        value = e.Current;
                        key = keySelector(value);
                    }
                    while (key == null);
                }

                while (e.MoveNext())
                {
                    TSource nextValue = e.Current;
                    TKey nextKey = keySelector(nextValue);
                    if (nextKey != null && comparer.Compare(nextKey, key) < 0)
                    {
                        key = nextKey;
                        value = nextValue;
                    }
                }
            }
            else
            {
                if (comparer == Comparer<TKey>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (Comparer<TKey>.Default.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) < 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>Returns the maximum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the maximum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>The value with the maximum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="System.IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => MaxBy(source, keySelector, null);

        /// <summary>Returns the maximum value in a generic sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to compare elements by.</typeparam>
        /// <param name="source">A sequence of values to determine the maximum value of.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">The <see cref="IComparer{TKey}" /> to compare keys.</param>
        /// <returns>The value with the maximum key in the sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException">No key extracted from <paramref name="source" /> implements the <see cref="IComparable" /> or <see cref="IComparable{TKey}" /> interface.</exception>
        /// <remarks>
        /// <para>If <typeparamref name="TKey" /> is a reference type and the source sequence is empty or contains only values that are <see langword="null" />, this method returns <see langword="null" />.</para>
        /// </remarks>
        public static TSource? MaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey>? comparer)
        {
            comparer ??= Comparer<TKey>.Default;

            using IEnumerator<TSource> e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                if (default(TSource) is null)
                {
                    return default;
                }
                else
                {
                    throw new InvalidOperationException("Enumerable is empty");
                }
            }

            TSource value = e.Current;
            TKey key = keySelector(value);

            if (default(TKey) is null)
            {
                if (key == null)
                {
                    TSource firstValue = value;

                    do
                    {
                        if (!e.MoveNext())
                        {
                            // All keys are null, surface the first element.
                            return firstValue;
                        }

                        value = e.Current;
                        key = keySelector(value);
                    }
                    while (key == null);
                }

                while (e.MoveNext())
                {
                    TSource nextValue = e.Current;
                    TKey nextKey = keySelector(nextValue);
                    if (nextKey != null && comparer.Compare(nextKey, key) > 0)
                    {
                        key = nextKey;
                        value = nextValue;
                    }
                }
            }
            else
            {
                if (comparer == Comparer<TKey>.Default)
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (Comparer<TKey>.Default.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
                else
                {
                    while (e.MoveNext())
                    {
                        TSource nextValue = e.Current;
                        TKey nextKey = keySelector(nextValue);
                        if (comparer.Compare(nextKey, key) > 0)
                        {
                            key = nextKey;
                            value = nextValue;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>Returns distinct elements from a sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to distinguish elements by.</typeparam>
        /// <param name="source">The sequence to remove duplicate elements from.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>An <see cref="IEnumerable{T}" /> that contains distinct elements from the source sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <remarks>
        /// <para>This method is implemented by using deferred execution. The immediate return value is an object that stores all the information that is required to perform the action. The query represented by this method is not executed until the object is enumerated either by calling its `GetEnumerator` method directly or by using `foreach` in Visual C# or `For Each` in Visual Basic.</para>
        /// <para>The <see cref="DistinctBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})" /> method returns an unordered sequence that contains no duplicate values. The default equality comparer, <see cref="EqualityComparer{T}.Default" />, is used to compare values.</para>
        /// </remarks>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => DistinctBy(source, keySelector, null);

        /// <summary>Returns distinct elements from a sequence according to a specified key selector function.</summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source" />.</typeparam>
        /// <typeparam name="TKey">The type of key to distinguish elements by.</typeparam>
        /// <param name="source">The sequence to remove duplicate elements from.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer{TKey}" /> to compare keys.</param>
        /// <returns>An <see cref="IEnumerable{T}" /> that contains distinct elements from the source sequence.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
        /// <remarks>
        /// <para>This method is implemented by using deferred execution. The immediate return value is an object that stores all the information that is required to perform the action. The query represented by this method is not executed until the object is enumerated either by calling its `GetEnumerator` method directly or by using `foreach` in Visual C# or `For Each` in Visual Basic.</para>
        /// <para>The <see cref="DistinctBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey}, IEqualityComparer{TKey}?)" /> method returns an unordered sequence that contains no duplicate values. If <paramref name="comparer" /> is <see langword="null" />, the default equality comparer, <see cref="EqualityComparer{T}.Default" />, is used to compare values.</para>
        /// </remarks>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
        {
            return DistinctByIterator(source, keySelector, comparer);
        }

        private static IEnumerable<TSource> DistinctByIterator<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
        {
            using IEnumerator<TSource> enumerator = source.GetEnumerator();

            if (enumerator.MoveNext())
            {
                var set = new HashSet<TKey>(comparer);
                do
                {
                    TSource element = enumerator.Current;
                    if (set.Add(keySelector(element)))
                    {
                        yield return element;
                    }
                }
                while (enumerator.MoveNext());
            }
        }

        /// <summary>
        /// An iterator that yields the distinct values in an <see cref="IEnumerable{TSource}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the source enumerable.</typeparam>
        private sealed partial class DistinctIterator<TSource> : Iterator<TSource>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly IEqualityComparer<TSource>? _comparer;
            private HashSet<TSource>? _set;
            private IEnumerator<TSource>? _enumerator;

            public DistinctIterator(IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
            {
                Debug.Assert(source != null);
                _source = source;
                _comparer = comparer;
            }

            public override Iterator<TSource> Clone() => new DistinctIterator<TSource>(_source, _comparer);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _enumerator = _source.GetEnumerator();
                        if (!_enumerator.MoveNext())
                        {
                            Dispose();
                            return false;
                        }

                        TSource element = _enumerator.Current;
                        _set = new HashSet<TSource>(_comparer);
                        _set.Add(element);
                        _current = element;
                        _state = 2;
                        return true;
                    case 2:
                        Debug.Assert(_enumerator != null);
                        Debug.Assert(_set != null);
                        while (_enumerator.MoveNext())
                        {
                            element = _enumerator.Current;
                            if (_set.Add(element))
                            {
                                _current = element;
                                return true;
                            }
                        }

                        break;
                }

                Dispose();
                return false;
            }

            public override void Dispose()
            {
                if (_enumerator != null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                    _set = null;
                }

                base.Dispose();
            }
        }
    }

    internal abstract class Iterator<TSource> : IEnumerable<TSource>, IEnumerator<TSource>
    {
        private readonly int _threadId;
        internal int _state;
        internal TSource _current = default!;

        /// <summary>
        /// Initializes a new instance of the <see cref="Iterator{TSource}"/> class.
        /// </summary>
        protected Iterator()
        {
            _threadId = Environment.CurrentManagedThreadId;
        }

        /// <summary>
        /// The item currently yielded by this iterator.
        /// </summary>
        public TSource Current => _current;

        /// <summary>
        /// Makes a shallow copy of this iterator.
        /// </summary>
        /// <remarks>
        /// This method is called if <see cref="GetEnumerator"/> is called more than once.
        /// </remarks>
        public abstract Iterator<TSource> Clone();

        /// <summary>
        /// Puts this iterator in a state whereby no further enumeration will take place.
        /// </summary>
        /// <remarks>
        /// Derived classes should override this method if necessary to clean up any
        /// mutable state they hold onto (for example, calling Dispose on other enumerators).
        /// </remarks>
        public virtual void Dispose()
        {
            _current = default!;
            _state = -1;
        }

        /// <summary>
        /// Gets the enumerator used to yield values from this iterator.
        /// </summary>
        /// <remarks>
        /// If <see cref="GetEnumerator"/> is called for the first time on the same thread
        /// that created this iterator, the result will be this iterator. Otherwise, the result
        /// will be a shallow copy of this iterator.
        /// </remarks>
        public IEnumerator<TSource> GetEnumerator()
        {
            Iterator<TSource> enumerator = _state == 0 && _threadId == Environment.CurrentManagedThreadId ? this : Clone();
            enumerator._state = 1;
            return enumerator;
        }

        /// <summary>
        /// Retrieves the next item in this iterator and yields it via <see cref="Current"/>.
        /// </summary>
        /// <returns><c>true</c> if there was another value to be yielded; otherwise, <c>false</c>.</returns>
        public abstract bool MoveNext();

        object? IEnumerator.Current => Current;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
#endif


#if !NET6_0_OR_GREATER
namespace System.Collections.Generic
{
    using System.Collections.Generic;

    internal sealed class PriorityQueue<TElement, TPriority>
    {
        private readonly List<(TPriority Priority, TElement Element)> _elements;
        private readonly IComparer<TPriority>? _comparer;

        public PriorityQueue() : this(0)
        {
        }

        public PriorityQueue(int size)
            : this(size, null)
        {
        }

        public PriorityQueue(IComparer<TPriority>? comparer) : this(0, comparer)
        {

        }

        public PriorityQueue(int size, IComparer<TPriority>? comparer)
        {
            _elements = new(size);
            _comparer = comparer;
        }

        public PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)> values)
            : this(values, null)
        {

        }

        public PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)> values, IComparer<TPriority>? comparer)
            : this(comparer)
        {
            foreach (var (element, priority) in values)
            {
                Enqueue(element, priority);
            }
        }

        private int GetLeftChildIndex(int elementIndex) => 2 * elementIndex + 1;
        private int GetRightChildIndex(int elementIndex) => 2 * elementIndex + 2;
        private int GetParentIndex(int elementIndex) => (elementIndex - 1) / 2;

        private bool HasLeftChild(int elementIndex) => GetLeftChildIndex(elementIndex) < _elements.Count;
        private bool HasRightChild(int elementIndex) => GetRightChildIndex(elementIndex) < _elements.Count;
        private bool IsRoot(int elementIndex) => elementIndex == 0;

        private (TPriority Priority, TElement Element) GetLeftChild(int elementIndex) => _elements[GetLeftChildIndex(elementIndex)];
        private (TPriority Priority, TElement Element) GetRightChild(int elementIndex) => _elements[GetRightChildIndex(elementIndex)];
        private (TPriority Priority, TElement Element) GetParent(int elementIndex) => _elements[GetParentIndex(elementIndex)];

        private int Compare(TPriority lhs, TPriority rhs)
        {
            if (_comparer is null)
            {
                return Comparer<TPriority>.Default.Compare(lhs, rhs);
            }
            return _comparer.Compare(lhs, rhs);
        }

        private void Swap(int firstIndex, int secondIndex)
        {
            var temp = _elements[firstIndex];
            _elements[firstIndex] = _elements[secondIndex];
            _elements[secondIndex] = temp;
        }

        private void ReCalculateDown()
        {
            int index = 0;
            while (HasLeftChild(index))
            {
                var smallerIndex = GetLeftChildIndex(index);
                if (HasRightChild(index) && Compare(GetRightChild(index).Priority, GetLeftChild(index).Priority) < 0)
                {
                    smallerIndex = GetRightChildIndex(index);
                }

                if (Compare(_elements[smallerIndex].Priority, _elements[index].Priority) >= 0)
                {
                    break;
                }

                Swap(smallerIndex, index);
                index = smallerIndex;
            }
        }

        private void ReCalculateUp()
        {
            var index = _elements.Count - 1;
            while (!IsRoot(index) && Compare(_elements[index].Priority, GetParent(index).Priority) < 0)
            {
                var parentIndex = GetParentIndex(index);
                Swap(parentIndex, index);
                index = parentIndex;
            }
        }

        public void Clear()
        {
            _elements.Clear();
        }

        public void Enqueue(TElement element, TPriority priority)
        {
            _elements.Add((priority, element));

            ReCalculateUp();
        }

        public TElement EnqueueDequeue(TElement element, TPriority priority)
        {
            if (_elements.Count == 0)
            {
                return element;
            }

            var result = _elements[0];
            _elements[0] = (priority, element);

            ReCalculateDown();
            return result.Element;
        }

        public void EnqueueRange(IEnumerable<TElement> elements, TPriority priority)
        {
            foreach (var element in elements)
            {
                Enqueue(element, priority);
            }
        }

        public void EnsureCapacity(int capacity)
        {
            // not supported
        }

        public TElement Peek()
        {
            return _elements[0].Element;
        }

        public void TrimExcess()
        {
            _elements.TrimExcess();
        }

        public bool TryPeek(out TElement element)
        {
            element = _elements.FirstOrDefault().Element;
            return _elements.Count != 0;
        }

        public bool TryDequeue(out TElement element, out TPriority priority)
        {
            if (_elements.Count == 0)
            {
                element = default;
                priority = default;
                return false;
            }

            var result = _elements[0];
            _elements[0] = _elements[_elements.Count - 1];
            _elements.RemoveAt(_elements.Count - 1);

            ReCalculateDown();

            (priority, element) = result;
            return true;
        }

    }
}
#endif

namespace MongoDB.Migration
{
    /// <summary>
    /// Abstracts the system clock to facilitate testing.
    /// </summary>
    public interface ISystemClock
    {
        /// <summary>
        /// Retrieves the current system time in UTC.
        /// </summary>
        DateTimeOffset UtcNow { get; }
    }
}
#pragma warning restore
