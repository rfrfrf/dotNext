﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static InlineIL.IL;
using static InlineIL.IL.Emit;
using M = InlineIL.MethodRef;

namespace DotNext.Runtime.InteropServices
{
    using RuntimeFeaturesAttribute = CompilerServices.RuntimeFeaturesAttribute;
    using typedref = TypedReference;    //IL compliant alias to TypedReference

    /// <summary>
    /// Low-level methods for direct memory access.
    /// </summary>
    /// <remarks>
    /// Methods in this class doesn't perform
    /// any safety check. Incorrect usage of them may destabilize
    /// Common Language Runtime.
    /// </remarks>
    public static unsafe class Memory
    {
        private static class FNV1a32
        {
            internal const int Offset = unchecked((int)2166136261);
            private const int Prime = 16777619;

            internal static int GetHashCode(int hash, int data) => (hash ^ data) * Prime;
        }

        private static class FNV1a64
        {
            internal const long Offset = unchecked((long)14695981039346656037);
            private const long Prime = 1099511628211;

            internal static long GetHashCode(long hash, long data) => (hash ^ data) * Prime;
        }

        /// <summary>
        /// Converts the value of this instance to a pointer of the specified type.
        /// </summary>
        /// <param name="source">The value to be converted into pointer.</param>
        /// <typeparam name="T">The type of the pointer.</typeparam>
        /// <returns>The typed pointer.</returns>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* ToPointer<T>(this IntPtr source)
            where T : unmanaged
        {
            Push(source);
            return ReturnPointer<T>();
        }

        /// <summary>
        /// Converts the value of this instance to a pointer of the specified type.
        /// </summary>
        /// <param name="source">The value to be converted into pointer.</param>
        /// <typeparam name="T">The type of the pointer.</typeparam>
        /// <returns>The typed pointer.</returns>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* ToPointer<T>(this UIntPtr source)
            where T : unmanaged
        {
            Push(source);
            return ReturnPointer<T>();
        }

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the given location
        /// and adjust pointer according with size of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged type to dereference.</typeparam>
        /// <param name="source">A pointer to block of memory.</param>
        /// <returns>Dereferenced value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Read<T>(ref IntPtr source)
            where T : unmanaged
        {
            var result = Unsafe.Read<T>(source.ToPointer());
            source += sizeof(T);
            return result;
        }

        /// <summary>
        /// Writes the characters in UTF-16 encoding to the specified address in the memory and adjust the pointer.
        /// </summary>
        /// <remarks>
        /// This method encodes the characters as null-terminated string.
        /// </remarks>
        /// <param name="destination">The location of in the memory.</param>
        /// <param name="str">The characters to be written into the memory.</param>
        public static unsafe void WriteString(ref IntPtr destination, ReadOnlySpan<char> str)
        {
            var len = str.Length;
            var dest = new Span<char>(destination.ToPointer(), len + 1);
            str.CopyTo(dest);
            dest[len] = default;
            destination += dest.Length * sizeof(char);
        }

        /// <summary>
        /// Writes the string in UTF-16 encoding to the specified address in the memory and adjust the pointer.
        /// </summary>
        /// <param name="destination">The location of in the memory.</param>
        /// <param name="str">The string to be written into the memory.</param>
        public static unsafe void WriteString(ref IntPtr destination, string str) => WriteString(ref destination, str.AsSpan());

        /// <summary>
        /// Reads UTF-16 encoded string from the specified memory location and adjust the pointer.
        /// </summary>
        /// <param name="source">The pointer to the memory location containing null-terminated characters.</param>
        /// <returns>The string decoded from the memory.</returns>
        public static unsafe string ReadString(ref IntPtr source)
        {
            var str = new string(source.ToPointer<char>());
            source += (str.Length + 1) * sizeof(char);
            return str;
        }

        /// <summary>
        /// Reads a value of type <typeparamref name="T"/> from the given location
        /// without assuming architecture dependent alignment of the addresses;
        /// and adjust pointer according with size of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged type to dereference.</typeparam>
        /// <param name="source">A pointer to block of memory.</param>
        /// <returns>Dereferenced value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadUnaligned<T>(ref IntPtr source)
            where T : unmanaged
        {
            var result = Unsafe.ReadUnaligned<T>(source.ToPointer());
            source += sizeof(T);
            return result;
        }

