using System;
using System.Collections.Generic;

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
            get { return triangles; }
        }

        private uint[] triangles;

        internal InteropVector3[] Vertices
        {
            get { return vertices; }
        }

        private InteropVector3[] vertices;

        internal InteropVector3[] Normals
        {
            get { return normals; }
        }

        private InteropVector3[] normals;

        internal InteropVector2[] UV
        {
            get { return uvs.Count > 0 ? uvs[0] : null; }
        }

        internal InteropVector2[] UV2
        {
            get { return uvs.Count > 1 ? uvs[1] : null; }
        }

        internal InteropVector2[] UV3
        {
            get { return uvs.Count > 2 ? uvs[2] : null; }
        }

        internal InteropVector2[] UV4
        {
            get { return uvs.Count > 3 ? uvs[3] : null; }
        }

        internal InteropColor[] Colors
        {
            get { return colors; }
        }

        private InteropColor[] colors;

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
            if (vertices == null)
            {
                vertices = new InteropVector3[count];
            }

            if (normals == null)
            {
                normals = new InteropVector3[count];
            }

            Array.Resize(ref vertices, count);
            Array.Resize(ref normals, count);

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

                if (!newVert.Approx(vertices[i]) || !newNorm.Approx(normals[i]))
                {
                    rangeStart = Math.Min(rangeStart, i);
                    rangeEnd = Math.Max(rangeEnd, i);
                    changedVertexCount++;
                }

                vertices[i] = newVert;
                normals[i] = newNorm;

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
            if (triangles == null)
            {
                triangles = new uint[count];
            }

            Array.Resize(ref triangles, count);

            // Triangle indices are re-mapped from MLoop vertex index
            // to the source vertex index using cachedLoops.
            for (int i = 0; i < loopTris.Length; i++)
            {
                var loop = loopTris[i];
                var j = i * 3;

                // TODO: Can this be any faster? This indirect lookup sucks,
                // but might be the best we can do with the way Blender
                // organizes data.
                triangles[j] = cachedLoops[loop.tri[0]].v;
                triangles[j + 1] = cachedLoops[loop.tri[1]].v;
                triangles[j + 2] = cachedLoops[loop.tri[2]].v;

                // Console.WriteLine($" - {triangles[j]}, {triangles[j + 1]},  {triangles[j + 2]}");
            }

            InteropLogger.Debug($"Copied {triangles.Length} triangle indices");
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
            var count = vertices.Length;

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

        List<InteropVector2[]> uvs;

        /// <summary>
        /// Reallocate storage space for mesh data - avoiding actual allocations
        /// and keeping existing data in-place as much as possible
        /// </summary>
        /// <param name="vertexCount"></param>
        /// <param name="triangleCount"></param>
        /// <param name="uvLayerCount"></param>
        private void Reallocate(int vertexCount, int triangleCount, int uvLayerCount, bool hasColors)
        {
            if (triangles == null)
            {
                triangles = new uint[triangleCount * 3];
            }
            else
            {
                Array.Resize(ref triangles, triangleCount * 3);
            }

            if (vertices == null)
            {
                vertices = new InteropVector3[vertexCount];
            }
            else
            {
                Array.Resize(ref vertices, vertexCount);
            }

            if (normals == null)
            {
                normals = new InteropVector3[vertexCount];
            }
            else
            {
                Array.Resize(ref normals, vertexCount);
            }

            if (hasColors)
            {
                if (colors == null)
                {
                    colors = new InteropColor[vertexCount];
                }
                else
                {
                    Array.Resize(ref colors, vertexCount);
                }
            }
            else // Deallocate
            {
                colors = null;
            }

            if (uvs == null)
            {
                uvs = new List<InteropVector2[]>();
            }
            else if (uvs.Count > uvLayerCount)
            {
                // Remove excess UVs
                uvs.RemoveRange(uvLayerCount, uvs.Count - uvLayerCount);
            }

            for (int layer = 0; layer < uvLayerCount; layer++)
            {
                // Fill the list if we added more UV layers
                if (uvs.Count <= layer)
                {
                    uvs.Add(new InteropVector2[vertexCount]);
                }
                else // Ensure there's enough storage space for UV data
                {
                    var uv = uvs[layer];
                    InteropLogger.Debug($"Resize uvs[{layer}] from {uv.Length} to {vertexCount}");

                    Array.Resize(ref uv, vertexCount);
                    uvs[layer] = uv;

                    InteropLogger.Debug($"Post resize: {uvs[layer].Length}");

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

            var colorScale = 1f / 255f;
            var normalScale = 1f / 32767f;

            // Copy in vertex coordinates and normals
            for (int i = 0; i < verts.Length; i++)
            {
                var co = verts[i].co;
                var no = verts[i].no;

                vertices[i] = new InteropVector3(co[0], co[1], co[2]);

                // Normals need to be cast from short -> float from Blender
                normals[i] = new InteropVector3(
                    no[0] * normalScale,
                    no[1] * normalScale,
                    no[2] * normalScale
                );
            }

            // Copy UV layers
            for (int layer = 0; layer < loopUVs.Count; layer++)
            {
                var uvLayer = uvs[layer];
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
                    colors[vertIndex] = new InteropColor(
                        col.r * colorScale,
                        col.g * colorScale,
                        col.b * colorScale,
                        col.a * colorScale
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
                        var vertUV = uvs[layer][vertIndex];
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
                        var vertCol = colors[vertIndex];

                        // TODO: Handle floating point errors?
                        if (col.r * colorScale != vertCol.r ||
                            col.g * colorScale  != vertCol.g ||
                            col.b * colorScale  != vertCol.b ||
                            col.a * colorScale  != vertCol.a
                        ) {
                            split = true;
                        }
                    }

                    triangles[(t * 3) + i] = vertIndex;

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
                    var prevVertIndex = triangles[tri]; // MVert index
                    var loopIndex = splitTris[tri]; // MLoop index

                    // Same coordinates as the original vertex
                    vertices[newVertIndex] = vertices[prevVertIndex];

                    // TODO: If there were split normals, that'd be handled here.
                    normals[newVertIndex] = normals[prevVertIndex];

                    // Read UVs from loops again to handle any split UVs
                    for (int layer = 0; layer < loopUVs.Count; layer++)
                    {
                        var uv = loopUVs[layer][loopIndex].uv;

                        uvs[layer][newVertIndex] = new InteropVector2(uv);
                    }

                    // Same deal for vertex colors - copy from the loop
                    if (loopCols != null)
                    {
                        var col = loopCols[loopIndex];

                        // Convert to floating point for Unity
                        colors[newVertIndex] = new InteropColor(
                            col.r * colorScale,
                            col.g * colorScale,
                            col.b * colorScale,
                            col.a * colorScale
                        );
                    }

                    // And finally update the triangle to point to the new vertex
                    triangles[tri] = newVertIndex;
                    newVertIndex++;
                }
            }
        }
    }
}
