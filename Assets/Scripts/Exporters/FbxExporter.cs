using LibMMD.Material;
using LibMMD.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using static LibMMD.Model.Morph;
using static LibMMD.Model.SkinningOperator;

/// <summary>
/// Binary FBX 7.4 writer for what the rest of the exporter produces: the format-neutral <see cref="RawMMDModel"/> the PMX
/// path builds, a <see cref="Skeleton"/> of rest matrices and the texture paths written beside the file. Unity and PMX are
/// left-handed, FBX right-handed with the global settings Blender writes, so x is mirrored, scaled by <see cref="UnitScale"/>
/// and triangle winding reversed.
/// </summary>
public static class FbxExporter
{
    public const string Extension = ".fbx";

    // the skeleton: what Write converts and writes

    /// <summary>The bone tree, described in Unity space and emitted in FBX space.</summary>
    public sealed class Skeleton
    {
        public sealed class Bone
        {
            public string Name;
            public int Parent = -1;

            /// <summary>Head position in Unity space, metres - what a refinement reasons about.</summary>
            public Vector3 Head = Vector3.zero;

            /// <summary>Direction the bone points in, Unity space - the PMX tail direction.</summary>
            public Vector3 Direction = Vector3.up;

            /// <summary>How long the bone is, Unity space: the distance its PMX tail sits at.</summary>
            public float Length;

            /// <summary>
            /// True for the stub bone at the tip of a childless bone: FBX has no bone length, and a reader derives one from
            /// the distance to children. See <see cref="Options.LeafBones"/>.
            /// </summary>
            public bool IsLeaf;

            /// <summary>Rest pose relative to the model root, already in FBX space (right-handed, cm).</summary>
            public Matrix4x4 Rest = Matrix4x4.identity;
        }

        public readonly List<Bone> Bones = new List<Bone>();

        public int IndexOf(string name)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i].Name == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// Builds the skeleton from the PMX bone list, which carries each head position and the direction its tail was derived to
        /// point in (see <c>ModelExporter.BoneTail</c>). A bone's Y axis is that direction and its Z comes from one reference for
        /// the whole rig ("down", or forward for a vertical bone), so roll is predictable and rotating a bone about X bends it the
        /// same way everywhere - the convention the add-on's eye controls use.
        /// </summary>
        public static Skeleton FromModel(RawMMDModel model, bool leafBones = true)
        {
            Skeleton skeleton = new Skeleton();
            LibMMD.Model.Bone[] bones = model.Bones ?? new LibMMD.Model.Bone[0];
            for (int i = 0; i < bones.Length; i++)
            {
                Bone bone = new Bone();
                bone.Name = bones[i].Name;
                bone.Parent = bones[i].ParentIndex >= 0 && bones[i].ParentIndex < bones.Length
                    ? bones[i].ParentIndex
                    : -1;
                bone.Head = bones[i].Position;
                bone.Direction = Direction(bones, i);
                bone.Length = TailLength(bones, i, bone.Direction);
                bone.Rest = RestMatrix(bone.Head, bone.Direction, Vector3.up);
                skeleton.Bones.Add(bone);
            }
            if (leafBones) skeleton.AddLeafBones();
            return skeleton;
        }

        /// <summary>
        /// Gives every chain end a child of its own so a reader that infers bone length from the distance to children has something
        /// to measure - the trick Blender's own FBX exporter uses (<c>add_leaf_bones</c>). Nothing is skinned to them, and they are
        /// counted before a reader that ignores them drops them, so lengths stay right.
        /// </summary>
        public void AddLeafBones()
        {
            int realCount = Bones.Count;
            for (int i = 0; i < realCount; i++)
            {
                bool hasChild = false;
                for (int j = 0; j < realCount && !hasChild; j++)
                {
                    hasChild = Bones[j].Parent == i;
                }
                if (hasChild || Bones[i].Length <= 1e-6f) continue;

                Bone leaf = new Bone();
                leaf.Name = UniqueName(Bones[i].Name + "_end");
                leaf.Parent = i;
                leaf.Head = Bones[i].Head + Bones[i].Direction * Bones[i].Length;
                leaf.Direction = Bones[i].Direction;
                leaf.IsLeaf = true;
                leaf.Rest = Bones[i].Rest *
                            Matrix4x4.Translate(new Vector3(0f, Bones[i].Length * UnitScale, 0f));
                Bones.Add(leaf);
            }
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            for (int suffix = 1; IndexOf(candidate) >= 0; suffix++)
            {
                candidate = name + "_" + suffix;
            }
            return candidate;
        }

        /// <summary>How far along the bone the PMX tail sits: a child's head, or the recorded offset.</summary>
        private static float TailLength(LibMMD.Model.Bone[] bones, int index, Vector3 direction)
        {
            LibMMD.Model.Bone bone = bones[index];
            LibMMD.Model.Bone.ChildBone tail = bone.ChildBoneVal;
            if (tail != null && tail.ChildUseId)
            {
                if (tail.Index >= 0 && tail.Index < bones.Length && tail.Index != index)
                {
                    return (bones[tail.Index].Position - bone.Position).magnitude;
                }
            }
            else if (tail != null && tail.Offset.sqrMagnitude > 1e-12f)
            {
                return tail.Offset.magnitude;
            }
            // No tail of its own: half of the segment leading into it, the same stub the PMX writer uses.
            for (int i = index; i >= 0 && i < bones.Length; i = bones[i].ParentIndex)
            {
                int parent = bones[i].ParentIndex;
                if (parent < 0 || parent >= bones.Length) break;
                Vector3 incoming = bones[i].Position - bones[parent].Position;
                if (incoming.sqrMagnitude > 1e-12f) return incoming.magnitude * 0.5f;
            }
            return 0f;
        }

