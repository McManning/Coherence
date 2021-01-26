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

    public class NativeArray<T> : IDisposable where T : struct
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

        public NativeArray()
        {
            isUnsafeReference = false;
            ptr = IntPtr.Zero;
            Length = 0;
        }

        public NativeArray(IntPtr src, int count)
        {
            isUnsafeReference = true;
            ptr = src;
            Length = count;

            // A null pointer can't have length
            if (src == IntPtr.Zero)
            {
                Length = 0;
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
            isUnsafeReference = false;
        }

        public void CopyFrom(NativeArray<T> other)
        {
            CopyFrom(other.ptr, other.Length);
        }

        public void CopyFrom(IntPtr src, int count)
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
            }

            UnsafeNativeMethods.CopyMemory(ptr, src, (uint)(ElementSize * count));
            isUnsafeReference = false;
        }

        public bool Equals(NativeArray<T> other)
        {
            return Equals(other.ptr, other.Length);
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

        public T this[int index]
        {
            get {
                if (index >= Length || index < 0)
                {
                    throw new IndexOutOfRangeException();
                }

                int offset = ElementSize * index;
                return FastStructure.PtrToStructure<T>(ptr + offset);
            }
        }
    }

    /// <summary>
    /// An array of structs with utilities for manipulating that array in various ways.
    ///
    /// <para>
    ///     This provides a dirty state to the buffer - indicating the maximum range
    ///     where modifications were made since the previous call to <see cref="Read"/>.
    /// </para>
    /// <para>
    ///     As an example - <c>foo.Clear().Resize(10).Fill(ptr, 5, 2)</c> would dirty
    ///     the array with an index range [0, 9].
    /// </para>
    /// <para>
    ///     Another example - <c>foo.Fill(ptr, 5, 2); foo[9] = bar;</c> would dirty
    ///     the index range [5, 9] - where the first op dirtied [5, 6] and the second [9, 9].
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ArrayBuffer<T> where T : struct
    {
        /*  Usage should look somewhat like the following:

            On Blender's side:

            ArrayBuffer<MVert> vertsBuf;
            MVert[] vertsFromBlender;
            ArrayBuffer<InteropVector3> interopVerts;

            // Copy data from Blender memory if it differs
            if (!vertsBuf.Equals(vertsFromBlender)) {
                vertsBuf.CopyFrom(vertsFromBlender);
                MagicMethodToUpdateInteropVertsFromVertsBuf();

                interopVerts[5].x = 1.0f; // Modify a vertex
                interopVerts.AppendCopy(10); // Copy a vertex into a new entry
                interopVerts.Add(new InteropVector3(...)); // Add a vertex
            }

            // .. Sometime later .. Send a copy to Unity if the vertices are dirty
            if (interopVerts.IsDirty) {
                MagicSendingMethod((node) => {
                    MagicHeaderWritingMethod();

                    // Write directly into shared memory
                    interopVerts
                        .CopyTo(
                            node.ptr,
                            interopVerts.DirtyStart,
                            interopVerts.DirtyLength
                        )
                        .Clean(); // Un-dirty it for next read
                });
            }

            On Unity's side - we'd read the buffer in a different (Unity struct) format:

            ArrayBuffer<Vector3> vertices;

            MagicReadingMethod((node) => {
                var hdr = MagicHeaderReadingMethod(node);

                // Resize to fit if needed and read directly from shared memory
                vertices
                    .Resize(hdr.length)
                    .CopyFrom(node.ptr, hdr.index, hdr.count);
            }

            // .. Sometime later .. Upload data to the GPU if dirtied
            if (vertices.IsDirty) {
                mesh.vertices = vertices.Read();
            }
        */
        private T[] data;

        public struct Range
        {
            public int start;
            public int end;

            public int Length { get => end - start + 1; }

            public void Expand(int start, int end)
            {
                this.start = Math.Min(start, this.start);
                this.end = Math.Max(end, this.end);
            }

            public void Restrict(int min, int max)
            {
                start = Math.Max(min, start);
                end = Math.Min(max, end);
            }
        }

        public bool IsEmpty
        {
            get => data == null || Length < 1;
        }

        public int Length { get; private set; }

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
        public int DirtyLength
        {
            get => DirtyEnd - DirtyStart + 1;
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
        /// Resize the buffer and mark dirty
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public ArrayBuffer<T> Resize(int size)
        {
            if (size < 1)
            {
                return Clear();
            }

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
                Dirty(0, Length - 1);
            }

            return this;
        }

        /// <summary>
        /// Deallocate the buffer and mark dirty until the next call
        /// to <see cref="Read"/>.
        /// </summary>
        /// <returns></returns>
        public ArrayBuffer<T> Clear()
        {
            data = null;
            Length = 0;

            // Dirty the whole thing - but with an invalid range
            // since there's no buffer elements to reference.
            IsDirty = true;
            DirtyStart = 0;
            DirtyEnd = -1;
            return this;
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
        public ArrayBuffer<T> Clean()
        {
            IsDirty = false;
            DirtyStart = 0;
            DirtyEnd = -1;
            return this;
        }

        /// <summary>
        /// Copy a subset of this buffer to the given memory address
        /// </summary>
        /// <param name="ptr">The destination memory location</param>
        /// <param name="index">The start index within this buffer</param>
        /// <param name="count">The number of elements to write</param>
        /// <returns></returns>
        public ArrayBuffer<T> CopyTo(IntPtr ptr, int index, int count)
        {
            FastStructure.WriteArray(ptr, data, index, count);
            return this;
        }

        /// <summary>
        /// Copy this buffer to the given memory address
        /// </summary>
        /// <param name="ptr">The destination memory location</param>
        /// <returns></returns>
        public ArrayBuffer<T> CopyTo(IntPtr ptr)
        {
            FastStructure.WriteArray(ptr, data, 0, Length);
            return this;
        }

        /// <summary>
        /// Copy this buffer to another buffer
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public ArrayBuffer<T> CopyTo(ArrayBuffer<T> other)
        {
            // Use our declared length, not the actual array length
            other.Resize(Length);
            Array.Copy(data, other.data, Length);
            other.Dirty(0, Length - 1);
            return this;
        }

        /// <summary>
        /// Copy data from the provided array - resizing ourselves to match.
        ///
        /// If the provided array is null, this will clear itself to match.
        /// </summary>
        /// <param name="other"></param>
        public ArrayBuffer<T> CopyFrom(T[] other)
        {
            if (other == null)
            {
                return Clear();
            }

            // let's try references for a sec...
            data = other;
            Length = other.Length;
            Dirty(0, Length - 1);

            return this;

            Resize(other.Length);
            Array.Copy(other, data, other.Length);
            return this;
        }

        /// <summary>
        /// Fill the buffer from the source memory location and mark dirty
        /// </summary>
        /// <param name="ptr">The source memory location</param>
        /// <param name="index">The start index within this buffer</param>
        /// <param name="count">The number of elements to read</param>
        /// <returns></returns>
        public ArrayBuffer<T> CopyFrom(IntPtr ptr, int index, int count)
        {
            if (index + count > Length)
            {
                throw new ArgumentOutOfRangeException(
                    $"index({index}) + count({count}) is larger than Length({Length})"
                );
            }

            FastStructure.ReadArray(data, ptr, index, count);
            Dirty(index, index + count - 1);

            return this;
        }

        /// <summary>
        /// Add a new <typeparamref name="T"/> to the end of the buffer
        /// </summary>
        public ArrayBuffer<T> Add(T value)
        {
            // Increase the underlying buffer size if it can't fit the new value
            if (data.Length <= Length)
            {
                Array.Resize(ref data, data.Length * 2);
            }

            data[Length] = value;
            Length++;

            // Dirty the element that was just added
            Dirty(Length - 1, Length - 1);
            return this;
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
            if (!IsEmpty)
            {
                Add(data[index]);
            }
        }

        /// <summary>
        /// Fast comparison against another array
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public unsafe bool Equals(T[] other)
        {
            // Both buffers point to nothing - this counts as equivalent
            if (other == null && data == null)
            {
                InteropLogger.Debug("Both null");
                return true;
            }

            if (other != null && data == null)
            {
                InteropLogger.Debug("data is null, other isn't");
                return false;
            }

            if (other == null && data != null)
            {
                InteropLogger.Debug("other is null, data isn't");
                return false;
            }

            if (other.Length != Length)
            {
                InteropLogger.Debug("Differnet lenggths");
                return false;
            }

            InteropLogger.Debug("memcmp test");


            return false;

            // Alternatively - SequenceEqual with spans
            // (see: https://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net)
            // but that's assuming .NET support that we don't necessarily have atm
            // return LowLevel.memcmp((byte[])other, data as byte[], other.Length) == 0;
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
            get {
                if (index >= Length || index < 0)
                {
                    throw new IndexOutOfRangeException();
                }

                return data[index];
            }
            set {
                if (index >= Length || index < 0)
                {
                    throw new IndexOutOfRangeException();
                }

                data[index] = value;
                Dirty(index, index);
            }
        }
    }
}