        /// <summary>
        /// Writes a value into the address using aligned access
        /// and adjust address according with size of
        /// type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged type.</typeparam>
        /// <param name="destination">Destination address.</param>
        /// <param name="value">The value to write into the address.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(ref IntPtr destination, T value)
            where T : unmanaged
        {
            Unsafe.Write(destination.ToPointer(), value);
            destination += sizeof(T);
        }

        /// <summary>
        /// Writes a value into the address using unaligned access
        /// and adjust address according with size of
        /// type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Unmanaged type.</typeparam>
        /// <param name="destination">Destination address.</param>
        /// <param name="value">The value to write into the address.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUnaligned<T>(ref IntPtr destination, T value)
            where T : unmanaged
        {
            Unsafe.WriteUnaligned(destination.ToPointer(), value);
            destination += sizeof(T);
        }

        /// <summary>
        /// Copies the specified number of bytes from one address in memory to another.
        /// </summary>
        /// <param name="source">The address of the bytes to copy.</param>
        /// <param name="destination">The target address.</param>
        /// <param name="length">The number of bytes to copy from source address to destination.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static void Copy(void* source, void* destination, long length)
            => Buffer.MemoryCopy(source, destination, length, length);

        /// <summary>
        /// Copies the specified number of bytes from one address in memory to another.
        /// </summary>
        /// <param name="source">The address of the bytes to copy.</param>
        /// <param name="destination">The target address.</param>
        /// <param name="length">The number of bytes to copy from source address to destination.</param>
		public static void Copy(IntPtr source, IntPtr destination, long length)
            => Copy(source.ToPointer(), destination.ToPointer(), length);

