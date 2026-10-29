#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

using lzo.net;

using Rampastring.Tools;

namespace DTAClient.Domain.Conquest
{
    public enum ConquestObjectKind
    {
        Infantry, Vehicle, Building
    }

    public sealed class ConquestStartingObject
    {
        public int Side { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Upgrade { get; set; } = string.Empty;
        public ConquestObjectKind Kind { get; set; }
        public int Count { get; set; } = 1;
        public int Footprint { get; set; } = 6;
    }

    /// <summary>Builds grants from the installed game's roster and places them on clear, level map cells.</summary>
    public static class ConquestStartingForces
    {
        public static List<ConquestPerkDefinition> Catalog(IniFile rules, int playerSide, int aiSide)
        {
            var countries = (rules.GetSection("Countries") ?? rules.GetSection("Houses"))?.Keys.Select(k => k.Value).ToList();
            var result = new List<ConquestPerkDefinition>();
            if (countries == null || playerSide < 0 || aiSide < 0 || playerSide >= countries.Count || aiSide >= countries.Count)
                return result;

            bool contains(string csv, string country) => csv.Split(',').Any(s => string.Equals(s.Trim(), country, StringComparison.OrdinalIgnoreCase));
            string[] roster(string list) => rules.GetSection(list)?.Keys.Select(k => k.Value).ToArray() ?? Array.Empty<string>();
            bool eligible(string id, int side) => rules.SectionExists(id) && rules.GetIntValue(id, "TechLevel", -1) >= 0 &&
                rules.GetIntValue(id, "Cost", 0) > 0 && contains(rules.GetStringValue(id, "Owner", string.Empty), countries[side]) &&
                !contains(rules.GetStringValue(id, "ForbiddenHouses", string.Empty), countries[side]) &&
                (string.IsNullOrEmpty(rules.GetStringValue(id, "RequiredHouses", string.Empty)) ||
                string.Equals(rules.GetStringValue(id, "RequiredHouses", string.Empty), "none", StringComparison.OrdinalIgnoreCase) ||
                contains(rules.GetStringValue(id, "RequiredHouses", string.Empty), countries[side]));

            void add(string id, string[] candidates, ConquestObjectKind kind, int count, int price, bool local = false)
            {
                var grants = new List<ConquestStartingObject>();
                foreach (int side in new[] { playerSide, aiSide }.Distinct())
                {
                    string? type = candidates.Where(t => eligible(t, side))
                        .OrderBy(t => rules.GetIntValue(t, "Cost", int.MaxValue)).ThenBy(t => t, StringComparer.Ordinal).FirstOrDefault();
                    if (type == null)
                        return;

                    string upgrade = string.Empty;
                    if (kind == ConquestObjectKind.Building)
                    {
                        string foundation = rules.GetStringValue(type, "PowersUpBuilding", string.Empty);
                        if (foundation.Length > 0)
                        {
                            string? parent = roster("BuildingTypes").FirstOrDefault(t => string.Equals(t, foundation, StringComparison.OrdinalIgnoreCase));
                            if (parent == null || !eligible(parent, side))
                                return;
                            upgrade = type;
                            type = parent;
                        }
                    }
                    grants.Add(new ConquestStartingObject { Side = side, Type = type, Upgrade = upgrade, Kind = kind, Count = count, Footprint = StockFootprint(type) });
                }
                result.Add(new ConquestPerkDefinition
                {
                    Id = id,
                    Kind = local ? ConquestPerkKind.StartingDefenses : kind == ConquestObjectKind.Building ?
                        ConquestPerkKind.StartingStructures : ConquestPerkKind.StartingTroops,
                    MaximumLevel = kind == ConquestObjectKind.Building ? 1 : 3,
                    PurchaseCost = price,
                    AutoCombatStrength = kind == ConquestObjectKind.Vehicle ? 2 : 1,
                    TerritoryOnly = local,
                    StartingObjects = grants
                });
            }

            bool armed(string id) => !string.IsNullOrEmpty(rules.GetStringValue(id, "Primary", string.Empty)) &&
                !string.Equals(rules.GetStringValue(id, "Primary", string.Empty), "none", StringComparison.OrdinalIgnoreCase) &&
                !rules.GetBooleanValue(id, "Harvester", false) && !rules.GetBooleanValue(id, "Organic", false) &&
                !rules.GetBooleanValue(id, "ConsideredAircraft", false) &&
                !string.Equals(rules.GetStringValue(id, "MovementZone", string.Empty), "Water", StringComparison.OrdinalIgnoreCase);
            add("starting-infantry", roster("InfantryTypes").Where(armed).ToArray(), ConquestObjectKind.Infantry, 3, 500);
            add("starting-vehicles", roster("VehicleTypes").Where(armed).ToArray(), ConquestObjectKind.Vehicle, 1, 750);
            foreach (var (id, key, price) in new[]
            {
                ("starting-power", "BuildPower", 500), ("starting-refinery", "BuildRefinery", 800),
                ("starting-barracks", "BuildBarracks", 500)
            })
            {
                string[] candidates = rules.GetStringValue("AI", key, string.Empty).Split(',').Select(t => t.Trim())
                    .Where(t => roster("BuildingTypes").Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();
                add(id, candidates, ConquestObjectKind.Building, 1, price);
            }
            add("node-defenses", roster("BuildingTypes").Where(t => rules.GetBooleanValue(t, "IsBaseDefense", false) &&
                !string.IsNullOrEmpty(rules.GetStringValue(t, "Primary", string.Empty))).ToArray(), ConquestObjectKind.Building, 1, 450, true);

            if (result.Any(p => p.Id == "starting-infantry" || p.Id == "starting-vehicles"))
                result.Add(new ConquestPerkDefinition
                {
                    Id = "veteran-escorts",
                    Kind = ConquestPerkKind.VeteranProduction,
                    MaximumLevel = 1,
                    PurchaseCost = 700,
                    AutoCombatStrength = 1
                });

            return result;
        }

        public static void Apply(IniFile map, ConquestSettings settings, ConquestBattle battle,
            int playerStart, int aiStart, int playerSide, int aiSide, Func<string, string>? playerType = null, IniFile? terrain = null, Func<string, bool>? canBuildOn = null)
        {
            var requests = new List<(ConquestStartingObject Object, bool Player, int Start)>();
            foreach (var perk in settings.Perks)
            {
                foreach (bool player in new[] { true, false })
                {
                    int level = (player ? battle.PlayerPerks : battle.AIPerks).GetValueOrDefault(perk.Id);
                    var grant = perk.StartingObjects.FirstOrDefault(g => g.Side == (player ? playerSide : aiSide));
                    if (grant == null || level == 0)
                        continue;
                    for (int i = 0; i < grant.Count * level; i++)
                        requests.Add((grant, player, player ? playerStart : aiStart));
                }
            }
            if (requests.Count == 0)
                return;

            var centers = requests.Select(r => r.Start).Distinct().Select(index =>
            {
                int packed = map.GetIntValue("Waypoints", index.ToString(CultureInfo.InvariantCulture), -1);
                if (index < 0 || packed < 0)
                    throw new InvalidDataException("Starting forces require an existing start waypoint on this map.");

                return (X: packed % 1000, Y: packed / 1000);
            }).ToArray();
            var groundTiles = GroundTiles(terrain, canBuildOn);
            var cells = ReadCells(map, centers, groundTiles, out var elevations);
            ExcludeOverlays(map, cells);
            var occupied = new HashSet<(int X, int Y)>();
            var starts = new List<(int X, int Y)>();
            foreach (var entry in map.GetSection("Waypoints")?.Keys ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (int.TryParse(entry.Key, out int index) && index >= 0 && index < 8 && int.TryParse(entry.Value, out int packed))
                {
                    var start = (packed % 1000, packed / 1000);
                    starts.Add(start);
                    Reserve(occupied, start.Item1 - 4, start.Item2 - 4, 9);
                }
            }
            foreach (string section in new[] { "Infantry", "Units", "Structures", "Aircraft" })
            {
                var entries = map.GetSection(section)?.Keys;
                if (entries == null)
                    continue;
                foreach (var entry in entries)
                {
                    string[] fields = entry.Value.Split(',');
                    if (fields.Length > 4 && int.TryParse(fields[3], out int x) && int.TryParse(fields[4], out int y))
                        Reserve(occupied, x - 1, y - 1, section == "Structures" ? 8 : 3);
                }
            }
            foreach (var entry in map.GetSection("Terrain")?.Keys ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (int.TryParse(entry.Key, out int packed))
                    Reserve(occupied, packed % 1000 - 1, packed / 1000 - 1, 3);
            }

            // Terrain order is identical for every grant at a base. Sort once, then
            // check live reservations while assigning each object.
            var candidatesByStart = new Dictionary<int, List<(int X, int Y)>>();
            foreach (int index in requests.Select(r => r.Start).Distinct())
            {
                int packed = map.GetIntValue("Waypoints", index.ToString(CultureInfo.InvariantCulture), -1);
                var start = (X: packed % 1000, Y: packed / 1000);
                if (!elevations.TryGetValue(start, out int height))
                    throw new InvalidDataException("Starting forces require an existing start waypoint on this map.");

                candidatesByStart[index] = cells.Where(c => c.Value == height && Distance(c.Key, start) <= 32 &&
                    starts.All(s => Distance(c.Key, s) >= 6)).Select(c => c.Key)
                    .OrderBy(c => Distance(c, start)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
            }

            // Work on a placement plan first: a failed map never receives a partial grant.
            var placements = new List<(string Section, string Value)>();
            foreach (var request in requests.OrderByDescending(r => r.Object.Kind == ConquestObjectKind.Building))
            {
                int packed = map.GetIntValue("Waypoints", request.Start.ToString(CultureInfo.InvariantCulture), -1);
                var start = (X: packed % 1000, Y: packed / 1000);
                int height = elevations[start];
                int footprint = request.Object.Kind == ConquestObjectKind.Building ? request.Object.Footprint : 1;
                (int X, int Y)? position = null;
                foreach (var candidate in candidatesByStart[request.Start])
                {
                    bool free = true;
                    for (int dx = 0; dx < footprint && free; dx++)
                    {
                        for (int dy = 0; dy < footprint; dy++)
                        {
                            var cell = (candidate.X + dx, candidate.Y + dy);
                            if (!cells.TryGetValue(cell, out int level) || level != height || occupied.Contains(cell))
                            {
                                free = false;
                                break;
                            }
                        }
                    }
                    if (free)
                    {
                        position = candidate;
                        break;
                    }
                }
                if (!position.HasValue)
                    throw new InvalidDataException("This map has no safe clear ground near the base for the purchased starting forces. Choose Auto Combat.");

                var point = position.Value;
                Reserve(occupied, point.X - 1, point.Y - 1, footprint + 2);
                string type = request.Player ? playerType?.Invoke(request.Object.Type) ?? request.Object.Type : request.Object.Type;
                string prefix = string.Join(",", "Spawn" + (request.Start + 1).ToString(CultureInfo.InvariantCulture), type, "256",
                    point.X.ToString(CultureInfo.InvariantCulture), point.Y.ToString(CultureInfo.InvariantCulture));
                string section = request.Object.Kind == ConquestObjectKind.Building ? "Structures" :
                    request.Object.Kind == ConquestObjectKind.Infantry ? "Infantry" : "Units";
                int rank = (request.Player ? battle.PlayerPerks : battle.AIPerks).GetValueOrDefault("veteran-escorts") > 0 ? 1 : 0;
                string upgrade = request.Object.Upgrade.Length > 0 ? request.Object.Upgrade : "None";
                string suffix = request.Object.Kind == ConquestObjectKind.Building ? ",64,None,1,0,1," + (request.Object.Upgrade.Length > 0 ? "1" : "0") + ",0," + upgrade + ",None,None,0,0" :
                    request.Object.Kind == ConquestObjectKind.Infantry ? ",1,Guard,64,None," + rank + ",-1,0,1,0" : ",64,Guard,None," + rank + ",-1,0,-1,1,0";
                placements.Add((section, prefix + suffix));
            }
            foreach (var placement in placements)
            {
                int index = 0;
                while (map.GetStringValue(placement.Section, "CQ" + index.ToString(CultureInfo.InvariantCulture), string.Empty).Length > 0)
                    index++;
                map.SetStringValue(placement.Section, "CQ" + index.ToString(CultureInfo.InvariantCulture), placement.Value);
            }
        }

        private static int StockFootprint(string type) => type.ToUpperInvariant() switch
        {
            // Conservative bounds for native TS/YR structures; custom art keeps the larger default.
            "GAPOWR" or "NAPOWR" or "NAAPWR" or "YAPOWR" or "PROC" or "GAREFN" or "NAREFN" or "YAREFN" or
            "GAPILE" or "NAHAND" or "YABRCK" => 4,
            "GACTWR" or "NALASR" or "GAPILL" or "YAGGUN" => 2,
            _ => 6
        };

        private static int Distance((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private static void Reserve(HashSet<(int X, int Y)> occupied, int x, int y, int size)
        {
            for (int dx = 0; dx < size; dx++)
            {
                for (int dy = 0; dy < size; dy++)
                    occupied.Add((x + dx, y + dy));
            }
        }

        private static void ExcludeOverlays(IniFile map, Dictionary<(int X, int Y), int> cells)
        {
            var section = map.GetSection("OverlayPack");
            if (section == null)
                return;

            byte[] encoded = ReadPack(section);
            var overlays = new byte[512 * 512];
            overlays.AsSpan().Fill((byte)255);
            int input = 0;
            int output = 0;
            while (input + 4 <= encoded.Length && output < overlays.Length)
            {
                int length = BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(input));
                int count = BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(input + 2));
                input += 4;
                if (length == 0 && count == 0)
                    break;
                if (length == 0 || count == 0 || length > encoded.Length - input || count > overlays.Length - output)
                    throw new InvalidDataException("Invalid Conquest map overlay block.");

                DecodeOverlayBlock(encoded.AsSpan(input, length), overlays.AsSpan(output, count));
                input += length;
                output += count;
            }
            foreach (var point in cells.Keys.ToArray())
            {
                if (point.X >= 512 || point.Y >= 512 || overlays[point.Y * 512 + point.X] != 255)
                    cells.Remove(point);
            }
        }

        private static void DecodeOverlayBlock(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            int input = 0;
            int output = 0;
            while (input < source.Length && output < destination.Length)
            {
                int command = source[input++];
                if (command == 128)
                    break;
                if (command >= 128 && command < 192)
                {
                    int count = command & 63;
                    if (count > source.Length - input || count > destination.Length - output)
                        throw new InvalidDataException("Truncated overlay literals.");
                    source.Slice(input, count).CopyTo(destination.Slice(output, count));
                    input += count;
                    output += count;
                    continue;
                }

                int length;
                int position;
                if (command < 128)
                {
                    if (input >= source.Length)
                        throw new InvalidDataException("Truncated overlay reference.");
                    length = (command >> 4) + 3;
                    position = output - (((command & 15) << 8) | source[input++]);
                }
                else
                {
                    length = (command & 63) + 3;
                    if (command >= 254)
                    {
                        if (input + 2 > source.Length)
                            throw new InvalidDataException("Truncated overlay run.");
                        length = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(input));
                        input += 2;
                    }
                    if (command == 254)
                    {
                        if (input >= source.Length || length > destination.Length - output)
                            throw new InvalidDataException("Invalid overlay fill.");
                        destination.Slice(output, length).Fill(source[input++]);
                        output += length;
                        continue;
                    }
                    if (input + 2 > source.Length)
                        throw new InvalidDataException("Truncated overlay reference.");
                    position = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(input));
                    input += 2;
                }
                if (position < 0 || position >= output || length > destination.Length - output)
                    throw new InvalidDataException("Invalid overlay reference.");
                for (int i = 0; i < length; i++)
                    destination[output++] = destination[position++];
            }
            if (output != destination.Length)
                throw new InvalidDataException("Incomplete overlay block.");
        }

