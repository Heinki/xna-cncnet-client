#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace DTAClient.Domain.Conquest
{
    /// <summary>
    /// Seeded, evenly scattered nodes joined by a spanning tree and short non-crossing routes.
    /// The stored network is authoritative for gameplay and presentation alike.
    /// </summary>
    public static class ConquestMapLayout
    {
        public static float Extent(ConquestState state) => state.Settings.MapSize * 220f;

        public static void Ensure(ConquestState state)
        {
            if (state.MapLayoutVersion != 0 || state.Territories.Count == 0)
                return;

            int count = state.Territories.Count;
            float extent = Extent(state);
            var random = new Random(state.Settings.Seed);
            var placed = new List<ConquestTerritory>();
            var last = state.Territories[count - 1];
            last.MapX = extent - 90;
            last.MapY = extent - 90;
            placed.Add(last);

            for (int i = 0; i < count - 1; i++)
            {
                var territory = state.Territories[i];
                float bestDistance = -1;
                for (int candidate = 0; candidate < 64; candidate++)
                {
                    float x = i == 0 ? 90 : 90 + (float)random.NextDouble() * (extent - 180);
                    float y = i == 0 ? 90 : 90 + (float)random.NextDouble() * (extent - 180);
                    if (i > 0)
                    {
                        if (candidate == 0)
                        {
                            x = extent / 2;
                            y = extent / 2;
                        }

                        float normalizedX = (x - extent / 2) / (extent * 0.43f);
                        float normalizedY = (y - extent / 2) / (extent * 0.43f);
                        if (normalizedX * normalizedX + normalizedY * normalizedY > 1)
                            continue;
                    }

                    float distance = placed.Min(p => DistanceSquared(x, y, p.MapX, p.MapY));
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        territory.MapX = x;
                        territory.MapY = y;
                    }
                }

                placed.Add(territory);
            }

            state.Connections.Clear();
            if (state.Started)
            {
                // Preserve every legal route and unresolved attack in saves from the grid prototype.
                foreach (var source in state.Territories)
                {
                    foreach (var target in state.Territories.Where(t => t.Id > source.Id &&
                        Math.Abs(t.X - source.X) + Math.Abs(t.Y - source.Y) == 1))
                    {
                        Connect(state, source.Id, target.Id);
                    }
                }
            }
            else
            {
                var connected = new HashSet<int> { 0 };
                while (connected.Count < count)
                {
                    float nearest = float.MaxValue;
                    int sourceId = 0;
                    int targetId = 0;
                    foreach (int source in connected)
                    {
                        foreach (var target in state.Territories.Where(t => !connected.Contains(t.Id)))
                        {
                            float distance = DistanceSquared(state.Territories[source], target);
                            if (distance < nearest)
                            {
                                nearest = distance;
                                sourceId = source;
                                targetId = target.Id;
                            }
                        }
                    }

                    Connect(state, sourceId, targetId);
                    connected.Add(targetId);
                }

                float maximumLength = extent / (float)Math.Sqrt(count) * 1.8f;
                foreach (var source in state.Territories)
                {
                    foreach (var target in state.Territories.Where(t => t.Id != source.Id)
                        .OrderBy(t => DistanceSquared(source, t)).Take(3))
                    {
                        if (DistanceSquared(source, target) > maximumLength * maximumLength ||
                            state.Connections.Any(e => e.Joins(source.Id, target.Id)) ||
                            state.Connections.Any(e => Crosses(state, source, target, e)))
                        {
                            continue;
                        }

                        Connect(state, source.Id, target.Id);
                    }
                }
            }

            state.MapLayoutVersion = 1;
        }

        private static void Connect(ConquestState state, int source, int target) => state.Connections.Add(
            new ConquestConnection { Source = Math.Min(source, target), Target = Math.Max(source, target) });

        private static float DistanceSquared(ConquestTerritory a, ConquestTerritory b) =>
            DistanceSquared(a.MapX, a.MapY, b.MapX, b.MapY);

        private static float DistanceSquared(float ax, float ay, float bx, float by) =>
            (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

        private static float Side(ConquestTerritory a, ConquestTerritory b, ConquestTerritory p) =>
            (b.MapX - a.MapX) * (p.MapY - a.MapY) - (b.MapY - a.MapY) * (p.MapX - a.MapX);

        private static bool Crosses(ConquestState state, ConquestTerritory a, ConquestTerritory b, ConquestConnection edge)
        {
            if (edge.Source == a.Id || edge.Target == a.Id || edge.Source == b.Id || edge.Target == b.Id)
                return false;

            var c = state.Territories[edge.Source];
            var d = state.Territories[edge.Target];

            return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
        }
    }
}
