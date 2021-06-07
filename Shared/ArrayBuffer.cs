using SharedMemory;
using System;
using System.Runtime.InteropServices;

namespace Coherence
{
    internal class LowLevel
    {
        #pragma warning disable IDE1006 // Naming Styles

        // Via: https://stackoverflow.com/a/1445405
        [DllImport("msvcrt.dll", CallingConvention=CallingConvention.Cdecl)]
        internal static extern int memcmp(byte[] b1, byte[] b2, long count);

        #pragma warning restore IDE1006 // Naming Styles
    }

    public interface IArray<T>
    {
        /// <summary>
        /// Number of elements in this array or sub-array.
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Index offset, if this was created through GetRange.
        /// Otherwise 0.
        /// </summary>
        int Offset { get; }

        /// <summary>
        /// Maximum allocated length for this buffer.
        ///
        /// <para>
        ///     If created through <see cref="GetRange(int, int)"/>
        ///     this will be the length of the parent buffer. Otherwise,
        ///     this will be equivalent to <see cref="Length"/>.
        /// </para>
        /// </summary>
        int MaxLength { get; }

        T this[int index] { get; }

        bool Equals(IArray<T> other);

        void CopyTo(IntPtr dst, int index, int count);

        void CopyTo(IArray<T> dst);

        void CopyFrom(IntPtr src, int index, int count);

        void CopyFrom(IArray<T> src);

        IArray<T> Resize(int size);

        /// <summary>
        /// Get a new array representing a subset of elements in this array.
        ///
        /// <para>
        ///     The sub-array will reference the same buffer of values,
        ///     but indexing will be offset by <paramref name="index"/>
        ///     and length will be <paramref name="count"/> elements.
        /// </para>
        ///
        /// <para>
        ///     Any changes to the parent array may invalidate the sub-array.
        /// </para>
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        IArray<T> GetRange(int index, int count);

        IArray<T> Clear();
    }

