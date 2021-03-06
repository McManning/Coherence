﻿using System;
using System.Collections.Generic;
using Coherence.BlenderDNA;

namespace Coherence
{
    internal static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default) {
            if (dict.ContainsKey(key))
            {
                return dict[key];
            }

            return defaultValue;
        }
    }

    /// <summary>
    /// A distinct mesh in a Blender scene referenced by one or more objects
    /// </summary>
    class Mesh : IInteropSerializable<InteropMesh>
    {
        /// <summary>
        /// Data that will be shared with Unity
        /// </summary>
        internal InteropMesh data;

        public string Name => data.name;

        internal Mesh(string name)
        {
            data.name = name;
        }

        public InteropMesh Serialize()
        {
            return data;
        }

        public void Deserialize(InteropMesh interopData)
        {
            throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy all mesh data - aligned to loops (thus no combination of like-vertices)
        /// </summary>
        internal void CopyMeshDataAlignedtoLoops(
            MVert[] verts,
            MLoop[] loops,
            MLoopTri[] loopTris,
            MLoopCol[] loopCols,
            List<MLoopUV[]> loopUVs
        ) {
            #if LEGACY
            Reallocate(loops.Length, loopTris.Length, loopUVs.Count, loopCols != null);

            // Normals need to be cast from short -> float from Blender
            var normalScale = 1f / 32767f;

            for (int i = 0; i < loops.Length; i++)
            {
                var co = verts[loops[i].v].co;
                var no = verts[loops[i].v].no;

                _vertices[i] = new InteropVector3(co[0], co[1], co[2]);
                _normals[i] = new InteropVector3(
                    no[0] * normalScale,
                    no[1] * normalScale,
                    no[2] * normalScale
                );

                // Copy UV layers - same length as loops
                for (int layer = 0; layer < loopUVs.Count; layer++)
                {
                    _uvs[layer][i] = new InteropVector2(
                        loopUVs[layer][i].uv
                    );
                }

                // Copy vertex colors - same length as loops
                if (loopCols != null)
                {
                    var col = loopCols[i];
                    _colors[i] = new InteropColor32(
                        col.r,
                        col.g,
                        col.b,
                        col.a
                    );
                }
            }

            // Copy triangles
            for (uint t = 0; t < loopTris.Length; t++)
            {
                for (uint i = 0; i < 3; i++)
                {
                    _triangles[(t * 3) + i] = loopTris[t].tri[i];
                }
            }
            #endif
        }

        /// <summary>
        /// Copy all mesh data from Blender in one go and optimize down as much as possible.
        /// </summary>
        /// <remarks>
        ///     Reference: LuxCoreRender/LuxCore::Scene_DefineBlenderMesh
        ///     for the logic dealing with split normals / UVs / etc.
        /// </remarks>
        /// <param name="verts"></param>
        /// <param name="loops"></param>
        /// <param name="loopTris"></param>
        /// <param name="loopUVs"></param>
        /// <param name="loopCols"></param>
        internal void CopyMeshData_V1(
            MVert[] verts,
            MLoop[] loops,
            MLoopTri[] loopTris,
            MLoopCol[] loopCols,
            List<MLoopUV[]> loopUVs
        ) {
            #if LEGACY
            // In the case of split vertices - this'll resize DOWN
            // and then resize UP again for split vertices.

            Reallocate(verts.Length, loopTris.Length, loopUVs.Count, loopCols != null);

            var normalScale = 1f / 32767f;

            // Copy in vertex coordinates and normals
            for (int i = 0; i < verts.Length; i++)
            {
                var co = verts[i].co;
                var no = verts[i].no;

                _vertices[i] = new InteropVector3(co[0], co[1], co[2]);

                // Normals need to be cast from short -> float from Blender
                _normals[i] = new InteropVector3(
                    no[0] * normalScale,
                    no[1] * normalScale,
                    no[2] * normalScale
                );
            }

            // Copy UV layers
            for (int layer = 0; layer < loopUVs.Count; layer++)
            {
                var uvLayer = _uvs[layer];
                for (uint i = 0; i < loopUVs[layer].Length; i++)
                {
                    var vertIndex = loops[i].v;

                    // This will overwrite itself for shared vertices - that's fine.
                    // We'll be handling split UVs when reading in triangle data.
                    uvLayer[vertIndex] = new InteropVector2(
                        loopUVs[layer][i].uv
                    );
                }
            }

            // Copy vertex colors if we got 'em
            if (loopCols != null)
            {
                for (uint i = 0; i < loopCols.Length; i++)
                {
                    var vertIndex = loops[i].v;
                    var col = loopCols[i];
                    _colors[vertIndex] = new InteropColor32(
                        col.r,
                        col.g,
                        col.b,
                        col.a
                    );
                }
            }

            // Track what triangle vertices need to be split.
            // This maps an index in `triangles` to an index in `loops`
            var splitTris = new Dictionary<uint, uint>();

            // Generate triangle list while identifying any vertices that will need
            // to be split - due to having split UVs, normals, etc in the loop data.
            for (uint t = 0; t < loopTris.Length; t++)
            {
                for (uint i = 0; i < 3; i++)
                {
                    var loopIndex = loopTris[t].tri[i];
                    var vertIndex = loops[loopIndex].v;

                    var split = false; // Assumes .v is already < verts.Length

                    // TODO: Test differing normals - not applicable
                    // here as normals are only read in from MVert

                    // Determine if we should make a new vertex for split UVs
                    for (int layer = 0; layer < loopUVs.Count && !split; layer++)
                    {
                        var loopUV = loopUVs[layer][loopIndex].uv;
                        var vertUV = _uvs[layer][vertIndex];
                        // TODO: Handle floating point errors?
                        if (loopUV[0] != vertUV.x || loopUV[1] != vertUV.y)
                        {
                            split = true;
                        }
                    }

                    // If we have vertex colors, check for split colors
                    if (loopCols != null)
                    {
                        var col = loopCols[loopIndex];
                        var vertCol = _colors[vertIndex];

                        if (col.r != vertCol.r ||
                            col.g != vertCol.g ||
                            col.b  != vertCol.b ||
                            col.a != vertCol.a
                        ) {
                            split = true;
                        }
                    }

                    _triangles[(t * 3) + i] = vertIndex;

                    // Track if we need to split the vertex in the triangle
                    // to a new one once we've iterated through everything
                    if (split)
                    {
                        splitTris.Add((t * 3) + i, loopIndex);
                    }
                }
            }

            // 7958 + 32245 = 40203
            // LOOPS are 31488

            // 7958 * 3 = 23874
            // 15744 loop triangles
            // 31488 vertex color indices

            // If we have triangle verts to split - apply all at once so there's
            // only a single re-allocation to our arrays.
            var totalNewVertices = splitTris.Count;
            if (totalNewVertices > 0)
            {
                InteropLogger.Debug($"Splitting {totalNewVertices} vertices");
                var newVertIndex = (uint)verts.Length;

                // Reallocate everything to fit the new set of vertices
                Reallocate(verts.Length + totalNewVertices, loopTris.Length, loopUVs.Count, loopCols != null);

                // Generate new vertices with any split data (normals, UVs, colors, etc)
                foreach (var tri in splitTris.Keys)
                {
                    var prevVertIndex = _triangles[tri]; // MVert index
                    var loopIndex = splitTris[tri]; // MLoop index

                    // Same coordinates as the original vertex
                    _vertices[newVertIndex] = _vertices[prevVertIndex];

                    // TODO: If there were split normals, that'd be handled here.
                    _normals[newVertIndex] = _normals[prevVertIndex];

                    // Read UVs from loops again to handle any split UVs
                    for (int layer = 0; layer < loopUVs.Count; layer++)
                    {
                        var uv = loopUVs[layer][loopIndex].uv;

                        _uvs[layer][newVertIndex] = new InteropVector2(uv);
                    }

                    // Same deal for vertex colors - copy from the loop
                    if (loopCols != null)
                    {
                        var col = loopCols[loopIndex];
                        _colors[newVertIndex] = new InteropColor32(
                            col.r,
                            col.g,
                            col.b,
                            col.a
                        );
                    }

                    // And finally update the triangle to point to the new vertex
                    _triangles[tri] = newVertIndex;
                    newVertIndex++;
                }
            }
            #endif
        }

        // Raw data from Blender - cached for later memcmp calls
        NativeArray<MLoop> loops = new NativeArray<MLoop>();
        NativeArray<MVert> verts = new NativeArray<MVert>();
        NativeArray<MLoopTri> loopTris = new NativeArray<MLoopTri>();
        NativeArray<MLoopCol> loopCols = new NativeArray<MLoopCol>();
        NativeArray<MLoopUV> loopUVs = new NativeArray<MLoopUV>();

        // ... and so on

        // Buffers converted from Blender data to an interop format
        ArrayBuffer<InteropVector3> vertices = new ArrayBuffer<InteropVector3>();
        ArrayBuffer<InteropVector3> normals = new ArrayBuffer<InteropVector3>();
        ArrayBuffer<InteropColor32> colors = new ArrayBuffer<InteropColor32>();
        ArrayBuffer<InteropVector2> uvs = new ArrayBuffer<InteropVector2>();

        // ... and so on

        ArrayBuffer<int> triangles = new ArrayBuffer<int>();

        /// <summary>
        /// Mapping an index in <see cref="loops"/> to a split vertex index in <see cref="vertices"/>
        /// </summary>
        Dictionary<int, int> splitVertices = new Dictionary<int, int>();

        internal void CopyMeshDataNative(
            NativeArray<MVert> verts,
            NativeArray<MLoop> loops,
            NativeArray<MLoopTri> loopTris,
            NativeArray<MLoopCol> loopCols,
            NativeArray<MLoopUV> loopUVs
        ) {
            // A change of unique vertex count or a change to the primary
            // loop mapping (loop index -> vertex index) requires a full rebuild.
            // This will hit frequently for things like Metaballs that change often
            if (verts.Length != this.verts.Length || !this.loops.Equals(loops))
            {
                InteropLogger.Debug($"CopyMeshData - rebuild all");
                this.loops.CopyFrom(loops);
                this.verts.CopyFrom(verts);
                this.loopCols.CopyFrom(loopCols);
                this.loopUVs.CopyFrom(loopUVs);
                // ... and so on

                this.loopTris.CopyFrom(loopTris);

                RebuildAll();
                return;
            }

            int prevVertexCount = vertices.Length;

            // Trigger rebuild of buffers based on whether the Blender data changed.

            if (!this.verts.Equals(verts))
            {
                InteropLogger.Debug("CopyMeshData - !verts.Equals");
                this.verts.CopyFrom(verts);
                RebuildVertices();
                RebuildNormals();
            }

            if (!this.loopCols.Equals(loopCols))
            {
                InteropLogger.Debug("CopyMeshData - !colors.Equals");
                this.loopCols.CopyFrom(loopCols);
                RebuildBuffer(this.loopCols, colors);
            }

            if (!this.loopUVs.Equals(loopUVs))
            {
                InteropLogger.Debug("CopyMeshData - !uvs.Equals");
                this.loopUVs.CopyFrom(loopUVs);
                RebuildBuffer(this.loopUVs, uvs);
            }

            // ... and so on

            InteropLogger.Debug("CopyMeshData - Check triangles");

            // If any of the channels created new split vertices
            // we need to rebuild the full triangle buffer to re-map
            // old loop triangle indices to new split vertex indices
            if (prevVertexCount != vertices.Length || !this.loopTris.Equals(loopTris))
            {
                InteropLogger.Debug($"CopyMeshData - !loopTris.Equals or prev {prevVertexCount} != {vertices.Length}");

                this.loopTris.CopyFrom(loopTris);
                RebuildTriangles();
            }
        }

        void RebuildAll()
        {
            InteropLogger.Debug($"RebuildAll name={Name}");
            splitVertices.Clear();

            RebuildVertices();
            RebuildNormals();
            RebuildBuffer(loopCols, colors);
            RebuildBuffer(loopUVs, uvs);
            // .. and so on

            RebuildTriangles();
        }

        int RebuildVertices()
        {
            // We resize to the number of vertices from Blender
            // PLUS the number of split vertices we already calculated
            // in a prior pass (thus already have the data filled out)
            vertices.Resize(verts.Length + splitVertices.Count);

            InteropLogger.Debug($"RebuildVertices name={Name}, Length={vertices.Length}");

            for (int i = 0; i < verts.Length; i++)
            {
                var vert = verts[i];

                // y/z are swizzled here to convert to Unity's coordinate space
                vertices[i] = new InteropVector3(
                    vert.co_x,
                    vert.co_z,
                    vert.co_y
                );
            }

            // Also update all existing split vertices to match
            // the original vertex that may have been updated
            foreach (var kv in splitVertices)
            {
                var vertIndex = (int)loops[kv.Key].v;
                vertices[kv.Value] = vertices[vertIndex];
            }

            // This will never split
            return 0;
        }

        int RebuildNormals()
        {
            // We assume it always happens AFTER RebuildVertices
            normals.Resize(vertices.Length);

            InteropLogger.Debug($"RebuildNormals name={Name}, Length={normals.Length}");

            // Normals need to be cast from short -> float from Blender
            var normalScale = 1f / 32767f;

            for (int i = 0; i < verts.Length; i++)
            {
                var vert = verts[i];

                // Like vertex coordinates - we swizzle y/z
                normals[i] = new InteropVector3(
                    vert.no_x * normalScale,
                    vert.no_z * normalScale,
                    vert.no_y * normalScale
                );
            }

            // Also update all existing split vertices to match
            // the original normal that may have been updated
            foreach (var kv in splitVertices)
            {
                var vertIndex = (int)loops[kv.Key].v;
                normals[kv.Value] = normals[vertIndex];
            }

            // For now we have no splits. Eventually this may
            // change if we add support for split normals
            // coming from a CustomData layer
            return 0;
        }

        void RebuildTriangles()
        {
            triangles.Resize(loopTris.Length * 3);

            InteropLogger.Debug($"RebuildTriangles name={Name}, Length={triangles.Length}");

            for (int t = 0; t < loopTris.Length; t++)
            {
                var loopTri = loopTris[t];

                // Triangles are flipped due to coordinate space conversions
                // that happen from Blender to Unity
                int tri0 = (int)loopTri.tri_2;
                int tri1 = (int)loopTri.tri_1;
                int tri2 = (int)loopTri.tri_0;

                // If the triangle vert has been mapped to a split vertex,
                // use that instead of the original vertex
                triangles[t * 3 + 0] = splitVertices.GetValueOrDefault(tri0, (int)loops[tri0].v);
                triangles[t * 3 + 1] = splitVertices.GetValueOrDefault(tri1, (int)loops[tri1].v);
                triangles[t * 3 + 2] = splitVertices.GetValueOrDefault(tri2, (int)loops[tri2].v);
            }
        }

        /// <summary>
        /// Generic implementation of mapping an array of Blender structs to interop structs.
        ///
        /// <para>
        ///     This handles target buffer (re)allocation and vertex splitting
        ///     if the values we're reading from <paramref name="source"/> deviates
        ///     from the value already written to the same index.
        /// </para>
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TTarget"></typeparam>
        /// <param name="source">Array of loop structs from Blender. Must be aligned to <see cref="loops"/></param>
        /// <param name="target">Array of interop structs for Unity. Must be aligned to <see cref="vertices"/></param>
        /// <returns></returns>
        int RebuildBuffer<TSource, TTarget>(
            NativeArray<TSource> source,
            ArrayBuffer<TTarget> target
        )
        where TSource : struct, IInteropConvertible<TTarget>
        where TTarget : struct
        {
            // No data in this buffer - clear target.
            if (source.Length < 1)
            {
                target.Clear();
                return 0;
            }

            // Match the target channel to the vertex length
            target.Resize(vertices.Length);

            InteropLogger.Debug(
                $"RebuildBuffer name={Name}, Length={target.Length}, " +
                $"TSource={typeof(TSource)}, TTarget={typeof(TTarget)}"
            );

            int splitCount = 0;
            var written = new bool[target.Length];

            // Determine which vertices need to split and add entries
            for (int i = 0; i < source.Length; i++)
            {
                var val = source[i];
                int vertIndex = (int)loops[i].v;

                if (splitVertices.ContainsKey(i))
                {
                    // Already split - copy to the split index
                    vertIndex = splitVertices[i];
                }
                // vertIndex will always be < written.Length
                // because any indices outside of that range will
                // be found in the splitVertices map
                else if (written[vertIndex])
                {
                    // If we already wrote to this vertex once -
                    // determine if we should split from the original.
                    // We exclude any indices outside the initial size
                    // that were added by Split() here.
                    var prevVal = target[vertIndex];

                    if (!val.Equals(prevVal)) {
                        vertIndex = Split(i);
                        splitCount++;
                    }
                }
                else
                {
                    written[vertIndex] = true;
                }

                target[vertIndex] = val.ToInterop();
            }

            InteropLogger.Debug($"RebuildBuffer name={Name} - Split {splitCount} vertices");

            return splitCount;
        }

        /// <summary>
        /// Split a vertex referenced by <paramref name="loopIndex"/> into a new vertex.
        /// This will automatically resize all buffers to fit the new data and copy
        /// all channels into the new index.
        /// </summary>
        /// <param name="loopIndex"></param>
        /// <returns></returns>
        int Split(int loopIndex)
        {
            var vertIndex = (int)loops[loopIndex].v;
            var newIndex = vertices.Length;
            splitVertices[loopIndex] = newIndex;

            // Add a copy of each channel's vertex
            // data into the new split vertex
            vertices.AppendCopy(vertIndex);
            normals.AppendCopy(vertIndex);
            colors.AppendCopy(vertIndex);
            uvs.AppendCopy(vertIndex);
            // ... and so on

            return newIndex;
        }

        /// <summary>
        /// Send all buffers to Unity - regardless of dirtied status
        /// </summary>
        internal void SendAll()
        {
            var b = Bridge.Instance;

            InteropLogger.Debug($"SendAll name={Name}");

            b.SendArray(RpcRequest.UpdateVertices, Name, vertices);
            b.SendArray(RpcRequest.UpdateNormals, Name, normals);
            b.SendArray(RpcRequest.UpdateVertexColors, Name, colors);
            b.SendArray(RpcRequest.UpdateUV, Name, uvs);
            // ... and so on

            b.SendArray(RpcRequest.UpdateTriangles, Name, triangles);

            SendApplyChanges();

            // Clean everything - Unity should be synced.
            CleanAllBuffers();
        }

        /// <summary>
        /// Send all dirtied buffers to Unity
        /// </summary>
        internal void SendDirty()
        {
            var b = Bridge.Instance;

            // If a channel was dirtied - we send the range of elements modified.
            // A change to one channel may dirty others.
            // Example: if the user updates vertex colors and causes 20 new split
            // vertices to be added to a 30k vertex model, we'll send a buffer
            // of 30k vertex colors and then 20 vertices, 20 normals, 20 uvs, [etc]

            // TODO: Eventually will send just the dirtied fragments.
            // Need to add support on Unity's side.
            // E.g. b.SendArray(RpcRequest.UpdateVertices, Name, vertices.GetDirtyRange());

            if (vertices.IsDirty)
                b.SendArray(RpcRequest.UpdateVertices, Name, vertices);

            if (normals.IsDirty)
                b.SendArray(RpcRequest.UpdateNormals, Name, normals);

            if (colors.IsDirty)
                b.SendArray(RpcRequest.UpdateVertexColors, Name, colors);

            if (uvs.IsDirty)
                b.SendArray(RpcRequest.UpdateUV, Name, uvs);

            // ... and so on

            if (triangles.IsDirty)
                b.SendArray(RpcRequest.UpdateTriangles, Name, triangles);

            SendApplyChanges();

            CleanAllBuffers();
        }

        void CleanAllBuffers()
        {
            // Reset dirty status on buffers
            vertices.Clean();
            normals.Clean();
            colors.Clean();
            uvs.Clean();
            // ... and so on

            triangles.Clean();
        }

        /// <summary>
        /// Notify Unity that it's safe to apply any dirtied buffers to the mesh.
        ///
        /// This happens after any number of buffers have been sent
        /// </summary>
        void SendApplyChanges()
        {
            Bridge.Instance.SendEntity(RpcRequest.UpdateMesh, this);
        }
    }
}