        private static byte[] ReadPack(IniSection section)
        {
            if (section.Keys.Count > 250000 || section.Keys.Any(k => !int.TryParse(k.Key, out _) || k.Value.Length > 16000000))
                throw new InvalidDataException("Invalid Conquest map pack entries.");
            long length = section.Keys.Sum(k => (long)k.Value.Length);
            if (length > 16000000)
                throw new InvalidDataException("Conquest map pack exceeds the supported size.");

            string encoded = string.Concat(section.Keys.OrderBy(k => int.Parse(k.Key, CultureInfo.InvariantCulture)).Select(k => k.Value));
            try
            {
                return Convert.FromBase64String(encoded);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("Invalid Conquest map pack encoding.", ex);
            }
        }

        private static HashSet<uint> GroundTiles(IniFile? terrain, Func<string, bool>? canBuildOn)
        {
            bool allowed(string ground) => canBuildOn?.Invoke(ground) ?? true;
            var result = new HashSet<uint>();
            if (allowed("Clear"))
                result.UnionWith(new uint[] { 0, 65535, uint.MaxValue });
            if (terrain == null)
                return result;

            // Flat ground groups come from this theater, not fixed tile numbers.
            // Slopes, cliffs, water, bridges and railway sets remain excluded.
            var groups = new[]
            {
                ("Clear", new[] { "ClearTile", "SandTile", "GreenTile", "PaveTile", "MiscPaveTile",
                    "ClearToSandLat", "ClearToGreenLat", "ClearToPaveLat" }),
                ("Rough", new[] { "RoughTile", "ClearToRoughLat", "RoughGround" }),
                ("Road", new[] { "PavedRoads", "PavedRoadEnds", "Medians", "DirtRoadJunction", "DirtRoadCurve", "DirtRoadStraight" })
            };
            var sets = new HashSet<int>();
            foreach (var (ground, keys) in groups)
            {
                if (allowed(ground))
                    sets.UnionWith(keys.Select(k => terrain.GetIntValue("General", k, -1)).Where(i => i >= 0));
            }
            uint firstTile = 0;
            for (int index = 0; index < 4096; index++)
            {
                string section = "TileSet" + index.ToString("D4", CultureInfo.InvariantCulture);
                if (!terrain.SectionExists(section))
                    break;
                int count = terrain.GetIntValue(section, "TilesInSet", 0);
                if (count < 0 || count > 4096 || firstTile + count > 65535)
                    throw new InvalidDataException("Invalid Conquest theater tile definitions.");

                if (sets.Contains(index))
                {
                    for (uint tile = firstTile; tile < firstTile + count; tile++)
                        result.Add(tile);
                }
                firstTile += (uint)count;
            }

            return result;
        }