    /// <summary>
    /// Array of structs in unmanaged memory
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NativeArray<T> : IDisposable, IArray<T> where T : struct
    {
        private IntPtr ptr;
        private bool isUnsafeReference;

        /// <summary>
        /// Native size of a struct in the array
        /// </summary>
        public int ElementSize => FastStructure.SizeOf<T>();

        /// <summary>
        /// Total number of elements in the array
        /// </summary>
        public int Length { get; private set; }

        public int Offset { get; private set; }

        public int MaxLength { get; private set; }

        public NativeArray()
        {
            isUnsafeReference = false;
            ptr = IntPtr.Zero;
            MaxLength = 0;
            Length = 0;
            Offset = 0;
        }

        public NativeArray(IntPtr src, int count)
        {
            isUnsafeReference = true;
            ptr = src;
            Length = count;
            MaxLength = count;
            Offset = 0;

            // A null pointer can't have length
            if (src == IntPtr.Zero)
            {
                Length = 0;
                MaxLength = 0;
            }
        }

        public void Dispose()
        {
            if (ptr != IntPtr.Zero && !isUnsafeReference)
            {
                Marshal.FreeHGlobal(ptr);
            }

            ptr = IntPtr.Zero;
            Length = 0;
            MaxLength = 0;
            Offset = 0;
            isUnsafeReference = false;
        }

        /// <summary>
        /// Deep value equality test
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool Equals(IntPtr ptr, int length)
        {
            // Same pointer and same length
            if (this.ptr == ptr && this.Length == length)
                return true;

            // Different lengths
            if (this.Length != length)
                return false;

            // One null
            if (this.ptr == IntPtr.Zero && ptr != IntPtr.Zero)
                return false;

            // One null
            if (this.ptr != IntPtr.Zero && ptr == IntPtr.Zero)
                return false;

            // memcmp (or equivalent method...)
            return UnsafeNativeMethods.CompareMemory(this.ptr, ptr, length * ElementSize) == 0;
        }

        public bool Equals(IArray<T> other)
        {
            if (other is NativeArray<T> arr)
            {
                return Equals(arr.ptr, arr.Length);
            }

            return false;
        }

        public void CopyTo(IntPtr dst, int index, int count)
        {
            UnsafeNativeMethods.CopyMemory(
                dst,
                IntPtr.Add(ptr, (Offset + index) * ElementSize),
                (uint)(ElementSize * count)
            );
        }

        public void CopyTo(IArray<T> dst)
        {
            dst.CopyFrom(ptr, 0, Length);
        }

        public void CopyFrom(IntPtr src, int index, int count)
        {
            // Passed in an empty value - clear ourselves to match
            if (count < 1 || src == IntPtr.Zero)
            {
                Dispose();
                return;
            }

            // Reallocate
            if (Length != count)
            {
                Dispose();

                ptr = Marshal.AllocHGlobal(ElementSize * count);
                Length = count;
                MaxLength = count;
            }

            UnsafeNativeMethods.CopyMemory(ptr, src, (uint)(ElementSize * count));
            isUnsafeReference = false;
        }

        public void CopyFrom(IArray<T> src)
        {
            if (src is NativeArray<T> other)
            {
                CopyFrom(other.ptr, 0, other.Length);
                return;
            }

            throw new NotSupportedException();
        }

        /*public NativeArray<T> CopyFrom(NativeArray<T> src)
        {
            return CopyFrom(src.ptr, 0, src.Length) as NativeArray<T>;
        }*/

        public IArray<T> Resize(int size)
        {
            throw new NotImplementedException();
        }

        public IArray<T> GetRange(int index, int count)
        {
            return new NativeArray<T>(
                IntPtr.Add(ptr, index * ElementSize),
                count
            ) {
                // Adjust range to match
                Offset = index,
                Length = count,
                MaxLength = Length
            };
        }

        public IArray<T> Clear()
        {
            Dispose();
            return this;
        }

        public T this[int index]
        {
            get {
                if (index >= Length || index < 0)
                {
                    throw new IndexOutOfRangeException();
                }

                int offset = ElementSize * index;
                return FastStructure.PtrToStructure<T>(
                    IntPtr.Add(ptr, offset)
                );
            }
        }
    }

