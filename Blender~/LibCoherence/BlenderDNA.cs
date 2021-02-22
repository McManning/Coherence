
/**
 * This file contains C# representations of Blender DNA structs
 * for use with .as_pointer() from bpy.types in Python.
 *
 * Since these structures are directly mirroring internal Blender
 * structures - they are subject to more breakage than just
 * accessing data through the Python APIs.
 */

using SharedMemory;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Coherence
{
    struct BlenderVersionMetadata
    {
        public Version min;
        public Version max;
        public int IDSize;
        public int customDataSize;
    }

    /// <summary>
    /// Compatibility factory to convert pointers to Blender structures
    /// that are also compatible with different Blender versions
    /// </summary>
    public static class BlenderDNA
    {
        static readonly BlenderVersionMetadata[] versions = new BlenderVersionMetadata[]
        {
            new BlenderVersionMetadata
            {
                min = new Version(1, 0, 0),
                max = new Version(2, 0, 0),
                IDSize = 10,
                customDataSize = 20
            },
            // ... etc ...
        };

        public static NativeArray<InteropVector3> GetCustomSplitNormals(IntPtr meshPtr)
        {
            // TODO: Calculate.
            IntPtr ptr = IntPtr.Zero;
            int count = 0;

            var arr = new NativeArray<InteropVector3>(ptr, count);
            return arr;
        }

        struct DeformationData
        {
            public NativeArray<byte> counts;
            public NativeArray<MDeformWeight> weights;
        }

        public static NativeArray<MDeformWeight> GetDeformationWeights(IntPtr meshPtr, int vertexCount)
        {
            // Naive implementation first.

            var mesh = FastStructure.PtrToStructure<Mesh>(meshPtr);

            var deforms = new MDeformVert[vertexCount];
            FastStructure.ReadArray(deforms, mesh.dvert, 0, vertexCount);
            
            var counts = new byte[vertexCount];
            var interopWeights = new List<InteropBoneWeight>(vertexCount);

            var vertexGroupToBone = new Dictionary<int, int>();
            // TODO: Fill.

            for (int i = 0; i < vertexCount; i++)
            {
                var deform = deforms[i];

                counts[i] = (byte)deform.totweight;
                var weights = new MDeformWeight[deform.totweight];
                FastStructure.ReadArray(weights, deform.dw, 0, deform.totweight);

                for (int j = 0; j < deform.totweight; j++)
                {
                    var weight = weights[j];
                    interopWeights.Add(new InteropBoneWeight { 
                        boneIndex = vertexGroupToBone[weight.def_nr],
                        weight = weight.weight
                    });
                }
            }
            
            int total = 0;
            for (int i = 0; i < vertexCount; i++) // O(vertices)
            {
                var tot = (byte)deforms[i].totweight;
                counts[i] = tot;
                total += tot;
            }

            var buffer = new NativeArray<InteropBoneWeight>(total);
           
            var deformWeightSize = FastStructure.SizeOf<MDeformWeight>();

            unsafe
            {
                bufferIdx = 0;
                for (int i = 0; i < vertexCount; i++) // O(vertices)
                {
                    var deform = deforms[i];
                
                    // still need to go vertex -> bone. And sort.
                    for (int j = 0; j < deform.totweight; j++)
                    {
                        var offset = IntPtr.Add(deform.dw, deformWeightSize * j);
                        int* def_nr = (int*)&offset;

                        offset = IntPtr.Add(offset, sizeof(float));
                        float* weight = (float*)&offset;
                    }
                    

                    buffer.CopyFromInto(
                        IntPtr.Add(deform.dw, deform.totweight))
                }
            }

            // We need to flatten the MDeformWeights into a single array to 
            // perform a single memcmp to determine if we need to send deltas.
            // Or just two levels of memcmp across the board? Not great, but it'd
            // give me a smaller window ... like:



            var deformVerts = new NativeArray<MDeformVert>(mesh.dvert, vertexCount);
            var deformWeights = new NativeArray<MDeformWeight>[vertexCount];



            // copy and memcmp deformVerts, if different, rebuild all and skip step 2.
            // step 2. copy and memcmp each weight.
            for (int i = 0; i < vertexCount; i++)
            {
                deformWeights.Equals(ptr, 5);
            }


            
            // TODO: Calculate.
            IntPtr ptr = IntPtr.Zero;
            int count = 0;

            var arr = new NativeArray<MDeformWeight>(ptr, count);
            return arr;
        }

        public static Mesh PtrToMesh(string blenderVersion, IntPtr ptr)
        {
            // The other problem is the CustomData layer layout. See notes in the struct
            // But if it's also going to change - that's another headache.
            // Maybe instead of structs we just fix constants to versions and byte offsets
            // and do some pointer math?

            // Or maybe something (dumber) where we iterate through memory until
            // we find "safe" values (vertex counts, strings, etc) and then read
            // from an offset there? Dumb, dangerous, etc.

            if (!Version.TryParse(blenderVersion, out Version version))
            {
                throw new Exception($"Cannot parse Blender version [{blenderVersion}]");
            }

            // The above doesn't really help with fetching the appropriate stuff from the mesh.


            return FastStructure.PtrToStructure<Mesh>(ptr);

            /*FastStructure<Mesh<ID_CompatLatest>>.PtrToStructure(src);


            var mesh = GetCompatMesh();
            // boxed struct of whatever size.

            Type generic = typeof(FastStructure<>);
            Type[] args = { typeof(Mesh<ID_CompatMaster>) };

            Type constructed = typeof(FastStructure<>).MakeGenericType(args);

            var fastStructureInstance = Activator.CreateInstance(constructed);
            var ptrToStructure = constructed.GetMethod("PtrToStructure");
            var boxedMesh = ptrToStructure.Invoke(fastStructureInstance, null);

            // Can't really access anything on it though since it's boxed...
            // I can read the size but who cares? I still need ldata/dvert
            */
        }


    }

    /// <summary>
    /// Blender's ID DNA struct.
    ///
    /// <para>
    ///     This is required to be laid out so that we can lay out <see cref="Mesh"/>.
    ///     Otherwise - this is unused.
    /// </para>
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/v2.90.1/source/blender/makesdna/DNA_ID.h#L259
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ID
    {
        public IntPtr next, prev;
        public IntPtr newid;

        public IntPtr lib;

        // 2/10/2021 MASTER ADD:
        // public IntPtr asset_data;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 66)]
        public char[] name;

        public ushort flag;

        public int tag;
        public int us;
        public int icon_id;
        public int recalc;

        public int recalc_up_to_undo_push;
        public int recalc_after_undo_push;

        public uint session_uuid;

        public IntPtr properties;
        public IntPtr override_library;
        public IntPtr orig_id;
        public IntPtr py_instance;

        // 2/10/2021 MASTER ADD:
        // public IntPtr _pad1;
    }

    /// <summary>
    /// Structure which stores custom element data associated with mesh
    /// elements (vertices, edges, faces). The custom data is organized
    /// into a series of layers, each with a data type (MTFace, MDeformVert, etc)
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_customdata_types.h#L70
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct CustomData
    {
        public IntPtr layers;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 47)]
        public int[] typemap;

        // 2/10/2021 MASTER:
        // [MarshalAs(UnmanagedType.ByValArray, SizeConst = 51)]
        // public int[] typemap;

        // 2/10/2021 2.90.0 TAG:
        // [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        // public int[] typemap;
        // [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        // public char[] _pad;

        public int totlayer, maxlayer;

        public int totsize;

        public IntPtr pool;
        public IntPtr external;
    }

    /// <summary>
    /// Mesh vertices
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L42
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MVert
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public float[] co;
        public float co_x;
        public float co_y;
        public float co_z;

        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public short[] no;
        public short no_x;
        public short no_y;
        public short no_z;

        public char flag;
        public char bweight;
    }

    /// <summary>
    /// Mesh loops
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L114
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoop
    {
        public uint v;
        public uint e;
    }

    /// <summary>
    /// UV coordinate for a polygon face and flag for selection and other options.
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L114
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopUV : IInteropConvertible<InteropVector2>
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        //public float[] uv;
        public float u;
        public float v;

        public int flag;

        public InteropVector2 ToInterop()
        {
            return new InteropVector2(u, v);
        }

        public bool Equals(InteropVector2 vec)
        {
            return u == vec.x && v == vec.y;
        }
    }

    /// <summary>
    /// Lightweight triangulation data
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L248
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopTri
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public uint[] tri;
        public uint tri_0;
        public uint tri_1;
        public uint tri_2;

        public uint poly;
    }

    /// <summary>
    /// Vertex color data
    ///
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L346
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopCol : IInteropConvertible<InteropColor32>
    {
        public byte r, g, b, a;

        public InteropColor32 ToInterop()
        {
            return new InteropColor32(r, g, b, a);
        }

        public bool Equals(InteropColor32 col)
        {
            return r == col.r
                && g == col.g
                && b == col.b
                && a == col.a;
        }
    }
    
    /// <summary>
    /// Vertex group index and weight
    /// 
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L284
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MDeformWeight
    {
        public int def_nr;
        public float weight;
    }
    
    /// <summary>
    /// Weight deformations per vertex - accessed through <see cref="Mesh.dvert"/>.
    /// 
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_meshdata_types.h#L291
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MDeformVert
    {
        /// <summary>
        /// Pointer to an array of <see cref="MDeformWeight"/>.
        /// </summary>
        public IntPtr dw;

        public int totweight;
        public int flag;
    }

    // This is only really needed for custom split normals.
    // Via Mesh->ldata layer
    // But that means we need to fill out Mesh all the way down
    // to the ldata entry (which means filling out ID, etc)
    // And then pull the layer from CustomData->layers
    // and requiring CustomData->typemap

    /// <summary>
    /// Blender's Mesh DNA struct. Used for:
    /// <list type="number">
    ///     <item>
    ///         Directly extracting vertex weights through <see cref="Mesh.dvert"/>
    ///         without going through slow Python iteration of vertices.
    ///     </item>
    ///     <item>
    ///         Accessing custom split normals through <see cref="Mesh.ldata"/>
    ///         which is also too slow to access through Python.
    ///     </item>
    /// </list>
    /// <para>
    ///     Reference: https://github.com/sobotka/blender/blob/master/source/blender/makesdna/DNA_mesh_types.h#L132
    /// </para>
    /// </summary>
    /// <typeparam name="ID">Blender DNA ID type. Adjustable to change compatibility.</typeparam>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct Mesh
    {
        // ID is explicitly removed here to support different Blender compatibilities.
        // As the ID struct can change size - we want to dynamically skip it while
        // pulling the Mesh data based on what version of Blender is being used.

        // public ID id;

        // Animation data
        public IntPtr adt;

        // Old animation system, deprecated for 2.5.
        public IntPtr ipo;
        public IntPtr key;
        public IntPtr mat;
        public IntPtr mselect;

        // BMESH ONLY
        public IntPtr mpoly;
        public IntPtr mloop;
        public IntPtr mloopuv;
        public IntPtr mloopcol;
        // END BMESH ONLY

        // Legacy face storage (quads & tries only),
        // faces are now stored in Mesh.mpoly & Mesh.mloop arrays.
        public IntPtr mface;
        public IntPtr mtface;
        public IntPtr tface;
        public IntPtr mvert;
        public IntPtr medge;
        public IntPtr dvert;

        // Array of colors for tessellated faces, must be number of
        // tessellated faces * 4 in length
        public IntPtr mcol;
        public IntPtr texcomesh;

        public IntPtr edit_mesh;

        public CustomData vdata;
        public CustomData edata;
        public CustomData fdata;

        // BMESH ONLY
        public CustomData pdata;
        public CustomData ldata;
        // END BMESH ONLY

        public int totvert;
        public int totedge;
        public int totface;
        public int totselect;

        // BMESH ONLY
        public int totpoly;
        public int totloop;
        // END BMESH ONLY

        // ... etc. Not included as we don't access it nor do
        // we use arrays of Mesh, so we don't need actual size.
    }
}
