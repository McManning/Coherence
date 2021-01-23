using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Coherence
{
    /// <summary>
    /// A distinct object in a blender scene
    /// </summary>
    class SceneObject : IInteropSerializable<InteropSceneObject>
    {
        /// <summary>
        /// Data that will be shared with Unity
        /// </summary>
        internal InteropSceneObject data;

        /// <summary>
        /// Should we attempt to optimize mesh data (combine vertices)
        /// or just send full loops as-is to Unity
        /// </summary>
        internal bool optimize;

        public string Name { get; set; }

        /// <summary>
        /// Name of the active material for this object
        /// </summary>
        internal string Material
        {
            get { return data.material; }
            set { data.material = value; }
        }

        internal InteropMatrix4x4 Transform
        {
            get { return data.transform; }
            set { data.transform = value; }
        }

        internal uint[] Triangles
        {
            get { return _triangles; }
        }

        private uint[] _triangles;

        internal InteropVector3[] Vertices
        {
            get { return _vertices; }
        }

        private InteropVector3[] _vertices;

        internal InteropVector3[] Normals
        {
            get { return _normals; }
        }

        private InteropVector3[] _normals;

        internal InteropVector2[] UV
        {
            get { return _uvs.Count > 0 ? _uvs[0] : null; }
        }

        internal InteropVector2[] UV2
        {
            get { return _uvs.Count > 1 ? _uvs[1] : null; }
        }

        internal InteropVector2[] UV3
        {
            get { return _uvs.Count > 2 ? _uvs[2] : null; }
        }

        internal InteropVector2[] UV4
        {
            get { return _uvs.Count > 3 ? _uvs[3] : null; }
        }

        internal InteropColor32[] Colors
        {
            get { return _colors; }
        }

        private InteropColor32[] _colors;

        internal InteropBoneWeight[] BoneWeights { get; set; }

        private Dictionary<int, InteropVector2[]> uvLayers;
        private MLoop[] cachedLoops;

        internal SceneObject(string name, SceneObjectType type)
        {
            Name = name;

            data = new InteropSceneObject
            {
                name = name,
                type = type,
                material = "Default"
            };

            uvLayers = new Dictionary<int, InteropVector2[]>();
        }

        /// <summary>
        /// Copy an <see cref="MLoop"/> array into managed memory for vertex lookups
        /// aligned with other MLoop* structures.
        /// </summary>
        /// <param name="loops"></param>
        internal void CopyFromMLoops(MLoop[] loops)
        {
            if (cachedLoops == null)
            {
                cachedLoops = new MLoop[loops.Length];
            }
            else
            {
                Array.Resize(ref cachedLoops, loops.Length);
            }

            Array.Copy(loops, cachedLoops, loops.Length);

            InteropLogger.Debug($"Copy {loops.Length} loops");
        }

        /// <summary>
        /// Copy an <see cref="MVert"/> array into <see cref="Vertices"/> and <see cref="Normals"/>.
        /// </summary>
        /// <param name="verts"></param>
        internal void CopyFromMVerts(MVert[] verts)
        {
            var count = verts.Length;
            if (_vertices == null)
            {
                _vertices = new InteropVector3[count];
            }

            if (_normals == null)
            {
                _normals = new InteropVector3[count];
            }

            Array.Resize(ref _vertices, count);
            Array.Resize(ref _normals, count);

            // We're copying the data instead of sending directly into shared memory
            // because we cannot guarantee that:
            //  1.  shared memory will be available for write when this is called
            //  2.  the source MVert array hasn't been reallocated once shared
            //      memory *is* available for write

            // TODO: "Smart" dirtying.
            // If verts don't change, don't flag for updates.
            // Or if verts change, only update a region of the verts.
            // Could do a BeginChangeCheck() ... EndChangeCheck() with
            // a bool retval if changes happened.

            // The idea is to do as much work as possible locally within
            // Blender to transmit the smallest deltas to Unity.

            // On a resize - this is just everything.
            int rangeStart = count;
            int rangeEnd = 0;
            int changedVertexCount = 0;

            for (int i = 0; i < count; i++)
            {
                var co = verts[i].co;
                var no = verts[i].no;

                var newVert = new InteropVector3(co[0], co[1], co[2]);

                // Normals need to be cast from short -> float
                var newNorm = new InteropVector3(no[0] / 32767f, no[1] / 32767f, no[2] / 32767f);

                if (!newVert.Approx(_vertices[i]) || !newNorm.Approx(_normals[i]))
                {
                    rangeStart = Math.Min(rangeStart, i);
                    rangeEnd = Math.Max(rangeEnd, i);
                    changedVertexCount++;
                }

                _vertices[i] = newVert;
                _normals[i] = newNorm;

                // TECHNICALLY I can collect an index list of what changed and send just
                // that to Unity - but is it really worth the extra effort? We'll see!

                // Console.WriteLine($" - v={vertices[i]}, n={normals[i]}");

                // Other issue is that changes may affect index 0, 1, and 1000, 1001 for a quad.
                // Or a change has shifted vertices around in the array due to some sort of
                // esoteric Blender operator. So to play it safe, we just update the whole array
                // until it can be guaranteed that we can identify an accurate slice of updates.
            }

            InteropLogger.Debug(
                $"** Changed {changedVertexCount} vertices within index range " +
                $"[{rangeStart}, {rangeEnd}] covering {rangeEnd - rangeStart} vertices"
            );
        }

        /// <summary>
        /// Copy an <see cref="MLoopTri"/> array into <see cref="Triangles"/>
        ///
        /// <para>
        ///     You <b>must</b> ensure <see cref="cachedLoops"/> is up to date via
        ///     <see cref="CopyFromMVerts(MVert[])"/> before performing this
        ///     operation to ensure that vertices can be mapped to.
        /// </para>
        /// </summary>
        /// <param name="loopTris"></param>
        internal void CopyFromMLoopTris(MLoopTri[] loopTris)
        {
            var count = loopTris.Length * 3;
            if (_triangles == null)
            {
                _triangles = new uint[count];
            }

            Array.Resize(ref _triangles, count);

            // Triangle indices are re-mapped from MLoop vertex index
            // to the source vertex index using cachedLoops.
            for (int i = 0; i < loopTris.Length; i++)
            {
                var loop = loopTris[i];
                var j = i * 3;

                // TODO: Can this be any faster? This indirect lookup sucks,
                // but might be the best we can do with the way Blender
                // organizes data.
                _triangles[j] = cachedLoops[loop.tri[0]].v;
                _triangles[j + 1] = cachedLoops[loop.tri[1]].v;
                _triangles[j + 2] = cachedLoops[loop.tri[2]].v;

                // Console.WriteLine($" - {triangles[j]}, {triangles[j + 1]},  {triangles[j + 2]}");
            }

            InteropLogger.Debug($"Copied {_triangles.Length} triangle indices");
        }

        /// <summary>
        /// Copy an <see cref="MLoopUV"/> array for <see cref="GetUV(int)"/>.
        ///
        /// <para>
        ///     You <b>must</b> ensure <see cref="cachedLoops"/> is up to date via
        ///     <see cref="CopyFromMVerts(MVert[])"/> before performing this
        ///     operation to ensure that vertices can be mapped to.
        /// </para>
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="loopUVs"></param>
        internal void CopyFromMLoopUV(int layer, MLoopUV[] loopUVs)
        {
            var count = _vertices.Length;

            if (!uvLayers.TryGetValue(layer, out InteropVector2[] uv))
            {
                uv = new InteropVector2[count];
            }

            Array.Resize(ref uv, count);
            uvLayers[layer] = uv;

            for (int i = 0; i < loopUVs.Length; i++)
            {
                // Note that this currently does not support
                // different UVs in the same layer for the same vertex
                // since we crush them down and don't try to split verts.
                // TODO: Improve on this.
                var v = cachedLoops[i].v; // Vertex index
                uv[v] = new InteropVector2(loopUVs[i].uv);
            }
        }

        public InteropSceneObject Serialize()
        {
            return data;
        }

        public void Deserialize(InteropSceneObject interopData)
        {
            throw new InvalidOperationException();
        }

        List<InteropVector2[]> _uvs;

        /// <summary>
        /// Reallocate storage space for mesh data - avoiding actual allocations
        /// and keeping existing data in-place as much as possible
        /// </summary>
        /// <param name="vertexCount"></param>
        /// <param name="triangleCount"></param>
        /// <param name="uvLayerCount"></param>
        private void Reallocate(int vertexCount, int triangleCount, int uvLayerCount, bool hasColors)
        {
            if (_triangles == null)
            {
                _triangles = new uint[triangleCount * 3];
            }
            else
            {
                Array.Resize(ref _triangles, triangleCount * 3);
            }

            if (_vertices == null)
            {
                _vertices = new InteropVector3[vertexCount];
            }
            else
            {
                Array.Resize(ref _vertices, vertexCount);
            }

            if (_normals == null)
            {
                _normals = new InteropVector3[vertexCount];
            }
            else
            {
                Array.Resize(ref _normals, vertexCount);
            }

            if (hasColors)
            {
                if (_colors == null)
                {
                    _colors = new InteropColor32[vertexCount];
                }
                else
                {
                    Array.Resize(ref _colors, vertexCount);
                }
            }
            else // Deallocate
            {
                _colors = null;
            }

            if (_uvs == null)
            {
                _uvs = new List<InteropVector2[]>();
            }
            else if (_uvs.Count > uvLayerCount)
            {
                // Remove excess UVs
                _uvs.RemoveRange(uvLayerCount, _uvs.Count - uvLayerCount);
            }

            for (int layer = 0; layer < uvLayerCount; layer++)
            {
                // Fill the list if we added more UV layers
                if (_uvs.Count <= layer)
                {
                    _uvs.Add(new InteropVector2[vertexCount]);
                }
                else // Ensure there's enough storage space for UV data
                {
                    var uv = _uvs[layer];
                    InteropLogger.Debug($"Resize uvs[{layer}] from {uv.Length} to {vertexCount}");

                    Array.Resize(ref uv, vertexCount);
                    _uvs[layer] = uv;

                    InteropLogger.Debug($"Post resize: {_uvs[layer].Length}");

                }
            }
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
        internal void CopyMeshData(
            MVert[] verts,
            MLoop[] loops,
            MLoopTri[] loopTris,
            MLoopCol[] loopCols,
            List<MLoopUV[]> loopUVs
        ) {
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
        }

        // Raw data from Blender - cached for later memcmp calls
        ArrayBuffer<MLoop> loops;
        ArrayBuffer<MVert> verts;
        ArrayBuffer<MLoopTri> loopTris;
        ArrayBuffer<MLoopCol> loopCols;
        // ... and so on

        // Buffers converted from Blender data to an interop format
        ArrayBuffer<InteropVector3> vertices;
        ArrayBuffer<InteropVector3> normals;
        ArrayBuffer<InteropColor32> colors;
        List<ArrayBuffer<InteropVector2>> uvs;
        // ... and so on
        ArrayBuffer<int> triangles;

        /// <summary>
        /// Mapping an index in <see cref="loops"/> to a split vertex index in <see cref="vertices"/>
        /// </summary>
        Dictionary<int, int> splitVertices = new Dictionary<int, int>();

        internal void CopyMeshData_V2(
            MVert[] verts,
            MLoop[] loops,
            MLoopTri[] loopTris,
            MLoopCol[] loopCols,
            List<MLoopUV[]> loopUVs
        ) {
            bool dirtyVertices = false;
            bool dirtyNormals = false;
            bool dirtyColors = false;
            bool dirtyTriangles = false;

            // A change of unique vertex count or a change to the primary
            // loop mapping (loop index -> vertex index) requires a full rebuild.
            if (verts.Length != this.verts.Length || !this.loops.Equals(loops))
            {
                this.loops.CopyFrom(loops);
                this.verts.CopyFrom(verts);
                this.loopCols.CopyFrom(loopCols);
                // ... and so on

                this.loopTris.CopyFrom(loopTris);

                RebuildAll();
                SendAll();
                return;
            }

            int addedVertices = 0;

            // Rebuild dirtied channels and aggregate how many
            // split vertices we needed to add while rebuilding
            if (!this.verts.Equals(verts))
            {
                this.verts.CopyFrom(verts);
                addedVertices += RebuildVertices();
                addedVertices += RebuildNormals();
                dirtyVertices = true;
                dirtyNormals = true;
            }

            if (!this.loopCols.Equals(loopCols))
            {
                this.loopCols.CopyFrom(loopCols);
                // addedVertices += RebuildColors();
                addedVertices += RebuildBuffer(this.loopCols, colors);
                dirtyColors = true;
            }

            // ... and so on


            // If any of the channels created new split vertices
            // we need to rebuild the full triangle buffer to re-map
            // old loop triangle indices to new split vertex indices
            if (addedVertices > 0 || !this.loopTris.Equals(loopTris))
            {
                this.loopTris.CopyFrom(loopTris);
                RebuildTriangles();
                dirtyTriangles = true;
            }

            // Send each updated channel to Unity.

            // If a channel was dirtied - we send the whole thing.
            // Otherwise if we added new split verts triggered by an adjacent
            // channel's update - we send just the new vertex data.

            // Example: if the user updates vertex colors and causes 20 new split
            // vertices to be added to a 30k vertex model, we'll send a buffer
            // of 30k vertex colors and then 20 vertices, 20 normals, 20 uvs, [etc]
            // instead of sending 30k values per-channel.
            int offset = vertices.Length - addedVertices;

            if (dirtyVertices)
                SendBuffer(RpcRequest.UpdateVertices, vertices);
            else if (addedVertices > 0)
                SendBuffer(RpcRequest.UpdateVertices, vertices, offset);

            if (dirtyNormals)
                SendBuffer(RpcRequest.UpdateNormals, normals);
            else if (addedVertices > 0)
                SendBuffer(RpcRequest.UpdateNormals, normals, offset);

            if (dirtyColors)
                SendBuffer(RpcRequest.UpdateVertexColors, colors);
            else if (addedVertices > 0)
                SendBuffer(RpcRequest.UpdateVertexColors, colors, offset);

            // ... and so on

            if (dirtyTriangles)
                SendBuffer(RpcRequest.UpdateTriangles, triangles);

            SendApplyChanges();
        }

        void RebuildAll()
        {
            splitVertices.Clear();

            RebuildVertices();
            RebuildNormals();
            // RebuildColors();
            RebuildBuffer(loopCols, colors);
            // .. and so on

            RebuildTriangles();
        }

        int RebuildVertices()
        {
            // We resize to the number of vertices from Blender
            // PLUS the number of split vertices we already calculated
            // in a prior pass (thus already have the data filled out)
            vertices.Resize(verts.Length + splitVertices.Count);

            for (int i = 0; i < verts.Length; i++)
            {
                vertices[i] = new InteropVector3(verts[i].co);
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

            // Normals need to be cast from short -> float from Blender
            var normalScale = 1f / 32767f;

            for (int i = 0; i < verts.Length; i++)
            {
                var no = verts[i].no;

                normals[i] = new InteropVector3(
                    no[0] * normalScale,
                    no[1] * normalScale,
                    no[2] * normalScale
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

            for (int t = 0; t < loopTris.Length; t++)
            {
                for (int i = 0; i < 2; i++)
                {
                    int loopIndex = (int)loopTris[t].tri[i];

                    // If the triangle vert has been mapped to a split vertex,
                    // use that instead of the original vertex
                    if (splitVertices.ContainsKey(loopIndex))
                    {
                        triangles[t * 3 + i] = splitVertices[loopIndex];
                    }
                    else
                    {
                        triangles[t * 3 + i] = (int)loops[loopIndex].v;
                    }
                }
            }
        }

        int RebuildColors()
        {
            // No vertex color data - clear everything.
            if (loopCols == null)
            {
                colors.Clear();
                return 0;
            }

            colors.Resize(vertices.Length);

            int splitCount = 0;
            var written = new bool[colors.Length];

            // Determine which vertices need to split and add entries
            for (int i = 0; i < loopCols.Length; i++)
            {
                var col = loopCols[i];
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
                    var prevCol = colors[vertIndex];

                    // InteropColor32.Equals(OtherType)
                    if (
                        prevCol.r != col.r ||
                        prevCol.g != col.g ||
                        prevCol.b != col.b ||
                        prevCol.a != col.a
                    ) {
                        // Split and use the new index
                        vertIndex = Split(i);
                        splitCount++;
                    }
                }
                else
                {
                    written[vertIndex] = true;
                }

                // new T(loopvalue)
                colors[vertIndex] = new InteropColor32(col.r, col.g, col.b, col.a);
            }

            return splitCount;
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
            ArrayBuffer<TSource> source,
            ArrayBuffer<TTarget> target
        )
        where TSource : struct, IInteropConvertible<TTarget>
        where TTarget : struct
        {
            // No data in this buffer - clear target.
            if (source.IsEmpty)
            {
                target.Clear();
                return 0;
            }

            // Match the target channel to the vertex length
            target.Resize(vertices.Length);

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
            // ... and so on

            return newIndex;
        }

        void SendAll()
        {
            SendBuffer(RpcRequest.UpdateVertices, vertices);
            SendBuffer(RpcRequest.UpdateNormals, normals);
            SendBuffer(RpcRequest.UpdateVertexColors, colors);
            // ... and so on

            SendBuffer(RpcRequest.UpdateTriangles, triangles);

            SendApplyChanges();
        }

        /// <summary>
        /// Send the buffer to Unity if it is non-empty.
        ///
        /// If <paramref name="offset"/> is defined, only the buffer starting at offset will be sent.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        void SendBuffer<T>(RpcRequest request, ArrayBuffer<T> buffer, int offset = 0) where T : struct
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            // TODO: If it existed previous and was removed,
            // we need to send something for that.
            // Maybe apply changes lists all active channels?
            // Or it just sends a 0 length array (seems less efficient though)

            // TODO: Send.

            // TODO: This can be in the bridge as a method instead.
        }

        void SendApplyChanges()
        {

        }
    }

    public class LowLevel
    {
        #pragma warning disable IDE1006 // Naming Styles

        // Via: https://stackoverflow.com/a/1445405
        [DllImport("msvcrt.dll", CallingConvention=CallingConvention.Cdecl)]
        public static extern int memcmp(byte[] b1, byte[] b2, long count);

        #pragma warning restore IDE1006 // Naming Styles
    }

    public class ArrayBuffer<T> where T : struct
    {
        T[] data;

        public int Length {
            get => data.Length;
        }

        public bool IsEmpty
        {
            get => data == null || data.Length < 1;
        }

        public bool Equals(T[] other)
        {
            // Alternatively - SequenceEqual with spans
            // (see: https://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net)
            // but that's assuming .NET support that we don't necessarily have atm
            return other.Length == data.Length
                && LowLevel.memcmp(other as byte[], data as byte[], other.Length) == 0;
        }

        public void CopyFrom(T[] other)
        {
            // do work copying into .data
        }

        /// <summary>
        /// Add a new T to the end of the buffer - resizing where necessary
        /// </summary>
        public void Add(T value)
        {
            // Do the thing that List<> does.
        }

        /// <summary>
        /// Add a new <typeparamref name="T"/> to the end of the buffer
        /// - copied from the specified index
        /// </summary>
        /// <param name="index"></param>
        public void AppendCopy(int index)
        {
            if (!IsEmpty)
            {
                Add(data[index]);
            }
        }

        public T this[int index]
        {
            get => data[index];
            set => data[index] = value;
        }

        public void Resize(int length)
        {
            //TODO
        }

        public void Clear()
        {
            data = null;
        }
    }
}
