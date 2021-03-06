using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNext.Buffers
{
    /// <summary>
    /// Represents unified representation of the memory rented using various
    /// types of memory pools.
    /// </summary>
    /// <typeparam name="T">The type of the items in the memory pool.</typeparam>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct MemoryOwner<T> : IMemoryOwner<T>, IConvertible<Memory<T>>
    {
        private readonly object? owner;
        private readonly T[]? array;  // not null only if owner is ArrayPool
        private readonly int length;

        internal MemoryOwner(ArrayPool<T>? pool, T[] array, int length)
        {
            this.array = array;
            owner = pool;
            this.length = length;
        }

        /// <summary>
        /// Rents the array from the pool.
        /// </summary>
        /// <param name="pool">The array pool.</param>
        /// <param name="length">The length of the array.</param>
        public MemoryOwner(ArrayPool<T> pool, int length)
        {
            array = pool.Rent(length);
            owner = pool;
            this.length = length;
        }

        /// <summary>
        /// Rents the memory from the pool.
        /// </summary>
        /// <param name="pool">The memory pool.</param>
        /// <param name="length">The number of elements to rent; or <c>-1</c> to rent default amount of memory.</param>
        public MemoryOwner(MemoryPool<T> pool, int length = -1)
        {
            array = null;
            IMemoryOwner<T> owner;
            this.owner = owner = pool.Rent(length);
            this.length = length < 0 ? owner.Memory.Length : length;
        }

        /// <summary>
        /// Retns the memory.
        /// </summary>
        /// <param name="provider">The memory provider.</param>
        /// <param name="length">The number of elements to rent.</param>
        public MemoryOwner(Func<int, IMemoryOwner<T>> provider, int length)
        {
            array = null;
            IMemoryOwner<T> owner;
            this.owner = owner = provider(length);
            this.length = Math.Min(owner.Memory.Length, length);
        }

        /// <summary>
        /// Rents the memory.
        /// </summary>
        /// <param name="provider">The memory provider.</param>
        public MemoryOwner(Func<IMemoryOwner<T>> provider)
        {
            array = null;
            IMemoryOwner<T> owner;
            this.owner = owner = provider();
            length = owner.Memory.Length;
        }

        /// <summary>
        /// Wraps the array as if it was rented.
        /// </summary>
        /// <param name="array">The array to wrap.</param>
        /// <param name="length">The length of the array.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is less than 0 or greater than the length of <paramref name="array"/>.</exception>
        public MemoryOwner(T[] array, int length)
        {
            if (length > array.Length || length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            this.array = array;
            this.length = length;
            owner = null;
        }

        /// <summary>
        /// Wraps the array as if it was rented.
        /// </summary>
        /// <param name="array">The array to wrap.</param>
        public MemoryOwner(T[] array)
            : this(array, array.Length)
        {
        }

        /// <summary>
        /// Gets length of the rented memory, in bytes.
        /// </summary>
        public int Length => length;

        // WARNING: This is mutable method and should be used with care
        internal void Expand()
        {
            int length;

            if (array != null)
                length = array.Length;
            else if (owner is IMemoryOwner<T> memory)
                length = memory.Memory.Length;
            else
                goto exit;

            Unsafe.AsRef(in this.length) = length;

            exit:
            return;
        }

        /// <summary>
        /// Determines whether this memory is empty.
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// Gets the memory belonging to this owner.
        /// </summary>
        /// <value>The memory belonging to this owner.</value>
        public Memory<T> Memory
        {
            get
            {
                Memory<T> result;
                if (array != null)
                    result = new Memory<T>(array);
                else if (owner is IMemoryOwner<T> memory)
                    result = memory.Memory;
                else
                    result = default;

                return result.Slice(0, length);
            }
        }

        /// <inheritdoc/>
        Memory<T> IConvertible<Memory<T>>.Convert() => Memory;

        /// <summary>
        /// Gets managed pointer to the item in the rented memory.
        /// </summary>
        /// <param name="index">The index of the element in memory.</param>
        /// <value>The managed pointer to the item.</value>
        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= length)
                    goto invalid_index;
                if (array != null)
                    return ref array[index];
                if (owner is IMemoryOwner<T> memory)
                    return ref Unsafe.Add(ref MemoryMarshal.GetReference(memory.Memory.Span), index);
                invalid_index:
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>
        /// Releases rented memory.
        /// </summary>
        public void Dispose()
        {
            switch (owner)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case ArrayPool<T> pool:
                    pool.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                    break;
            }
        }
    }
}