        /// <summary>The direction a bone's tail points, as the PMX bone records it: a child, or an offset.</summary>
        public static Vector3 Direction(LibMMD.Model.Bone[] bones, int index)
        {
            LibMMD.Model.Bone bone = bones[index];
            LibMMD.Model.Bone.ChildBone tail = bone.ChildBoneVal;
            if (tail != null && tail.ChildUseId)
            {
                if (tail.Index >= 0 && tail.Index < bones.Length && tail.Index != index)
                {
                    Vector3 delta = bones[tail.Index].Position - bone.Position;
                    if (delta.sqrMagnitude > 1e-12f) return delta.normalized;
                }
            }
            else if (tail != null && tail.Offset.sqrMagnitude > 1e-12f)
            {
                return tail.Offset.normalized;
            }

            // No usable tail: carry on the way the chain arrives at the bone, as the PMX writer does.
            for (int i = index; i >= 0 && i < bones.Length; i = bones[i].ParentIndex)
            {
                int parent = bones[i].ParentIndex;
                if (parent < 0 || parent >= bones.Length) break;
                Vector3 incoming = bones[i].Position - bones[parent].Position;
                if (incoming.sqrMagnitude > 1e-12f) return incoming.normalized;
            }
            return Vector3.up;
        }

        /// <summary>
        /// A rest matrix from a head position, the direction the bone points in and the "up" the roll is taken from, all in Unity
        /// space. The basis is built in FBX space rather than composed in Unity and converted, because a mirror turns a proper
        /// rotation into an improper one, leaving Y or the roll sign flipped on some bones.
        /// </summary>
        public static Matrix4x4 RestMatrix(Vector3 head, Vector3 direction, Vector3 up)
        {
            Vector3 y = direction.sqrMagnitude > 1e-12f
                ? ToFbxDirection(direction).normalized
                : Vector3.up;
            Vector3 upDirection = up.sqrMagnitude > 1e-10f ? ToFbxDirection(up).normalized : Vector3.up;

            // a bone pointing straight along "up" cannot use it, so those fall back to forward for a stable roll
            Vector3 reference = Mathf.Abs(Vector3.Dot(y, upDirection)) <= 0.99f
                ? upDirection
                : ToFbxDirection(Vector3.forward);

            Vector3 x = Vector3.Cross(reference, y);
            if (x.sqrMagnitude < 1e-10f) x = Vector3.Cross(ToFbxDirection(Vector3.forward), y);
            x.Normalize();
            Vector3 z = Vector3.Cross(x, y);

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            matrix.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            matrix.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            Vector3 translation = ToFbxPosition(head);
            matrix.SetColumn(3, new Vector4(translation.x, translation.y, translation.z, 1f));
            return matrix;
        }
    }

    public sealed class Options
    {
        public string Creator = "UmaViewer";

        /// <summary>Folder the PNGs sit in, relative to the FBX file - the PMX export's "Texture2D".</summary>
        public string TextureFolder = "Texture2D";

        public bool WriteBindPose = true;

        /// <summary>
        /// Add a stub child at the tip of every chain end so a reader can measure the bone; without it a length exists only for a
        /// bone with children, and every finger tip, hair end and helper bone is drawn with a fallback.
        /// </summary>
        public bool LeafBones = true;

        /// <summary>
        /// Deflate the big arrays (zlib, the encoding every FBX writer uses): uncompressed they make a file several times
        /// larger, and a reader has to inflate them anyway.
        /// </summary>
        public bool CompressArrays = true;

        /// <summary>
        /// Carry the PNGs inside the file as well as referencing the folder next to it, so a reader that honours the embedded copy
        /// (Blender packs it into the .blend) shows the model textured even after the .fbx is moved away from its paths. About 2 MB.
        /// </summary>
        public bool EmbedTextures = true;
    }

    public static bool IsFbxPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    public static void Write(string path, RawMMDModel model, Skeleton skeleton, Options options = null)
    {
        Writer writer = new Writer(model, skeleton, options ?? new Options());
        writer.Write(path);
    }

    // space conversion (public so the export step and its tests can state the mapping once)

    /// <summary>
    /// Centimetres per model unit: the exporter hands over the viewer's world units, which are metres, and this file declares
    /// centimetres (unit scale 1.0 in the global settings), so the factor is 100 - 12.5x smaller than the same rig's .pmx
    /// numbers, which the PMX writer reaches by dividing by <c>WorldSizeAmplifier</c>, 0.08.
    /// </summary>
    public const float UnitScale = 100f;

    // x is the axis that mirrors: a z mirror differs by a half turn about up and lands the model back to front
    public static Vector3 ToFbxPosition(Vector3 p)
    {
        return new Vector3(-p.x, p.y, p.z) * UnitScale;
    }

    public static Vector3 ToFbxDirection(Vector3 d)
    {
        return new Vector3(-d.x, d.y, d.z);
    }

    public static Quaternion ToFbxRotation(Quaternion q)
    {
        // mirroring x conjugates a rotation (the axis is mirrored, the angle negated); the writer builds bone orientations in FBX space instead
        return new Quaternion(q.x, -q.y, -q.z, q.w);
    }

    // the FBX node tree

    private sealed class Node
    {
        public readonly string Name;
        public readonly List<object> Properties = new List<object>();
        public readonly List<Node> Children = new List<Node>();

        public Node(string name, params object[] properties)
        {
            Name = name;
            if (properties != null) Properties.AddRange(properties);
        }

        public Node Add(string name, params object[] properties)
        {
            Node child = new Node(name, properties);
            Children.Add(child);
            return child;
        }

        public Node Add(Node child)
        {
            Children.Add(child);
            return child;
        }

        public Node Properties70()
        {
            return Add("Properties70");
        }

        public Node Property(string name, string type, string label, string flags, params object[] values)
        {
            Node property = new Node("P", name, type, label, flags);
            if (values != null) property.Properties.AddRange(values);
            Children.Add(property);
            return this;
        }
    }

    /// <summary>A raw byte blob property ('R').</summary>
    private sealed class RawBytes
    {
        public readonly byte[] Data;

        public RawBytes(byte[] data)
        {
            Data = data;
        }
    }

    // one export

    private sealed class Writer
    {
        private const int Version = 7400;
        private const int BlockSentinelLength = 13;   // 12 bytes of offsets/counts plus the name length

        private static readonly byte[] HeaderMagic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");
        private static readonly byte[] FooterId =
        {
            0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e
        };
        private static readonly byte[] FooterMagic =
        {
            0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b
        };
        private static readonly byte[] FileId =
        {
            0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1
        };

        private readonly RawMMDModel model;
        private readonly Skeleton skeleton;
        private readonly Options options;

        private readonly Node objects = new Node("Objects");
        private readonly Node connections = new Node("Connections");
        private readonly Dictionary<string, int> typeCounts = new Dictionary<string, int>();

        private int[] polygonVertices = new int[0];   // one entry per polygon vertex, ~ removed
        private long nextId = 1000000;
        private string outputDirectory = "";

        internal Writer(RawMMDModel model, Skeleton skeleton, Options options)
        {
            this.model = model;
            this.skeleton = skeleton;
            this.options = options;
        }

        internal void Write(string path)
        {
            outputDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            long rootId = AddRoot();
            long[] boneIds = AddBones(rootId);
            long meshId = AddMesh(rootId);
            long geometryId = AddGeometry(meshId);
            AddMaterials(meshId);
            AddClusters(geometryId, boneIds);
            if (options.WriteBindPose) AddBindPose(boneIds);

            string creator = options.Creator + " " + Application.version;

            List<Node> roots = new List<Node>();
            roots.Add(BuildHeader(creator));
            roots.Add(new Node("FileId", new RawBytes(FileId)));
            roots.Add(new Node("CreationTime", "1970-01-01 10:00:00:000"));
            roots.Add(new Node("Creator", creator));
            roots.Add(BuildGlobalSettings());
            roots.Add(BuildDocuments());
            roots.Add(new Node("References"));
            roots.Add(BuildDefinitions());
            roots.Add(objects);
            roots.Add(connections);
            Node takes = new Node("Takes");
            takes.Add("Current", "");
            roots.Add(takes);

            using (BinaryWriter file = new BinaryWriter(File.Create(path)))
            {
                WriteFile(file, roots);
            }
        }

        private long NewId()
        {
            return nextId++;
        }

        private void Count(string type)
        {
            typeCounts.TryGetValue(type, out int count);
            typeCounts[type] = count + 1;
        }

        private void Connect(long child, long parent)
        {
            connections.Add("C", "OO", child, parent);
        }

        private void ConnectProperty(long child, long parent, string property)
        {
            connections.Add("C", "OP", child, parent, property);
        }

        private static string ObjectName(string name, string type)
        {
            return name + "\0\x01" + type;
        }

        private string ModelName()
        {
            return string.IsNullOrEmpty(model.NameEn) ? model.Name : model.NameEn;
        }

        // ---- skeleton ------------------------------------------------------------------------------

        private long AddRoot()
        {
            long id = NewId();
            string name = ModelName();

            long attributeId = NewId();
            Node attribute = objects.Add("NodeAttribute", attributeId, ObjectName(name, "NodeAttribute"), "Null");
            attribute.Add("TypeFlags", "Null");
            Count("NodeAttribute");

            Node node = objects.Add("Model", id, ObjectName(name, "Model"), "Null");
            node.Add("Version", 232);
            Node properties = node.Properties70();
            properties.Property("InheritType", "enum", "", "", 1);
            properties.Property("DefaultAttributeIndex", "int", "Integer", "", 0);
            properties.Property("Lcl Translation", "Lcl Translation", "", "A", 0.0, 0.0, 0.0);
            properties.Property("Lcl Rotation", "Lcl Rotation", "", "A", 0.0, 0.0, 0.0);
            properties.Property("Lcl Scaling", "Lcl Scaling", "", "A", 1.0, 1.0, 1.0);
            node.Add("Shading", true);
            node.Add("Culling", "CullingOff");
            Count("Model");

            Connect(id, 0L);           // 0 is the FBX scene root
            Connect(attributeId, id);
            return id;
        }

        private long[] AddBones(long rootId)
        {
            long[] ids = new long[skeleton.Bones.Count];
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                Skeleton.Bone bone = skeleton.Bones[i];
                long id = NewId();
                ids[i] = id;

                long attributeId = NewId();
                Node attribute = objects.Add("NodeAttribute", attributeId,
                    ObjectName(bone.Name, "NodeAttribute"), "LimbNode");
                attribute.Add("TypeFlags", "Skeleton");
                Count("NodeAttribute");

                Matrix4x4 parentRest = bone.Parent >= 0 ? skeleton.Bones[bone.Parent].Rest : Matrix4x4.identity;
                Matrix4x4 local = parentRest.inverse * bone.Rest;

                Node node = objects.Add("Model", id, ObjectName(bone.Name, "Model"), "LimbNode");
                node.Add("Version", 232);
                Node properties = node.Properties70();
                properties.Property("RotationActive", "bool", "", "", 1);
                properties.Property("InheritType", "enum", "", "", 1);
                properties.Property("ScalingMax", "Vector3D", "Vector", "", 0.0, 0.0, 0.0);
                properties.Property("DefaultAttributeIndex", "int", "Integer", "", 0);
                // stated XYZ explicitly: that is what a reader assumes by default, so the assumption is visible
                properties.Property("RotationOrder", "enum", "", "", 0);

                Vector3 translation = new Vector3(local.m03, local.m13, local.m23);
                Vector3 rotation = ToEulerXyz(local.rotation);
                Vector3 scale = local.lossyScale;
                properties.Property("Lcl Translation", "Lcl Translation", "", "A",
                    (double)translation.x, (double)translation.y, (double)translation.z);
                properties.Property("Lcl Rotation", "Lcl Rotation", "", "A",
                    (double)rotation.x, (double)rotation.y, (double)rotation.z);
                properties.Property("Lcl Scaling", "Lcl Scaling", "", "A",
                    (double)scale.x, (double)scale.y, (double)scale.z);
                node.Add("Shading", true);
                node.Add("Culling", "CullingOff");
                Count("Model");

                Connect(id, bone.Parent >= 0 ? ids[bone.Parent] : rootId);
                Connect(attributeId, id);
            }
            return ids;
        }

        private long AddMesh(long rootId)
        {
            string name = ModelName() + "_mesh";

            long attributeId = NewId();
            Node attribute = objects.Add("NodeAttribute", attributeId, ObjectName(name, "NodeAttribute"), "Mesh");
            attribute.Add("TypeFlags", "Mesh");
            Count("NodeAttribute");

            long id = NewId();
            Node node = objects.Add("Model", id, ObjectName(name, "Model"), "Mesh");
            node.Add("Version", 232);
            Node properties = node.Properties70();
            properties.Property("InheritType", "enum", "", "", 1);
            properties.Property("DefaultAttributeIndex", "int", "Integer", "", 0);
            properties.Property("Lcl Translation", "Lcl Translation", "", "A", 0.0, 0.0, 0.0);
            properties.Property("Lcl Rotation", "Lcl Rotation", "", "A", 0.0, 0.0, 0.0);
            properties.Property("Lcl Scaling", "Lcl Scaling", "", "A", 1.0, 1.0, 1.0);
            node.Add("Shading", true);
            node.Add("Culling", "CullingOff");
            Count("Model");

            // the mesh belongs under the root: a reader collects a skin's meshes by walking down from the armature node
            Connect(id, rootId);
            Connect(attributeId, id);
            return id;
        }

        private static Vector3 ToEulerXyz(Quaternion quaternion)
        {
            // Readers take Lcl Rotation as Euler('XYZ'), i.e. R = Rz * Ry * Rx.
            Matrix4x4 m = Matrix4x4.Rotate(quaternion.normalized);
            double x, y, z;
            double sy = -m.m20;
            if (Math.Abs(sy) < 0.999999)
            {
                y = Math.Asin(sy);
                x = Math.Atan2(m.m21, m.m22);
                z = Math.Atan2(m.m10, m.m00);
            }
            else
            {
                y = sy > 0 ? Math.PI / 2 : -Math.PI / 2;
                x = Math.Atan2(-m.m12, m.m11);
                z = 0.0;
            }
            const double toDegrees = 180.0 / Math.PI;
            return new Vector3((float)(x * toDegrees), (float)(y * toDegrees), (float)(z * toDegrees));
        }

        // ---- geometry ------------------------------------------------------------------------------

        private long AddGeometry(long meshId)
        {
            Vertex[] vertices = model.Vertices ?? new Vertex[0];
            int[] triangles = model.TriangleIndexes ?? new int[0];

            const int ExtraUvCount = 3;
            double[] positions = new double[vertices.Length * 3];
            double[] normals = new double[vertices.Length * 3];
            double[] uv = new double[vertices.Length * 2];
            double[][] extraUvs = new double[ExtraUvCount][];
            for (int layer = 0; layer < ExtraUvCount; layer++)
            {
                extraUvs[layer] = new double[vertices.Length * 4];
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vertex vertex = vertices[i];
                Vector3 position = ToFbxPosition(vertex.Coordinate);
                Vector3 normal = ToFbxDirection(vertex.Normal);
                positions[i * 3] = position.x;
                positions[i * 3 + 1] = position.y;
                positions[i * 3 + 2] = position.z;
                normals[i * 3] = normal.x;
                normals[i * 3 + 1] = normal.y;
                normals[i * 3 + 2] = normal.z;

                // pmx stores texture v flipped; FBX and Blender share the other convention, so the uv goes back
                uv[i * 2] = vertex.UvCoordinate.x;
                uv[i * 2 + 1] = 1f - vertex.UvCoordinate.y;

                Vector4[] extras = vertex.ExtraUvCoordinate;
                for (int layer = 0; layer < ExtraUvCount; layer++)
                {
                    Vector4 value = extras != null && layer < extras.Length ? extras[layer] : Vector4.zero;
                    extraUvs[layer][i * 4] = value.x;
                    extraUvs[layer][i * 4 + 1] = 1f - value.y;
                    extraUvs[layer][i * 4 + 2] = value.z;
                    // both halves are flipped, not just the first: mmd_tools reads UV1..UV3 from the xy half and
                    // _UV2/_UV3 from zw, flipping v in both, so flipping only xy misplaces _UV2's height and _UV3
                    extraUvs[layer][i * 4 + 3] = 1f - value.w;
                }
            }

            // the mirror flips handedness, so triangle winding is reversed with it
            int polygonCount = triangles.Length / 3;
            int[] polygonIndices = new int[polygonCount * 3];
            int[] polygonVertices = new int[polygonCount * 3];
            for (int i = 0; i < polygonCount; i++)
            {
                int a = triangles[i * 3];
                int b = triangles[i * 3 + 1];
                int c = triangles[i * 3 + 2];
                polygonVertices[i * 3] = a;
                polygonVertices[i * 3 + 1] = c;
                polygonVertices[i * 3 + 2] = b;
                polygonIndices[i * 3] = a;
                polygonIndices[i * 3 + 1] = c;
                polygonIndices[i * 3 + 2] = ~b;         // ~index marks the last vertex of a polygon
            }
            this.polygonVertices = polygonVertices;

            long id = NewId();
            Node geometry = objects.Add("Geometry", id, ObjectName(ModelName() + "_mesh", "Geometry"), "Mesh");
            geometry.Add("Properties70");
            geometry.Add("GeometryVersion", 124);
            geometry.Add("Vertices", positions);
            geometry.Add("PolygonVertexIndex", polygonIndices);

            Node normalLayer = geometry.Add("LayerElementNormal", 0);
            normalLayer.Add("Version", 101);
            normalLayer.Add("Name", "");
            // one normal per vertex: a reader then reads 13968 normals instead of one per polygon vertex
            normalLayer.Add("MappingInformationType", "ByVertice");
            normalLayer.Add("ReferenceInformationType", "Direct");
            normalLayer.Add("Normals", normals);

            // six UV layers, named the way mmd_tools lays pmx extra UVs out, so the channels the uma shader reads
            // (UV1, UV2, UV3, _UV2, _UV3) land where it expects: extra[0] -> UV1, extra[1] -> UV2 with its z in _UV2
            AddUvLayer(geometry, "UVMap", uv, 2, 0, 0);
            AddUvLayer(geometry, "UV1", extraUvs[0], 4, 0, 1);
            AddUvLayer(geometry, "UV2", extraUvs[1], 4, 0, 2);
            AddUvLayer(geometry, "UV3", extraUvs[2], 4, 0, 3);
            AddUvLayer(geometry, "_UV2", extraUvs[1], 4, 2, 4);
            AddUvLayer(geometry, "_UV3", extraUvs[2], 4, 2, 5);

            Node materialLayer = geometry.Add("LayerElementMaterial", 0);
            materialLayer.Add("Version", 101);
            materialLayer.Add("Name", "");
            materialLayer.Add("MappingInformationType", "ByPolygon");
            materialLayer.Add("ReferenceInformationType", "IndexToDirect");
            materialLayer.Add("Materials", MaterialPerPolygon(polygonCount));

            Node layerNode = geometry.Add("Layer", 0);
            layerNode.Add("Version", 100);
            layerNode.Add("LayerElement").Add("Type", "LayerElementNormal").Add("TypedIndex", 0);
            layerNode.Add("LayerElement").Add("Type", "LayerElementMaterial").Add("TypedIndex", 0);
            for (int i = 0; i <= 5; i++)
            {
                layerNode.Add("LayerElement").Add("Type", "LayerElementUV").Add("TypedIndex", i);
            }

            Count("Geometry");
            Connect(id, meshId);

            AddBlendShapes(id);
            return id;
        }

        /// <summary>
        /// One UV layer: one value per vertex and one index per polygon vertex choosing the corner's. The stride is
        /// <paramref name="components"/> and <paramref name="offset"/> picks the two components that become the layer.
        /// </summary>
        private void AddUvLayer(Node geometry, string name, double[] perVertex, int components, int offset,
            int typedIndex)
        {
            int vertexCount = perVertex.Length / components;
            double[] values = new double[vertexCount * 2];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                values[vertex * 2] = perVertex[vertex * components + offset];
                values[vertex * 2 + 1] = perVertex[vertex * components + offset + 1];
            }

            Node uvLayer = geometry.Add("LayerElementUV", typedIndex);
            uvLayer.Add("Version", 101);
            uvLayer.Add("Name", name);
            uvLayer.Add("MappingInformationType", "ByPolygonVertex");
            uvLayer.Add("ReferenceInformationType", "IndexToDirect");
            uvLayer.Add("UV", values);
            uvLayer.Add("UVIndex", polygonVertices);
        }

        /// <summary>Which material the polygon at this position uses, from the PMX part ranges.</summary>
        private int[] MaterialPerPolygon(int polygonCount)
        {
            int[] result = new int[polygonCount];
            Part[] parts = model.Parts ?? new Part[0];
            for (int part = 0; part < parts.Length; part++)
            {
                int first = parts[part].BaseShift / 3;
                int last = (parts[part].BaseShift + parts[part].TriangleIndexNum) / 3;
                for (int polygon = first; polygon < last && polygon < polygonCount; polygon++)
                {
                    result[polygon] = part;
                }
            }
            return result;
        }

        // ---- blend shapes --------------------------------------------------------------------------

        private void AddBlendShapes(long geometryId)
        {
            List<Morph> vertexMorphs = new List<Morph>();
            foreach (Morph morph in model.Morphs ?? new Morph[0])
            {
                if (morph.Type == MorphType.MorphTypeVertex && morph.MorphDatas != null && morph.MorphDatas.Length > 0)
                {
                    vertexMorphs.Add(morph);
                }
            }
            if (vertexMorphs.Count == 0) return;

            long blendShapeId = NewId();
            Node blendShape = objects.Add("Deformer", blendShapeId,
                ObjectName(ModelName() + "_mesh", "Deformer"), "BlendShape");
            blendShape.Add("Version", 100);
            Count("Deformer");
            Connect(blendShapeId, geometryId);

            foreach (Morph morph in vertexMorphs)
            {
                List<int> indices = new List<int>();
                List<Vector3> offsets = new List<Vector3>();
                foreach (MorphData data in morph.MorphDatas)
                {
                    if (!(data is VertexMorphData vertex)) continue;
                    if (vertex.Offset == Vector3.zero) continue;
                    indices.Add(vertex.VertexIndex);
                    offsets.Add(ToFbxPosition(vertex.Offset));
                }
                // a morph whose offsets are all zero still gets its channel: its name is part of the model, and dropping it would make the FBX's morph list differ from the pmx's

                double[] positions = new double[indices.Count * 3];
                for (int i = 0; i < offsets.Count; i++)
                {
                    positions[i * 3] = offsets[i].x;
                    positions[i * 3 + 1] = offsets[i].y;
                    positions[i * 3 + 2] = offsets[i].z;
                }

                long channelId = NewId();
                Node channel = objects.Add("Deformer", channelId,
                    ObjectName(morph.Name, "SubDeformer"), "BlendShapeChannel");
                channel.Add("Version", 100);
                channel.Add("DeformPercent", 0.0);
                channel.Add("FullWeights", new double[] { 100.0 });
                Count("Deformer");
                Connect(channelId, blendShapeId);

                long shapeId = NewId();
                Node shape = objects.Add("Geometry", shapeId, ObjectName(morph.Name, "Geometry"), "Shape");
                shape.Add("Version", 100);
                shape.Add("Indexes", indices.ToArray());
                shape.Add("Vertices", positions);
                // no Normals array: the exporter computes no morph normals and readers ignore them, while a zeroed array the length of Vertices costs a megabyte
                Count("Geometry");
                Connect(shapeId, channelId);
            }
        }

        // ---- materials and textures -----------------------------------------------------------------

        private void AddMaterials(long meshId)
        {
            Part[] parts = model.Parts ?? new Part[0];
            long[] textureIds = AddTextures();

            for (int i = 0; i < parts.Length; i++)
            {
                MMDMaterial material = parts[i].Material;
                long id = NewId();
                Node node = objects.Add("Material", id, ObjectName(material.Name, "Material"), "");
                node.Add("Version", 102);
                node.Add("ShadingModel", "Phong");
                node.Add("MultiLayer", 0);
                Node properties = node.Properties70();
                Color diffuse = material.DiffuseColor;
                Color specular = material.SpecularColor;
                Color ambient = material.AmbientColor;
                properties.Property("ShadingModel", "KString", "", "", "phong");
                properties.Property("EmissiveColor", "Color", "", "A", 0.0, 0.0, 0.0);
                properties.Property("AmbientColor", "Color", "", "A",
                    (double)ambient.r, (double)ambient.g, (double)ambient.b);
                properties.Property("DiffuseColor", "Color", "", "A",
                    (double)diffuse.r, (double)diffuse.g, (double)diffuse.b);
                properties.Property("DiffuseFactor", "Number", "", "A", 1.0);
                properties.Property("TransparentColor", "Color", "", "A", 0.0, 0.0, 0.0);
                properties.Property("TransparencyFactor", "Number", "", "A", (double)(1f - diffuse.a));
                properties.Property("SpecularColor", "Color", "", "A",
                    (double)specular.r, (double)specular.g, (double)specular.b);
                properties.Property("SpecularFactor", "Number", "", "A", 1.0);
                properties.Property("ShininessExponent", "Number", "", "A", (double)material.Shiness);
                properties.Property("ReflectionColor", "Color", "", "A", 0.0, 0.0, 0.0);
                properties.Property("ReflectionFactor", "Number", "", "A", 1.0);
                properties.Property("Opacity", "double", "Number", "", (double)diffuse.a);
                if (!string.IsNullOrEmpty(material.MetaInfo))
                {
                    // the uma shader settings have no FBX field, so they travel as a user property for a script to read
                    properties.Property("UmaMaterial", "KString", "", "", material.MetaInfo);
                }
                Count("Material");

                // Connection order is material slot order.
                Connect(id, meshId);
                long textureId = TextureId(textureIds, material.Texture);
                if (textureId != 0) ConnectProperty(textureId, id, "DiffuseColor");
            }
        }

        private long[] AddTextures()
        {
            long[] ids = new long[model.TextureList.Count];
            for (int i = 0; i < model.TextureList.Count; i++)
            {
                string relative = TextureRelativePath(model.TextureList[i]);
                if (string.IsNullOrEmpty(relative))
                {
                    ids[i] = 0;
                    continue;
                }
                string name = Path.GetFileNameWithoutExtension(relative);

                long videoId = NewId();
                Node video = objects.Add("Video", videoId, ObjectName(name, "Video"), "Clip");
                video.Add("Type", "Clip");
                video.Properties70().Property("Path", "KString", "XRefUrl", "", relative);
                video.Add("UseMipMap", 0);
                video.Add("Filename", relative);
                video.Add("RelativeFilename", relative);
                // the embedded copy goes on the Video node, where a reader looks for it: it is what lets the model survive being moved
                byte[] embedded = EmbeddedTexture(relative);
                if (embedded != null) video.Add("Content", new RawBytes(embedded));
                Count("Video");

                long textureId = NewId();
                Node texture = objects.Add("Texture", textureId, ObjectName(name, "Texture"), "");
                texture.Add("Type", "TextureVideoClip");
                texture.Add("Version", 202);
                texture.Add("TextureName", ObjectName(name, "Texture"));
                Node properties = texture.Properties70();
                properties.Property("UVSet", "KString", "", "", "UVMap");
                properties.Property("UseMaterial", "bool", "", "", 1);
                texture.Add("Media", ObjectName(name, "Video"));
                texture.Add("FileName", relative);
                texture.Add("RelativeFilename", relative);
                texture.Add("ModelUVTranslation", 0.0, 0.0);
                texture.Add("ModelUVScaling", 1.0, 1.0);
                texture.Add("Texture_Alpha_Source", "None");
                texture.Add("Cropping", 0, 0, 0, 0);
                Count("Texture");

                Connect(videoId, textureId);
                ids[i] = textureId;
            }
            return ids;
        }

        private long TextureId(long[] ids, MMDTexture texture)
        {
            if (texture == null) return 0;
            for (int i = 0; i < model.TextureList.Count && i < ids.Length; i++)
            {
                if (ReferenceEquals(model.TextureList[i], texture)) return ids[i];
            }
            string wanted = texture.TexturePath;
            for (int i = 0; i < model.TextureList.Count && i < ids.Length; i++)
            {
                if (model.TextureList[i] != null && model.TextureList[i].TexturePath == wanted) return ids[i];
            }
            return 0;
        }

        /// <summary>The texture list's own path, if it is one a reader can resolve next to the file.</summary>
        private string TextureRelativePath(MMDTexture texture)
        {
            if (texture == null || string.IsNullOrEmpty(texture.TexturePath) || texture.IsGlobalToon) return null;
            string path = texture.TexturePath.Replace('\\', '/');
            // the exporter writes "Texture2D/<name>.png" relative to the model, which is what FBX wants; anything else is left out rather than left unresolvable
            if (path.Contains(":/") || path.StartsWith("/")) return null;
            if (path.StartsWith(options.TextureFolder + "/")) return path;
            return options.TextureFolder + "/" + Path.GetFileName(path);
        }

        /// <summary>The PNG's bytes, read back from where the texture exporter just wrote them.</summary>
        private byte[] EmbeddedTexture(string relative)
        {
            if (!options.EmbedTextures || string.IsNullOrEmpty(outputDirectory)) return null;
            string absolute = Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                return File.Exists(absolute) ? File.ReadAllBytes(absolute) : null;
            }
            catch (IOException)
            {
                // a texture that cannot be read back is no reason to fail: the relative path is in the file, so a reader still finds it
                return null;
            }
        }

        // ---- skinning ------------------------------------------------------------------------------

        private void AddClusters(long geometryId, long[] boneIds)
        {
            long skinId = NewId();
            Node skin = objects.Add("Deformer", skinId, ObjectName(ModelName() + "_mesh", "Deformer"), "Skin");
            skin.Add("Version", 101);
            skin.Add("Link_DeformAcuracy", 50.0);
            Count("Deformer");
            // the skin hangs off the *geometry*, not the mesh's Model node: a reader looks for it by walking
            // bone -> cluster -> skin -> geometry and stops if the last step is not a geometry
            Connect(skinId, geometryId);

            Vertex[] vertices = model.Vertices ?? new Vertex[0];
            List<int>[] indexes = new List<int>[skeleton.Bones.Count];
            List<double>[] weights = new List<double>[skeleton.Bones.Count];
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = new List<int>();
                weights[i] = new List<double>();
            }

            for (int v = 0; v < vertices.Length; v++)
            {
                SkinningOperator skinning = vertices[v].SkinningOperator;
                if (skinning == null || skinning.Param == null) continue;
                switch (skinning.Type)
                {
                    case SkinningType.SkinningBdef1:
                        AddWeight(indexes, weights, ((Bdef1)skinning.Param).BoneId, 1f, v);
                        break;

                    case SkinningType.SkinningBdef2:
                        Bdef2 bdef2 = (Bdef2)skinning.Param;
                        AddWeight(indexes, weights, bdef2.BoneId[0], bdef2.BoneWeight, v);
                        AddWeight(indexes, weights, bdef2.BoneId[1], 1f - bdef2.BoneWeight, v);
                        break;

                    case SkinningType.SkinningBdef4:
                        Bdef4 bdef4 = (Bdef4)skinning.Param;
                        for (int j = 0; j < 4; j++)
                        {
                            AddWeight(indexes, weights, bdef4.BoneId[j], bdef4.BoneWeight[j], v);
                        }
                        break;

                    case SkinningType.SkinningSdef:
                        // SDEF's extra terms have no FBX equivalent; the two bones and their weight carry the deformation as closely as possible
                        Sdef sdef = (Sdef)skinning.Param;
                        AddWeight(indexes, weights, sdef.BoneId[0], sdef.BoneWeight, v);
                        AddWeight(indexes, weights, sdef.BoneId[1], 1f - sdef.BoneWeight, v);
                        break;
                }
            }

            for (int bone = 0; bone < skeleton.Bones.Count; bone++)
            {
                // a stub leaf bone needs no cluster (a reader falls back to the node's own transform), but every real
                // bone gets one even weightless: the cluster is where its rest pose is read from
                if (skeleton.Bones[bone].IsLeaf) continue;

                long clusterId = NewId();
                Node cluster = objects.Add("Deformer", clusterId,
                    ObjectName(skeleton.Bones[bone].Name, "SubDeformer"), "Cluster");
                cluster.Add("Version", 100);
                cluster.Add("UserData", "", "");
                cluster.Add("Indexes", indexes[bone].ToArray());
                cluster.Add("Weights", weights[bone].ToArray());
                Matrix4x4 rest = skeleton.Bones[bone].Rest;      // already in FBX space
                cluster.Add("Transform", MatrixToArray(rest.inverse));
                cluster.Add("TransformLink", MatrixToArray(rest));
                cluster.Add("TransformAssociateModel", MatrixToArray(Matrix4x4.identity));
                Count("Deformer");
                Connect(clusterId, skinId);
                Connect(boneIds[bone], clusterId);
            }
        }

        private static void AddWeight(List<int>[] indexes, List<double>[] weights, int bone, float weight, int vertex)
        {
            if (bone < 0 || bone >= indexes.Length || weight <= 0f) return;
            indexes[bone].Add(vertex);
            weights[bone].Add(weight);
        }

        // a bind pose in addition to the clusters, for the applications that look for one
        private void AddBindPose(long[] boneIds)
        {
            long poseId = NewId();
            Node pose = objects.Add("Pose", poseId, ObjectName("BindPose", "Pose"), "BindPose");
            pose.Add("Type", "BindPose");
            pose.Add("Version", 100);
            pose.Add("NbPoseNodes", skeleton.Bones.Count);
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                Node poseNode = pose.Add("PoseNode");
                poseNode.Add("Node", boneIds[i]);
                poseNode.Add("Matrix", MatrixToArray(skeleton.Bones[i].Rest));
            }
            Count("Pose");
            Connect(poseId, 0L);
        }

        /// <summary>FBX stores a matrix column by column.</summary>
        private static double[] MatrixToArray(Matrix4x4 m)
        {
            double[] values = new double[16];
            for (int column = 0; column < 4; column++)
            {
                for (int row = 0; row < 4; row++)
                {
                    values[column * 4 + row] = m[row, column];
                }
            }
            return values;
        }

        // ---- file scaffolding ----------------------------------------------------------------------

        private static Node BuildHeader(string creator)
        {
            Node header = new Node("FBXHeaderExtension");
            header.Add("FBXHeaderVersion", 1003);
            header.Add("FBXVersion", Version);
            header.Add("EncryptionType", 0);
            DateTime now = DateTime.Now;
            Node stamp = header.Add("CreationTimeStamp");
            stamp.Add("Version", 1000);
            stamp.Add("Year", now.Year);
            stamp.Add("Month", now.Month);
            stamp.Add("Day", now.Day);
            stamp.Add("Hour", now.Hour);
            stamp.Add("Minute", now.Minute);
            stamp.Add("Second", now.Second);
            stamp.Add("Millisecond", now.Millisecond);
            header.Add("Creator", creator);
            return header;
        }

        private static Node BuildGlobalSettings()
        {
            Node settings = new Node("GlobalSettings");
            settings.Add("Version", 1000);
            Node properties = settings.Properties70();
            properties.Property("UpAxis", "int", "Integer", "", 1);
            properties.Property("UpAxisSign", "int", "Integer", "", 1);
            properties.Property("FrontAxis", "int", "Integer", "", 2);
            properties.Property("FrontAxisSign", "int", "Integer", "", 1);
            properties.Property("CoordAxis", "int", "Integer", "", 0);
            properties.Property("CoordAxisSign", "int", "Integer", "", 1);
            properties.Property("OriginalUpAxis", "int", "Integer", "", -1);
            properties.Property("OriginalUpAxisSign", "int", "Integer", "", 1);
            properties.Property("UnitScaleFactor", "double", "Number", "", 1.0);
            properties.Property("OriginalUnitScaleFactor", "double", "Number", "", 1.0);
            properties.Property("AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0);
            properties.Property("DefaultCamera", "KString", "", "", "Producer Perspective");
            properties.Property("TimeMode", "enum", "", "", 11);
            properties.Property("TimeSpanStart", "KTime", "Time", "", 0L);
            properties.Property("TimeSpanStop", "KTime", "Time", "", 46186158000L);
            properties.Property("CustomFrameRate", "double", "Number", "", 24.0);
            return settings;
        }

        private static Node BuildDocuments()
        {
            Node documents = new Node("Documents");
            documents.Add("Count", 1);
            Node document = documents.Add("Document", 1000000001L, "Scene", "Scene");
            Node properties = document.Properties70();
            properties.Property("SourceObject", "object", "", "");
            properties.Property("ActiveAnimStackName", "KString", "", "", "");
            document.Add("RootNode", 0L);
            return documents;
        }

        private Node BuildDefinitions()
        {
            Node definitions = new Node("Definitions");
            definitions.Add("Version", 100);
            int total = 1;
            foreach (KeyValuePair<string, int> pair in typeCounts)
            {
                total += pair.Value;
            }
            definitions.Add("Count", total);
            definitions.Add("ObjectType", "GlobalSettings").Add("Count", 1);
            foreach (KeyValuePair<string, int> pair in typeCounts)
            {
                definitions.Add("ObjectType", pair.Key).Add("Count", pair.Value);
            }
            return definitions;
        }

        // ---- binary encoding -----------------------------------------------------------------------

        /// <summary>
        /// The file as the FBX binary format expects it: header, the top-level node list closed by a null record, then
        /// the footer. A node's offsets are patched once its children are written, the end offset being known by then.
        /// </summary>
        private void WriteFile(BinaryWriter writer, List<Node> roots)
        {
            writer.Write(HeaderMagic);
            writer.Write((uint)Version);
            WriteNodeList(writer, roots, allLast: false);

            writer.Write(FooterId);
            writer.Write(new byte[4]);
            long position = writer.BaseStream.Position;
            int padding = (int)(((position + 15) & ~15L) - position);
            if (padding == 0) padding = 16;
            writer.Write(new byte[padding]);
            writer.Write((uint)Version);
            writer.Write(new byte[120]);
            writer.Write(FooterMagic);
        }

        private void WriteNodeList(BinaryWriter writer, List<Node> nodes, bool allLast)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                WriteNode(writer, nodes[i], allLast || i == nodes.Count - 1);
            }
            WriteNullRecord(writer);
        }

        private static void WriteNullRecord(BinaryWriter writer)
        {
            writer.Write(new byte[BlockSentinelLength]);
        }

        private void WriteNode(BinaryWriter writer, Node node, bool isLast)
        {
            Stream stream = writer.BaseStream;
            long start = stream.Position;
            writer.Write(0u);                              // end offset, patched below
            writer.Write((uint)node.Properties.Count);
            writer.Write(0u);                              // property list length, patched below
            byte[] name = Encoding.ASCII.GetBytes(node.Name);
            writer.Write((byte)name.Length);
            writer.Write(name);

            long propertyStart = stream.Position;
            foreach (object property in node.Properties)
            {
                WriteProperty(writer, property);
            }
            long propertyEnd = stream.Position;

            if (node.Children.Count > 0)
            {
                WriteNodeList(writer, node.Children, allLast: false);
            }
            else if (node.Properties.Count == 0 && !isLast)
            {
                // a childless node with nothing in it still needs a list terminator unless it closes its parent, the rule readers rely on to find the end of a sibling list
                WriteNullRecord(writer);
            }

            long end = stream.Position;
            stream.Position = start;
            writer.Write((uint)end);
            writer.Write((uint)node.Properties.Count);
            writer.Write((uint)(propertyEnd - propertyStart));
            stream.Position = end;
        }

        private void WriteProperty(BinaryWriter writer, object property)
        {
            switch (property)
            {
                case bool value:
                    writer.Write((byte)'C');
                    writer.Write((byte)(value ? 1 : 0));
                    break;
                case short value:
                    writer.Write((byte)'Y');
                    writer.Write(value);
                    break;
                case int value:
                    writer.Write((byte)'I');
                    writer.Write(value);
                    break;
                case long value:
                    writer.Write((byte)'L');
                    writer.Write(value);
                    break;
                case float value:
                    writer.Write((byte)'F');
                    writer.Write(value);
                    break;
                case double value:
                    writer.Write((byte)'D');
                    writer.Write(value);
                    break;
                case string value:
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    writer.Write((byte)'S');
                    writer.Write((uint)bytes.Length);
                    writer.Write(bytes);
                    break;
                }
                case RawBytes value:
                    writer.Write((byte)'R');
                    writer.Write((uint)value.Data.Length);
                    writer.Write(value.Data);
                    break;
                case int[] values:
                    writer.Write((byte)'i');
                    WriteArray(writer, ToBytes(values), values.Length);
                    break;
                case long[] values:
                    writer.Write((byte)'l');
                    WriteArray(writer, ToBytes(values), values.Length);
                    break;
                case float[] values:
                    writer.Write((byte)'f');
                    WriteArray(writer, ToBytes(values), values.Length);
                    break;
                case double[] values:
                    writer.Write((byte)'d');
                    WriteArray(writer, ToBytes(values), values.Length);
                    break;
                default:
                    throw new ArgumentException("Unsupported FBX property type: " +
                                                (property == null ? "null" : property.GetType().ToString()));
            }
        }

        /// <summary>
        /// An array property: element count, encoding, byte length, then the bytes - count and length being the
        /// uncompressed ones, as the format requires. Arrays over 128 bytes are deflated, as every FBX writer does.
        /// </summary>
        private void WriteArray(BinaryWriter writer, byte[] bytes, int count)
        {
            if (!options.CompressArrays || bytes.Length <= 128)
            {
                writer.Write((uint)count);
                writer.Write(0u);
                writer.Write((uint)bytes.Length);
                writer.Write(bytes);
                return;
            }

            byte[] compressed = Zlib(bytes);
            writer.Write((uint)count);
            writer.Write(1u);
            writer.Write((uint)compressed.Length);
            writer.Write(compressed);
        }

        private static byte[] ToBytes(int[] values)
        {
            byte[] bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToBytes(long[] values)
        {
            byte[] bytes = new byte[values.Length * 8];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToBytes(float[] values)
        {
            byte[] bytes = new byte[values.Length * 4];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static byte[] ToBytes(double[] values)
        {
            byte[] bytes = new byte[values.Length * 8];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// zlib, which is what an FBX array's encoding 1 means: a raw deflate stream with a two byte zlib header in
        /// front and an Adler-32 of the uncompressed data behind. .NET's DeflateStream does the middle part only.
        /// </summary>
        private static byte[] Zlib(byte[] data)
        {
            using (MemoryStream output = new MemoryStream())
            {
                output.WriteByte(0x78);   // CM = deflate, CINFO = 32K window
                output.WriteByte(0x01);   // FLEVEL = fastest, no preset dictionary, header check bits valid
                using (DeflateStream deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                uint adler = Adler32(data);
                output.WriteByte((byte)(adler >> 24));
                output.WriteByte((byte)(adler >> 16));
                output.WriteByte((byte)(adler >> 8));
                output.WriteByte((byte)adler);
                return output.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            const uint modulus = 65521;
            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % modulus;
                b = (b + a) % modulus;
            }
            return (b << 16) | a;
        }
    }
}