        /// <summary>
        /// Copies the specified number of elements from source address to the destination address.
        /// </summary>
        /// <param name="source">The address of the bytes to copy.</param>
        /// <param name="destination">The target address.</param>
        /// <param name="count">The number of elements to copy.</param>
        /// <typeparam name="T">The type of the element.</typeparam>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy<T>(ref T source, ref T destination, uint count)
            where T : unmanaged
        {
            Push(ref destination);
            Push(ref source);
            Push(count);
            Sizeof(typeof(T));
            Mul_Ovf_Un();
            Cpblk();
            Ret();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte Adjust<T>(this ref byte address)
            where T : unmanaged
        {
            Push(ref address);
            Sizeof(typeof(T));
            Conv_I();
            Add();
            return ref ReturnRef<byte>();
        }

        private static ref byte Adjust<T>(this ref byte address, [In, Out]long* length)
            where T : unmanaged
        {
            Push(length);
            Dup();
            Ldind_I8();
            Sizeof(typeof(T));
            Conv_I8();
            Sub();
            Stind_I8();

            return ref address.Adjust<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T As<T>(this ref byte address)
            where T : unmanaged
        {
            Push(ref address);
            Ldobj(typeof(T));
            return Return<T>();
        }

        private static T AsUnaligned<T>(this ref byte address)
            where T : unmanaged
        {
            Push(ref address);
            Unaligned(1);
            Ldobj(typeof(T));
            return Return<T>();
        }

        /// <summary>
        /// Computes 64-bit hash code for the block of memory, 64-bit version.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        public static long GetHashCode64(ref byte source, long length, long hash, Func<long, long, long> hashFunction, bool salted = true)
            => GetHashCode64(ref source, length, hash, new ValueFunc<long, long, long>(hashFunction, true), salted);

        /// <summary>
        /// Computes 64-bit hash code for the block of memory, 64-bit version.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        public static long GetHashCode64(ref byte source, long length, long hash, in ValueFunc<long, long, long> hashFunction, bool salted = true)
        {
            switch (length)
            {
                case sizeof(byte):
                    hash = hashFunction.Invoke(hash, source);
                    break;
                case sizeof(ushort):
                    hash = hashFunction.Invoke(hash, source.AsUnaligned<ushort>());
                    break;
                case sizeof(uint):
                    hash = hashFunction.Invoke(hash, source.AsUnaligned<uint>());
                    break;
                default:
                    for (; length >= sizeof(IntPtr); source = ref source.Adjust<IntPtr>(&length))
                        hash = hashFunction.Invoke(hash, source.AsUnaligned<IntPtr>().ToInt64());
                    for (; length > 0L; source = ref source.Adjust<byte>(&length))
                        hash = hashFunction.Invoke(hash, source);
                    break;
            }
            return salted ? hashFunction.Invoke(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        /// <summary>
		/// Computes 64-bit hash code for the block of memory, 64-bit version.
		/// </summary>
		/// <remarks>
		/// This method may give different value each time you run the program for
		/// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
		/// </remarks>
		/// <param name="source">A pointer to the block of memory.</param>
		/// <param name="length">Length of memory block to be hashed, in bytes.</param>
		/// <param name="hash">Initial value of the hash.</param>
		/// <param name="hashFunction">Hashing function.</param>
		/// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
		/// <returns>Hash code of the memory block.</returns>
		[CLSCompliant(false)]
        public static long GetHashCode64(void* source, long length, long hash, Func<long, long, long> hashFunction, bool salted = true)
            => GetHashCode64(ref Unsafe.AsRef<byte>(source), length, hash, new ValueFunc<long, long, long>(hashFunction, true), salted);

        /// <summary>
        /// Computes 64-bit hash code for the block of memory, 64-bit version.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        [CLSCompliant(false)]
        public static long GetHashCode64(void* source, long length, long hash, in ValueFunc<long, long, long> hashFunction, bool salted = true)
            => GetHashCode64(ref Unsafe.AsRef<byte>(source), length, hash, hashFunction, salted);

        /// <summary>
        /// Computes 64-bit hash code for the block of memory.
        /// </summary>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <remarks>
        /// This method uses FNV-1a hash algorithm.
        /// </remarks>
        /// <returns>Content hash code.</returns>
        /// <seealso href="http://www.isthe.com/chongo/tech/comp/fnv/#FNV-1a">FNV-1a</seealso>
        [RuntimeFeatures(Augmentation = true)]
        public static long GetHashCode64(ref byte source, long length, bool salted = true)
            => GetHashCode64(ref source, length, FNV1a64.Offset, new ValueFunc<long, long, long>(FNV1a64.GetHashCode), salted);

        /// <summary>
        /// Computes 64-bit hash code for the block of memory.
        /// </summary>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <remarks>
        /// This method uses FNV-1a hash algorithm.
        /// </remarks>
        /// <returns>Content hash code.</returns>
        /// <seealso href="http://www.isthe.com/chongo/tech/comp/fnv/#FNV-1a">FNV-1a</seealso>
        [CLSCompliant(false)]
        public static long GetHashCode64(void* source, long length, bool salted = true)
            => GetHashCode64(ref Unsafe.AsRef<byte>(source), length, salted);

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        public static int GetHashCode32(ref byte source, long length, int hash, Func<int, int, int> hashFunction, bool salted = true)
            => GetHashCode32(ref source, length, hash, new ValueFunc<int, int, int>(hashFunction, true), salted);

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        public static int GetHashCode32(ref byte source, long length, int hash, in ValueFunc<int, int, int> hashFunction, bool salted = true)
        {
            switch (length)
            {
                case sizeof(byte):
                    hash = hashFunction.Invoke(hash, source);
                    break;
                case sizeof(ushort):
                    hash = hashFunction.Invoke(hash, source.AsUnaligned<ushort>());
                    break;
                default:
                    for (; length >= sizeof(int); source = ref source.Adjust<int>(&length))
                        hash = hashFunction.Invoke(hash, source.AsUnaligned<int>());
                    for (; length > 0L; source = ref source.Adjust<byte>(&length))
                        hash = hashFunction.Invoke(hash, source);
                    break;
            }
            return salted ? hashFunction.Invoke(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        [CLSCompliant(false)]
        public static int GetHashCode32(void* source, long length, int hash, Func<int, int, int> hashFunction, bool salted = true)
            => GetHashCode32(ref Unsafe.AsRef<byte>(source), length, hash, new ValueFunc<int, int, int>(hashFunction, true), salted);

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <remarks>
        /// This method may give different value each time you run the program for
        /// the same data. To disable this behavior, pass false to <paramref name="salted"/>. 
        /// </remarks>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="hash">Initial value of the hash.</param>
        /// <param name="hashFunction">Hashing function.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <returns>Hash code of the memory block.</returns>
        [CLSCompliant(false)]
        public static int GetHashCode32(void* source, long length, int hash, in ValueFunc<int, int, int> hashFunction, bool salted = true)
            => GetHashCode32(ref Unsafe.AsRef<byte>(source), length, hash, hashFunction, salted);

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <remarks>
        /// This method uses FNV-1a hash algorithm.
        /// </remarks>
        /// <returns>Content hash code.</returns>
        /// <seealso href="http://www.isthe.com/chongo/tech/comp/fnv/#FNV-1a">FNV-1a</seealso>
        [RuntimeFeatures(Augmentation = true)]
        public static int GetHashCode32(ref byte source, long length, bool salted = true)
            => GetHashCode32(ref source, length, FNV1a32.Offset, new ValueFunc<int, int, int>(FNV1a32.GetHashCode), salted);

        internal static int GetHashCode32Aligned(ref byte source, long length, bool salted)
        {
            //do not call overloaded GetHashCode32Aligned accepting ValueFunc because it
            //is not so performant as manually inlined code
            var hash = FNV1a32.Offset;
            for (; length >= sizeof(int); source = ref source.Adjust<int>(&length))
                hash = FNV1a32.GetHashCode(hash, source.As<int>());
            for (; length > 0L; source = ref source.Adjust<byte>(&length))
                hash = FNV1a32.GetHashCode(hash, source);
            return salted ? FNV1a32.GetHashCode(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        internal static int GetHashCode32Aligned(ref byte source, long length, int hash, in ValueFunc<int, int, int> hashFunction, bool salted)
        {
            for (; length >= sizeof(int); source = ref source.Adjust<int>(&length))
                hash = hashFunction.Invoke(hash, source.As<int>());
            for (; length > 0L; source = ref source.Adjust<byte>(&length))
                hash = hashFunction.Invoke(hash, source);
            return salted ? hashFunction.Invoke(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        internal static long GetHashCode64Aligned(ref byte source, long length, bool salted)
        {
            //do not call overloaded GetHashCode64Aligned accepting ValueFunc because it
            //is not so performant as manually inlined code
            var hash = FNV1a64.Offset;
            for (; length >= sizeof(long); source = ref source.Adjust<long>(&length))
                hash = FNV1a64.GetHashCode(hash, source.As<long>());
            for (; length >= sizeof(uint); source = ref source.Adjust<uint>(&length))
                hash = FNV1a64.GetHashCode(hash, source.As<uint>());
            for (; length > 0L; source = ref source.Adjust<byte>(&length))
                hash = FNV1a64.GetHashCode(hash, source);
            return salted ? FNV1a64.GetHashCode(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        internal static long GetHashCode64Aligned(ref byte source, long length, long hash, in ValueFunc<long, long, long> hashFunction, bool salted)
        {
            for (; length >= sizeof(long); source = ref source.Adjust<long>(&length))
                hash = hashFunction.Invoke(hash, source.As<long>());
            for (; length >= sizeof(uint); source = ref source.Adjust<uint>(&length))
                hash = hashFunction.Invoke(hash, source.As<uint>());
            for (; length > 0L; source = ref source.Adjust<byte>(&length))
                hash = hashFunction.Invoke(hash, source);
            return salted ? hashFunction.Invoke(hash, RandomExtensions.BitwiseHashSalt) : hash;
        }

        /// <summary>
        /// Computes 32-bit hash code for the block of memory.
        /// </summary>
        /// <param name="source">A pointer to the block of memory.</param>
        /// <param name="length">Length of memory block to be hashed, in bytes.</param>
        /// <param name="salted"><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</param>
        /// <remarks>
        /// This method uses FNV-1a hash algorithm.
        /// </remarks>
        /// <returns>Content hash code.</returns>
        /// <seealso href="http://www.isthe.com/chongo/tech/comp/fnv/#FNV-1a">FNV-1a</seealso>
        [CLSCompliant(false)]
        public static int GetHashCode32(void* source, long length, bool salted = true)
            => GetHashCode32(ref Unsafe.AsRef<byte>(source), length, salted);

        /// <summary>
        /// Sets all bits of allocated memory to zero.
        /// </summary>
        /// <remarks>
        /// This method has the same behavior as <see cref="Unsafe.InitBlockUnaligned(ref byte,byte,uint)"/> but
        /// without restriction on <see cref="uint"/> data type for the length of the memory block.
        /// </remarks>
        /// <param name="address">The pointer to the memory to be cleared.</param>
        /// <param name="length">The length of the memory to be cleared, in bytes.</param>
        public static void ClearBits(IntPtr address, long length)
        {
            do
            {
                var count = (int)Math.Min(length, int.MaxValue);
                Unsafe.InitBlockUnaligned(address.ToPointer(), 0, (uint)count);
                address += count;
                length -= count;
            } while (length > 0);
        }

        /// <summary>
        /// Sets all bits of allocated memory to zero.
        /// </summary>
        /// <param name="address">The pointer to the memory to be cleared.</param>
        /// <param name="length">The length of the memory to be cleared.</param>
        [CLSCompliant(false)]
        public static void ClearBits(void* address, long length) => ClearBits(new IntPtr(address), length);

        /// <summary>
		/// Computes equality between two blocks of memory.
		/// </summary>
		/// <param name="first">A pointer to the first memory block.</param>
		/// <param name="second">A pointer to the second memory block.</param>
		/// <param name="length">Length of first and second memory blocks, in bytes.</param>
		/// <returns><see langword="true"/>, if both memory blocks have the same data; otherwise, <see langword="false"/>.</returns>
		[CLSCompliant(false)]
        public static bool Equals(void* first, void* second, long length)
            => Equals(ref Unsafe.AsRef<byte>(first), ref Unsafe.AsRef<byte>(second), length);

        private static bool EqualsUnaligned(ref byte first, ref byte second, long length)
        {
            do
            {
                var count = (int)Math.Min(length, int.MaxValue);
                if (MemoryMarshal.CreateSpan(ref first, count).SequenceEqual(MemoryMarshal.CreateSpan(ref second, count)))
                {
                    first = ref Unsafe.Add(ref first, count);
                    second = ref Unsafe.Add(ref second, count);
                    length -= count;
                }
                else
                    return false;
            } while (length > 0);
            return true;
        }

        /// <summary>
        /// Computes equality between two blocks of memory.
        /// </summary>
        /// <param name="first">A pointer to the first memory block.</param>
        /// <param name="second">A pointer to the second memory block.</param>
        /// <param name="length">Length of first and second memory blocks, in bytes.</param>
        /// <returns><see langword="true"/>, if both memory blocks have the same data; otherwise, <see langword="false"/>.</returns>
        public static bool Equals(ref byte first, ref byte second, long length) => AreSame(in first, in second) || length switch
        {
            0L => true,
            sizeof(byte) => first == second,
            sizeof(ushort) => first.AsUnaligned<ushort>() == second.As<ushort>(),
            sizeof(uint) => first.AsUnaligned<uint>() == second.AsUnaligned<uint>(),
            sizeof(ulong) => first.AsUnaligned<ulong>() == second.AsUnaligned<ulong>(),
            _ => EqualsUnaligned(ref first, ref second, length)
        };

        internal static int CompareUnaligned(ref byte first, ref byte second, long length)
        {
            var comparison = 0;
            for (int count; length > 0 && comparison == 0; length -= count, first = ref Unsafe.Add(ref first, count), second = ref Unsafe.Add(ref second, count))
            {
                count = (int)Math.Min(length, int.MaxValue);
                comparison = MemoryMarshal.CreateSpan(ref first, count).SequenceCompareTo(MemoryMarshal.CreateSpan(ref second, count));
            }
            return comparison;
        }

        /// <summary>
        /// Bitwise comparison of two memory blocks.
        /// </summary>
        /// <param name="first">The pointer to the first memory block.</param>
        /// <param name="second">The pointer to the second memory block.</param>
        /// <param name="length">The length of the first and second memory blocks.</param>
        /// <returns>Comparison result which has the semantics as return type of <see cref="IComparable.CompareTo(object)"/>.</returns>
        public static int Compare(ref byte first, ref byte second, long length)
        {
            if (AreSame(in first, in second))
                return 0;
            switch (length)
            {
                default:
                    Push(first);
                    Push(second);
                    Push(length);
                    Call(new M(typeof(Memory), nameof(CompareUnaligned)));
                    break;
                case 0L:
                    Ldc_I4_0();
                    break;
                case 1:
                    Push(first);
                    Push(second);
                    Ldind_U1();
                    Call(new M(typeof(byte), nameof(byte.CompareTo), typeof(byte)));
                    break;
                case 2:
                    Push(first);
                    Unaligned(1);
                    Ldind_U2();
                    Conv_U8();
                    Pop(out ulong temp);
                    Push(ref temp);
                    Push(second);
                    Unaligned(1);
                    Ldind_U2();
                    Conv_U8();
                    Call(new M(typeof(ulong), nameof(ulong.CompareTo), typeof(ulong)));
                    break;
                case 4:
                    Push(first);
                    Unaligned(1);
                    Ldind_U4();
                    Conv_U8();
                    Pop(out temp);
                    Push(ref temp);
                    Push(second);
                    Unaligned(1);
                    Ldind_U4();
                    Conv_U8();
                    Call(new M(typeof(ulong), nameof(ulong.CompareTo), typeof(ulong)));
                    break;
                case 8:
                    Push(first);
                    Unaligned(1);
                    Ldobj(typeof(ulong));
                    Pop(out temp);
                    Push(ref temp);
                    Push(second);
                    Unaligned(1);
                    Ldobj(typeof(ulong));
                    Call(new M(typeof(ulong), nameof(ulong.CompareTo), typeof(ulong)));
                    break;
            }
            return Return<int>();
        }

        /// <summary>
        /// Bitwise comparison of two memory blocks.
        /// </summary>
        /// <param name="first">The pointer to the first memory block.</param>
        /// <param name="second">The pointer to the second memory block.</param>
        /// <param name="length">The length of the first and second memory blocks.</param>
        /// <returns>Comparison result which has the semantics as return type of <see cref="IComparable.CompareTo(object)"/>.</returns>
        [CLSCompliant(false)]
        public static int Compare(void* first, void* second, long length) 
            => Compare(ref Unsafe.AsRef<byte>(first), ref Unsafe.AsRef<byte>(second), length);

        internal static bool IsZeroAligned(ref byte address, long length)
        {
            var result = false;
            if (Vector.IsHardwareAccelerated)
                while (length >= Vector<byte>.Count)
                    if (address.As<Vector<byte>>() == Vector<byte>.Zero)
                        address = ref address.Adjust<Vector<byte>>(&length);
                    else
                        goto exit;
            while (length >= sizeof(UIntPtr))
                if (address.As<UIntPtr>() == default)
                    address = ref address.Adjust<UIntPtr>(&length);
                else
                    goto exit;
            while (length > 0)
                if (address == 0)
                    address = ref address.Adjust<byte>(&length);
                else
                    goto exit;
            result = true;
            exit:
            return result;
        }

        internal static bool EqualsAligned(ref byte first, ref byte second, long length)
        {
            var result = false;
            if (Vector.IsHardwareAccelerated)
                while (length >= Vector<byte>.Count)
                    if (first.As<Vector<byte>>() == second.As<Vector<byte>>())
                    {
                        length -= sizeof(Vector<byte>);
                        first = ref first.Adjust<Vector<byte>>();
                        second = ref second.Adjust<Vector<byte>>();
                    }
                    else
                        goto exit;
            while (length >= sizeof(UIntPtr))
                if (first.As<UIntPtr>() == second.As<UIntPtr>())
                {
                    length -= sizeof(UIntPtr);
                    first = ref first.Adjust<UIntPtr>();
                    second = ref second.Adjust<UIntPtr>();
                }
                else
                    goto exit;
            while (length > 0)
                if (first == second)
                {
                    length -= sizeof(byte);
                    first = ref first.Adjust<byte>();
                    second = ref second.Adjust<byte>();
                }
                else
                    goto exit;
            result = true;
            exit:
            return result;
        }

        /// <summary>
        /// Indicates that two managed pointers are equal.
        /// </summary>
        /// <typeparam name="T">Type of managed pointer.</typeparam>
        /// <param name="first">The first managed pointer.</param>
        /// <param name="second">The second managed pointer.</param>
        /// <returns><see langword="true"/>, if both managed pointers are equal; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreSame<T>(in T first, in T second)
        {
            Ldarg(nameof(first));
            Ldarg(nameof(second));
            Ceq();
            return Return<bool>();
        }

        /// <summary>
        /// Returns address of the managed pointer to type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of managed pointer.</typeparam>
        /// <param name="value">Managed pointer to convert into address.</param>
        /// <returns>The address for the managed pointer.</returns>
        /// <remarks>
        /// This method converts managed pointer into address,
        /// not the address of the object itself.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr AddressOf<T>(in T value)
        {
            Ldarg(nameof(value));
            return Return<IntPtr>();
        }

        /// <summary>
        /// Swaps two values.
        /// </summary>
        /// <param name="first">The first value to be replaced with <paramref name="second"/>.</param>
        /// <param name="second">The second value to be replaced with <paramref name="first"/>.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        public static void Swap<T>(ref T first, ref T second)
        {
            var tmp = first;
            first = second;
            second = tmp;
        }

        /// <summary>
        /// Swaps two values.
        /// </summary>
        /// <param name="first">The first value to be replaced with <paramref name="second"/>.</param>
        /// <param name="second">The second value to be replaced with <paramref name="first"/>.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Swap<T>(T* first, T* second)
            where T : unmanaged
            => Swap(ref first[0], ref second[0]);

        /// <summary>
        /// Converts typed reference into managed pointer.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="reference">The typed reference.</param>
        /// <returns>A managed pointer to the value represented by reference.</returns>
        /// <exception cref="InvalidCastException"><typeparamref name="T"/> is not identical to the type stored in the typed reference.</exception>
        [SuppressMessage("Usage", "CA2208", Justification = "The name of the generic parameter is correct")]
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AsRef<T>(this typedref reference)
        {
            Ldarg(nameof(reference));
            Refanyval(typeof(T));
            return ref ReturnRef<T>();
        }

        /// <summary>
        /// Converts contiguous memory identified by the specified pointer
        /// into <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="pointer">The typed pointer.</param>
        /// <typeparam name="T">The type of the pointer.</typeparam>
        /// <returns>The span of contiguous memory.</returns>
        [CLSCompliant(false)]
        public static unsafe Span<byte> AsSpan<T>(T* pointer) where T : unmanaged => AsSpan(ref pointer[0]);

        /// <summary>
        /// Converts contiguous memory identified by the specified pointer
        /// into <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="value">The managed pointer.</param>
        /// <typeparam name="T">The type of the pointer.</typeparam>
        /// <returns>The span of contiguous memory.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<byte> AsSpan<T>(ref T value) where T : unmanaged => MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), sizeof(T));

        /// <summary>
        /// Copies one value into another.
        /// </summary>
        /// <typeparam name="T">The value type to copy.</typeparam>
        /// <param name="input">The reference to the source location.</param>
        /// <param name="output">The reference to the destination location.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy<T>(in T input, out T output)
            where T : struct
        {
            Ldarg(nameof(output));
            Ldarg(nameof(input));
            Cpobj(typeof(T));
            Ret();
            throw Unreachable();    //need here because output var should be assigned
        }

        /// <summary>
        /// Copies one value into another.
        /// </summary>
        /// <typeparam name="T">The value type to copy.</typeparam>
        /// <param name="input">The reference to the source location.</param>
        /// <param name="output">The reference to the destination location.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static void Copy<T>(T* input, T* output)
            where T : unmanaged
            => Copy(in input[0], out output[0]);

        /// <summary>
        /// Determines whether the specified managed pointer is <see langword="null"/>.
        /// </summary>
        /// <param name="value">The managed pointer to check.</param>
        /// <typeparam name="T">The type of the managed pointer.</typeparam>
        /// <returns><see langword="true"/>, if the specified managed pointer is <see langword="null"/>; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull<T>(in T value)
        {
            Ldarg(nameof(value));
            Ldnull();
            Conv_I();
            Ceq();
            return Return<bool>();
        }

        /// <summary>
        /// Throws <see cref="NullReferenceException"/> if given managed pointer is <see langword="null"/>.
        /// </summary>
        /// <param name="value">The managed pointer to check.</param>
        /// <typeparam name="T">The type of the managed pointer.</typeparam>
        /// <exception cref="NullReferenceException"><paramref name="value"/> pointer is <see langword="null"/>.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNull<T>(in T value)
        {
            Ldarg(nameof(value));
            Ldobj(typeof(T));
            Pop();
            Ret();
        }

        /// <summary>
        /// Gets a reference to the array element with restricted mutability.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <param name="array">The array object.</param>
        /// <param name="index">The index of the array element.</param>
        /// <returns>The reference to the array element with restricted mutability.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T GetReadonlyRef<T>(this T[] array, long index)
        {
            Push(array);
            Push(index);
            Conv_Ovf_I();
            Readonly();
            Ldelema(typeof(T));
            return ref ReturnRef<T>();
        }
    }
}
