#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ClassicUO.Assets;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game;

internal static class BoatRoutePlanner
{
    private static readonly Point[] Steps = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    internal static List<Point>? Find(MapLoader maps, TileDataLoader tiles, int facet, Point start, Point goal,
        Point[] footprint, CancellationToken cancellation)
    {
        var blocks = new Dictionary<Point, ulong>();
        var open = new PriorityQueue<(Point Tile, int Cost), int>();
        var routes = new Dictionary<Point, (Point Previous, int Cost)> { [start] = (start, 0) };
        if (!CanSail(goal))
            return null;

        open.Enqueue((start, 0), Distance(start));
        int expanded = 0;
        long deadline = Environment.TickCount64 + 5000;
        while (open.TryDequeue(out var node, out _) && ++expanded <= 250_000
            && Environment.TickCount64 < deadline && !cancellation.IsCancellationRequested)
        {
            if (node.Cost != routes[node.Tile].Cost)
                continue;
            if (node.Tile == goal)
            {
                var path = new List<Point>();
                for (Point tile = goal; tile != start; tile = routes[tile].Previous)
                    path.Add(tile);
                path.Reverse();
                return path;
            }

            int cost = node.Cost + 1;
            foreach (Point step in Steps)
            {
                Point next = node.Tile + step;
                if ((routes.TryGetValue(next, out var existing) && existing.Cost <= cost) || !CanSail(next))
                    continue;
                routes[next] = (node.Tile, cost);
                open.Enqueue((next, cost), cost + Distance(next));
            }
        }
        return null;

        int Distance(Point point) => Math.Abs(goal.X - point.X) + Math.Abs(goal.Y - point.Y);

        bool CanSail(Point anchor)
        {
            foreach (Point offset in footprint)
            {
                Point tile = anchor + offset;
                if (tile.X < 0 || tile.Y < 0 || tile.X >= maps.MapsDefaultSize[facet, 0]
                    || tile.Y >= maps.MapsDefaultSize[facet, 1])
                    return false;

                var block = new Point(tile.X >> 3, tile.Y >> 3);
                if (!blocks.TryGetValue(block, out ulong water))
                    blocks[block] = water = ReadWaterBlock(maps, tiles, facet, block);
                if ((water & (1UL << ((tile.Y & 7) * 8 + (tile.X & 7)))) == 0)
                    return false;
            }
            return true;
        }
    }

    private static ulong ReadWaterBlock(MapLoader maps, TileDataLoader tiles, int facet, Point block)
    {
        ref IndexMap index = ref maps.GetIndex(facet, block.X, block.Y);
        if (!index.IsValid())
            return 0;

        var cells = index.MapFile.ReadAt<MapBlock>((long)index.MapAddress).Cells;
        ulong water = 0;
        for (int i = 0; i < 64; i++)
        {
            int id = cells[i].TileID & 0x3FFF;
            if (id < tiles.LandData.Length && tiles.LandData[id].IsWet)
                water |= 1UL << i;
        }

        if (index.StaticCount == 0)
            return water;

        var buffer = ArrayPool<StaticsBlock>.Shared.Rent((int)index.StaticCount);
        try
        {
            var statics = buffer.AsSpan(0, (int)index.StaticCount);
            index.StaticFile.ReadAt((long)index.StaticAddress, MemoryMarshal.AsBytes(statics));
            foreach (var tile in statics)
            {
                if (tile.X >= 8 || tile.Y >= 8 || tile.Color >= tiles.StaticData.Length)
                    continue;
                var data = tiles.StaticData[tile.Color];
                int local = tile.Y * 8 + tile.X;
                if (!data.IsWet && (data.IsImpassable || data.IsSurface) && tile.Z >= cells[local].Z - 2
                    && tile.Z <= cells[local].Z + 20)
                    water &= ~(1UL << local);
            }
            return water;
        }
        finally
        {
            ArrayPool<StaticsBlock>.Shared.Return(buffer);
        }
    }
}