        private static Dictionary<(int X, int Y), int> ReadCells(IniFile map, (int X, int Y)[] centers, HashSet<uint> groundTiles,
            out Dictionary<(int X, int Y), int> elevations)
        {
            var section = map.GetSection("IsoMapPack5") ?? throw new InvalidDataException("Starting forces require an isometric map tile pack.");
            byte[] compressed = ReadPack(section);
            var result = new Dictionary<(int X, int Y), int>();
            elevations = new Dictionary<(int X, int Y), int>();
            // A tile is 11 bytes and may straddle compression blocks. Keep one block
            // and its partial record, never a second expanded copy of the entire map.
            var block = new byte[ushort.MaxValue + 11];
            int pending = 0;
            int decodedLength = 0;
            int offset = 0;
            while (offset + 4 <= compressed.Length)
            {
                int inputLength = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(offset));
                int outputLength = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(offset + 2));
                offset += 4;
                if (inputLength == 0 && outputLength == 0)
                    break;
                if (inputLength == 0 || outputLength == 0 || inputLength > compressed.Length - offset || decodedLength + outputLength > 16000000)
                    throw new InvalidDataException("Invalid Conquest map tile block.");

                using var input = new MemoryStream(compressed, offset, inputLength);
                using var stream = new LzoStream(input, CompressionMode.Decompress);
                int count = 0;
                while (count < outputLength)
                {
                    int read = stream.Read(block, pending + count, outputLength - count);
                    if (read == 0)
                        throw new InvalidDataException("Truncated Conquest map tile block.");
                    count += read;
                }
                int available = pending + outputLength;
                for (int i = 0; i + 11 <= available; i += 11)
                {
                    int x = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(i));
                    int y = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(i + 2));
                    if (x >= 1000 || y >= 1000)
                        continue;

                    bool nearby = false;
                    foreach (var center in centers)
                    {
                        if (center.X == x && center.Y == y)
                            elevations[(x, y)] = block[i + 9];
                        if (Distance((x, y), center) <= 44)
                            nearby = true;
                    }
                    if (!nearby)
                        continue;

                    uint tile = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(i + 4));
                    if (groundTiles.Contains(tile))
                        result[(x, y)] = block[i + 9];
                }
                pending = available % 11;
                block.AsSpan(available - pending, pending).CopyTo(block);
                decodedLength += outputLength;
                offset += inputLength;
            }

            return result;
        }
    }
}