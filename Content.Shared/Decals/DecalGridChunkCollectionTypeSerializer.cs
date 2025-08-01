using System.Globalization;
using System.Linq;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;
using static Content.Shared.Decals.DecalGridComponent;

namespace Content.Shared.Decals
{
    [TypeSerializer]
    public sealed partial class DecalGridChunkCollectionTypeSerializer : ITypeSerializer<DecalGridChunkCollection, MappingDataNode>
    {
        public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            return serializationManager.ValidateNode<Dictionary<Vector2i, Dictionary<uint, Decal>>>(node, context);
        }

        public DecalGridChunkCollection Read(ISerializationManager serializationManager,
            MappingDataNode node,
            IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
            ISerializationManager.InstantiationDelegate<DecalGridChunkCollection>? _ = default)
        {
            node.TryGetValue("version", out var versionNode);
            var version = ((ValueDataNode?) versionNode)?.AsInt() ?? 1;
            Dictionary<Vector2i, DecalChunk> dictionary;
            uint nextIndex = 0;
            var ids = new HashSet<uint>();

            // TODO: Dump this when we don't need support anymore.
            if (version > 1)
            {
                var nodes = (SequenceDataNode) node["nodes"];
                dictionary = new Dictionary<Vector2i, DecalChunk>();

                foreach (var dNode in nodes)
                {
                    var aNode = (MappingDataNode) dNode;
                    var data = serializationManager.Read<DecalData>(aNode["node"], hookCtx, context);
                    var deckNodes = (MappingDataNode) aNode["decals"];

                    foreach (var (decalUidNode, decalData) in deckNodes)
                    {
                        var dUid = uint.Parse(decalUidNode, CultureInfo.InvariantCulture);
                        var coords = serializationManager.Read<Vector2>(decalData, hookCtx, context);

                        var chunkOrigin = SharedMapSystem.GetChunkIndices(coords, SharedDecalSystem.ChunkSize);
                        var chunk = dictionary.GetOrNew(chunkOrigin);
                        var decal = new Decal(coords, data.Id, data.Color, data.Angle, data.ZIndex, data.Cleanable, data.ShaderID);

                        nextIndex = Math.Max(nextIndex, dUid);

                        // Re-used ID somehow
                        // This will bump all IDs by up to 1 but will ensure the map is still readable.
                        if (!ids.Add(dUid))
                        {
                            dUid = nextIndex++;
                            ids.Add(dUid);
                        }

                        chunk.Decals[dUid] = decal;
                    }
                }
            }
            else
            {
                dictionary = serializationManager.Read<Dictionary<Vector2i, DecalChunk>>(node, hookCtx, context, notNullableOverride: true);

                foreach (var decals in dictionary.Values)
                {
                    foreach (var uid in decals.Decals.Keys)
                    {
                        nextIndex = Math.Max(uid, nextIndex);
                    }
                }
            }

            nextIndex++;
            return new DecalGridChunkCollection(dictionary) { NextDecalId = nextIndex };
        }

        public DataNode Write(ISerializationManager serializationManager,
            DecalGridChunkCollection value, IDependencyCollection dependencies,
            bool alwaysWrite = false,
            ISerializationContext? context = null)
        {
            var lookup = new Dictionary<DecalData, List<uint>>();
            var decalLookup = new Dictionary<uint, Decal>();

            var allData = new MappingDataNode();
            // Want consistent chunk + decal ordering so diffs aren't mangled
            var nodes = new SequenceDataNode();

            // Assuming decal indices stay consistent:
            // We'll write decals by
            // - decaldata
            // - decal uid
            // - additional decal data

            // Build all of the decal lookups first.
            foreach (var chunk in value.ChunkCollection.Values)
            {
                foreach (var (uid, decal) in chunk.Decals)
                {
                    var data = new DecalData(decal);
                    var existing = lookup.GetOrNew(data);
                    existing.Add(uid);
                    decalLookup[uid] = decal;
                }
            }

            var lookupNodes = lookup.Keys.ToList();
            lookupNodes.Sort();

            foreach (var data in lookupNodes)
            {
                var uids = lookup[data];
                var lookupNode = new MappingDataNode { { "node", serializationManager.WriteValue(data, alwaysWrite, context) } };
                var decks = new MappingDataNode();

                uids.Sort();

                foreach (var uid in uids)
                {
                    var decal = decalLookup[uid];
                    // Inline coordinates
                    decks.Add(uid.ToString(), serializationManager.WriteValue(decal.Coordinates, alwaysWrite, context));
                }

                lookupNode.Add("decals", decks);
                nodes.Add(lookupNode);
            }

            allData.Add("version", 2.ToString(CultureInfo.InvariantCulture));
            allData.Add("nodes", nodes);

            return allData;
        }

        [DataDefinition]
        private readonly partial struct DecalData : IEquatable<DecalData>, IComparable<DecalData>
        {
            [DataField("id")]
            public string Id { get; init; } = string.Empty;

            [DataField("color")]
            public Color? Color { get; init; }

            [DataField("angle")]
            public Angle Angle { get; init; } = Angle.Zero;

            [DataField("zIndex")]
            public int ZIndex { get; init; }

            [DataField("cleanable")]
            public bool Cleanable { get; init; }

            [DataField]
            public string ShaderID { get; init; } = string.Empty; //imp edit - for shaders

            public DecalData(string id, Color? color, Angle angle, int zIndex, bool cleanable, string shaderId) //imp edit - added decal shaders
            {
                Id = id;
                Color = color;
                Angle = angle;
                ZIndex = zIndex;
                Cleanable = cleanable;
                ShaderID = shaderId; //imp edit - for shaders
            }

            public DecalData(Decal decal)
            {
                Id = decal.Id;
                Color = decal.Color;
                Angle = decal.Angle;
                ZIndex = decal.ZIndex;
                Cleanable = decal.Cleanable;
                ShaderID = decal.ShaderID; //imp edit - for decal shaders
            }

            public bool Equals(DecalData other)
            {
                return Id == other.Id &&
                       Nullable.Equals(Color, other.Color) &&
                       Angle.Equals(other.Angle) &&
                       ZIndex == other.ZIndex &&
                       Cleanable == other.Cleanable &&
                       ShaderID == other.ShaderID; //imp edit - for shaders;
            }

            public override bool Equals(object? obj)
            {
                return obj is DecalData other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Id, Color, Angle, ZIndex, Cleanable, ShaderID); //imp edit - for shaders
            }

            public int CompareTo(DecalData other)
            {
                var idComparison = string.Compare(Id, other.Id, StringComparison.Ordinal);
                if (idComparison != 0)
                    return idComparison;

                var colorComparison = string.Compare(Color?.ToHex(), other.Color?.ToHex(), StringComparison.Ordinal);

                if (colorComparison != 0)
                    return colorComparison;

                var angleComparison = Angle.Theta.CompareTo(other.Angle.Theta);

                if (angleComparison != 0)
                    return angleComparison;

                var zIndexComparison = ZIndex.CompareTo(other.ZIndex);
                if (zIndexComparison != 0)
                    return zIndexComparison;

                return Cleanable.CompareTo(other.Cleanable);
            }
        }
    }
}