    /// <summary>
    /// An array of structs in managed memory.
    ///
    /// <para>
    ///     This provides a dirty state to the buffer - indicating the maximum range
    ///     where modifications were made since the previous call to <see cref="Read"/>.
    /// </para>
    /// <para>
    ///     As an example - <c>foo.Clear().Resize(10).CopyFrom(ptr, 5, 2)</c> would dirty
    ///     the array with an index range [0, 9].
    /// </para>
    /// <para>
    ///     Another example - <c>foo.CopyFrom(ptr, 5, 2); foo[9] = bar;</c> would dirty
    ///     the index range [5, 9] - where the first op dirtied [5, 6] and the second [9, 9].
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ArrayBuffer<T> : IArray<T> where T : struct
    {
        protected T[] data;

        public int Length { get; private set; }

        public int MaxLength { get; private set; }

        /// <summary>
        /// If created from <see cref="GetRange(int, int)"/> this will be the starting index.
        /// </summary>
        public int Offset { get; private set; }

        public bool IsDirty { get; private set; }

        /// <summary>
        /// Starting index of the buffer that was dirtied when <see cref="IsDirty"/>.
        /// </summary>
        public int DirtyStart { get; private set; }

        /// <summary>
        /// Last index of the dirtied region of the buffer when <see cref="IsDirty"/>.
        /// </summary>
        public int DirtyEnd { get; private set; }

        /// <summary>
        /// Length of the dirtied entries, starting at <see cref="DirtyStart"/>.
        /// </summary>
        public int DirtyLength => DirtyEnd - DirtyStart + 1;

        public ArrayBuffer(T[] initial = null)
        {
            if (initial != null)
            {
                data = initial;
                Length = initial.Length;
                MaxLength = initial.Length;
                Dirty(0, Length - 1);
            }
        }

        /// <summary>
        /// Get the underlying array of data - clearing the dirtied state in the process.
        ///
        /// <para>
        ///     Note that the length of the returned array may be larger than <see cref="Length"/>
        ///     as it may allocate more space to make room for more calls to <see cref="Add(T)"/>.
        /// </para>
        /// </summary>
        /// <returns></returns>
        public T[] Read()
        {
            IsDirty = false;
            DirtyStart = 0;
            DirtyEnd = 0;
            return data;
        }

        /// <summary>
        /// Reinterpret the underlying storage type of this buffer
        ///
        /// ... might belong in NativeArray instead.
        /// </summary>
        /// <typeparam name="N"></typeparam>
        /// <returns></returns>
        public ArrayBuffer<N> Reinterpret<N>() where N : struct
        {
            throw new NotImplementedException();
        }

        public ArrayBuffer<T> GetDirtyRange()
        {
            if (!IsDirty)
            {
                return null;
            }

            return GetRange(DirtyStart, DirtyLength) as ArrayBuffer<T>;
        }

        /// <summary>
        /// Dirty a range of the buffer.
        ///
        /// If the buffer has already been dirtied, this will combine ranges.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        private void Dirty(int start, int end)
        {
            if (IsDirty)
            {
                // Merge with previous range.
                // Length is also checked in case an op resizes the array
                DirtyStart = Math.Min(start, DirtyStart);
                DirtyEnd = Math.Min(Math.Max(end, DirtyEnd), Length - 1);

                // dirtied.Expand(start, end);
            }
            else
            {
                IsDirty = true;
                DirtyStart = start;
                DirtyEnd = end;
            }
        }

        /// <summary>
        /// Remove the dirtied status
        /// </summary>
        public void Clean()
        {
            IsDirty = false;
            DirtyStart = 0;
            DirtyEnd = -1;
        }

        /// <summary>
        /// Add a new <typeparamref name="T"/> to the end of the buffer
        /// </summary>
        public void Add(T value)
        {
            if (data == null)
            {
                data = new T[2];
                Length = 0;
                MaxLength = 0;
            }

            // Increase the underlying buffer size if it can't fit the new value
            if (data.Length <= Length)
            {
                Array.Resize(ref data, data.Length * 2);
            }

            data[Length] = value;
            Length++;
            MaxLength++;

            // Dirty the element that was just added
            Dirty(Length - 1, Length - 1);
        }

        /// <summary>
        /// Add a new <typeparamref name="T"/> to the end of the buffer
        /// - copied from the specified index.
        ///
        /// If the buffer is empty - this will do nothing.
        /// </summary>
        /// <param name="index"></param>
        public void AppendCopy(int index)
        {
            if (Length > 0)
            {
                Add(data[index]);
            }
        }

        public bool Equals(IArray<T> other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || Length != other.Length)
            {
                return false;
            }

            for (int i = 0; i < Length; i++)
            {
                if (!this[i].Equals(other[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Copy a subset of this buffer to the given memory address.
        ///
        /// The current <see cref="Offset"/> will be applied during copy.
        /// </summary>
        /// <param name="ptr">The destination memory location</param>
        /// <param name="index">The start index within this buffer</param>
        /// <param name="count">The number of elements to write</param>
        public void CopyTo(IntPtr dst, int index, int count)
        {
            FastStructure.WriteArray(dst, data, Offset + index, count);
        }

        /// <summary>
        /// Copy this buffer to another buffer as a whole
        /// </summary>
        /// <param name="other"></param>
        public void CopyTo(IArray<T> dst)
        {
            if (dst is ArrayBuffer<T> other)
            {
                other.Resize(Length);

                Array.Copy(data, other.data, Length);
                other.Dirty(0, Length - 1);

                return;
            }

            throw new NotImplementedException(
                "Can only copy ArrayBuffer to ArrayBuffer"
            );
        }

        /// <summary>
        /// Fill the buffer from the source memory location and mark dirty
        /// </summary>
        /// <param name="ptr">The source memory location</param>
        /// <param name="index">The start index within this buffer</param>
        /// <param name="count">The number of elements to read</param>
        public void CopyFrom(IntPtr src, int index, int count)
        {
            if (Offset > 0)
            {
                throw new NotImplementedException(
                    "Cannot CopyFrom into a subarray"
                );
            }

            if (index + count > Length)
            {
                throw new OverflowException(
                    $"index({index}) + count({count}) is larger than Length({Length})"
                );
            }

            FastStructure.ReadArray(data, src, index, count);
            Dirty(index, index + count - 1);
        }

        /// <summary>
        /// Copy data from the provided array - resizing ourselves to match.
        /// </summary>
        /// <param name="src"></param>
        public void CopyFrom(IArray<T> src)
        {
            if (Offset > 0)
            {
                throw new NotImplementedException(
                    "Cannot CopyFrom into a subarray"
                );
            }

            if (src is ArrayBuffer<T> other)
            {
                other.CopyTo(this);
                return;
            }

            throw new NotImplementedException(
                "Can only copy ArrayBuffer to ArrayBuffer"
            );
        }

        /// <summary>
        /// Copy data from a network message representing an array segment
        /// </summary>
        /// <param name="msg"></param>
        public void CopyFrom(InteropMessage msg)
        {
            Resize(msg.header.length)
                .CopyFrom(msg.data, msg.header.index, msg.header.count);
        }

        /// <summary>
        /// Resize the buffer and mark dirty.
        ///
        /// Any sub-arrays created from <see cref="GetRange(int, int)"/>
        /// may be invalidated by this operation.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public IArray<T> Resize(int size)
        {
            if (Offset > 0)
            {
                throw new NotImplementedException(
                    "Cannot resize a buffer created from GetRange"
                );
            }

            if (size < 1)
                return Clear();

            if (data == null)
            {
                data = new T[size];
            }
            else
            {
                Array.Resize(ref data, size);
            }

            // Only dirty if this operation did something.
            if (size != Length)
            {
                Length = size;
                MaxLength = size;
                Dirty(0, Length - 1);
            }

            return this;
        }

        /// <summary>
        /// Return a sub-array of this array for a range of elements.
        ///
        /// The sub-array will reference the same buffer of values,
        /// but indexing will be offset by <paramref name="index"/>
        /// and length will be reported as <paramref name="count"/>.
        ///
        /// Any changes to the parent's buffer may invalidate the
        /// sub-array.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public IArray<T> GetRange(int index, int count)
        {
            return new ArrayBuffer<T>
            {
                data = data,
                Offset = index,
                Length = count,
                MaxLength = Length
            };
        }

        /// <summary>
        /// Deallocate the buffer and mark dirty until the next call.
        ///
        /// Any sub-arrays created from <see cref="GetRange(int, int)"/>
        /// will be invalidated by this operation.
        /// to <see cref="Read"/>.
        /// </summary>
        /// <returns></returns>
        public IArray<T> Clear()
        {
            data = null;
            Offset = 0;
            Length = 0;
            MaxLength = 0;

            // Dirty the whole thing - but with an invalid range
            // since there's no buffer elements to reference.
            IsDirty = true;
            DirtyStart = 0;
            DirtyEnd = -1;
            return this;
        }

        /// <summary>
        /// Read/write a value from the buffer.
        ///
        /// Writes will dirty the buffer at the index.
        /// </summary>
        /// <param name="index"></param>
        /// <exception cref="IndexOutOfRangeException" />
        /// <returns></returns>
        public T this[int index]
        {
            // We don't verify ranges to keep this fast as possible.
            get {
                return data[Offset + index];
            }
            set {
                data[Offset + index] = value;
                Dirty(Offset + index, Offset + index);
            }
        }
    }
}
