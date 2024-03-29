﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Security.Cryptography;

#nullable enable

namespace PSMaze
{
    [Flags]
    public enum Direction : byte
    {
        None = 0,
        Up = 1,
        Down = 2,
        Left = 4,
        Right = 8
    }

    [Serializable]
    public class MazeCell
    {
        public int X { get; }
        public int Y { get; }
        public string? Flag { get; }
        internal MazeCell(int x, int y, string? flag) { X = x; Y = y; Flag = flag; }

        internal PSObject ToPSObject()
        {
            var pso = new PSObject(this);
            pso.Members.Add(
                new PSMemberSet("PSStandardMembers", new[]
                { new PSPropertySet("DefaultDisplayPropertySet", new[] { "X", "Y", "Flag" }) }));
            return pso;
        }
        internal PSObject ToPSObjectWithDirection(Direction dir)
        {
            var pso = new PSObject(this);
            pso.Properties.Add(new PSNoteProperty("Direction", dir));
            pso.Members.Add(
                new PSMemberSet("PSStandardMembers", new[]
                { new PSPropertySet("DefaultDisplayPropertySet", new[] { "Direction", "X", "Y", "Flag" }) }));
            return pso;
        }
    }

    [CmdletProvider("Maze", ProviderCapabilities.None)]
    [OutputType(typeof(Direction), ProviderCmdlet = ProviderCmdlet.GetItem)]
    [OutputType(typeof(Direction), ProviderCmdlet = ProviderCmdlet.GetChildItem)]
    public class MazeProvider : NavigationCmdletProvider
    {
        private static readonly Direction[,] _Maze;
        private static ref Direction GetMazeAt((int x, int y) cell) => ref _Maze[cell.y, cell.x];
        private static int MazeRow => _Maze.GetLength(1);
        private static int MazeCol => _Maze.GetLength(0);
        static MazeProvider()
        {
            _Maze = new Direction[64, 64];
            for (int y = 0; y < MazeCol; ++y)
                for (int x = 0; x < MazeRow; ++x)
                    GetMazeAt((x, y)) = Direction.None;

            Random random = new Random(0x1551);

            // Construct a disjoint set
            var disj = new ((int x, int y) parent, int rank)[MazeCol, MazeRow];
            ref ((int x, int y) parent, int rank) disjGet((int x, int y) u) => ref disj[u.y, u.x];

            for (int y = 0; y < MazeCol; ++y)
                for (int x = 0; x < MazeRow; ++x)
                    disjGet((x, y)) = ((x, y), 0);

            (int x, int y) disjFind((int x, int y) u)
            {
                if (u == disjGet(u).parent)
                    return u;
                else
                    return disjGet(u).parent = disjFind(disjGet(u).parent);
            }
            void disjUnion((int x, int y) u_from, (int x, int y) v_from)
            {
                var u = disjFind(u_from);
                var v = disjFind(v_from);
                if (disjGet(u).rank > disjGet(v).rank)
                    disjGet(v).parent = u;
                else
                {
                    disjGet(u).parent = v;
                    if (disjGet(u).rank == disjGet(v).rank)
                        disjGet(v).rank++;
                }
            }

            // Construct edges ready to be removed
            var edges = new List<((int x, int y) src, Direction d)>();
            for (int y = 0; y < MazeCol; ++y)
                for (int x = 0; x < MazeRow; ++x)
                {
                    if (y > 0) edges.Add(((x, y), Direction.Up));
                    if (x > 0) edges.Add(((x, y), Direction.Left));
                }
            // Fisher-Yates shuffle
            for (int n = edges.Count - 1; n > 0; n--)
            {
                int k = random.Next(n + 1);
                var tmp = edges[k];
                edges[k] = edges[n];
                edges[n] = tmp;
            }

            // Use Kruskal's algorithm to generate a maze
            foreach (var (src, d) in edges)
            {
                (int x, int y) dst = d switch
                {
                    Direction.Up => (src.x, src.y - 1),
                    Direction.Left => (src.x - 1, src.y),
                    _ => throw new InvalidOperationException(),
                };
                if (disjFind(src) != disjFind(dst))
                {
                    disjUnion(src, dst);
                    _Maze[src.y, src.x] |= d;
                    _Maze[dst.y, dst.x] |= d switch
                    {
                        Direction.Up => Direction.Down,
                        Direction.Left => Direction.Right,
                        _ => throw new InvalidOperationException(),
                    };
                }
            }
        }

        protected override bool IsValidPath(string path)
        {
            return true;
        }

        protected override Collection<PSDriveInfo> InitializeDefaultDrives()
        {
            return new Collection<PSDriveInfo>() { new PSDriveInfo("Maze", ProviderInfo, "", "", null) };
        }

        private (int x, int y)? GetCoordinate(string path)
        {
            try
            {
                int x = 0, y = 0;
                foreach (var d in path.Split('\\').SelectMany(sd => sd.ToLower() switch
                {
                    "up" => Enumerable.Repeat(Direction.Up, 1),
                    "down" => Enumerable.Repeat(Direction.Down, 1),
                    "left" => Enumerable.Repeat(Direction.Left, 1),
                    "right" => Enumerable.Repeat(Direction.Right, 1),
                    "" => Enumerable.Empty<Direction>(),
                    _ => throw new InvalidOperationException()
                }))
                {
                    if ((GetMazeAt((x, y)) & d) == 0)
                        return null;
                    else
                        (x, y) = d switch
                        {
                            Direction.Up => (x, y - 1),
                            Direction.Down => (x, y + 1),
                            Direction.Left => (x - 1, y),
                            Direction.Right => (x + 1, y),
                            _ => throw new InvalidOperationException()
                        };
                }
                return (x, y);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private MazeCell? GetCellRepr(string path)
        {
            if (GetCoordinate(path) is (int x, int y))
            {
                int? flag = null;
                if ((x, y) == (MazeRow - 1, MazeCol - 1))
                {
                    using var sha256 = SHA256.Create();
                    var hash = sha256.ComputeHash(path.Split('\\').SelectMany(sd => sd.ToLower() switch
                    {
                        "up" => Enumerable.Repeat((byte)0, 1),
                        "down" => Enumerable.Repeat((byte)1, 1),
                        "left" => Enumerable.Repeat((byte)2, 1),
                        "right" => Enumerable.Repeat((byte)3, 1),
                        "" => Enumerable.Empty<byte>(),
                        _ => throw new InvalidOperationException()
                    }).Aggregate(new List<byte>(), (list, b) =>
                    {
                        if (list.Count > 0 && (list.Last() ^ b) == 1)
                            list.RemoveAt(list.Count - 1);
                        else
                            list.Add(b);
                        return list;
                    }).ToArray());
                    int nnflag = 0;
                    for (int i = 0; i < hash.Length; ++i)
                        nnflag ^= hash[i] << (8 * (i % 4));
                    flag = nnflag;
                }
                return new MazeCell(x, y, flag is int f ? $"flag{{D0_y0u_1ik3_PSC0r3_n0w_{f.ToString("X8")}}}" : null);
            }
            else
                return null;
        }

        protected override bool ItemExists(string path)
        {
            return GetCoordinate(path) != null;
        }

        protected override bool IsItemContainer(string path)
        {
            return GetCoordinate(path) != null;
        }

        protected override void GetChildItems(string path, bool recurse)
        {
            if (recurse)
                throw new InvalidOperationException("Recursive Get-ChildItems is not allowed in PSMaze. Do It Yourself :)");

            var coord = GetCoordinate(path) ?? throw new InvalidOperationException("Path not exist.");
            if (GetMazeAt(coord).HasFlag(Direction.Up))
            {
                var npath = $"{path}\\Up";
                WriteItemObject(GetCellRepr(npath)?.ToPSObjectWithDirection(Direction.Up), npath, true);
            }
            if (GetMazeAt(coord).HasFlag(Direction.Down))
            {
                var npath = $"{path}\\Down";
                WriteItemObject(GetCellRepr(npath)?.ToPSObjectWithDirection(Direction.Down), npath, true);
            }
            if (GetMazeAt(coord).HasFlag(Direction.Left))
            {
                var npath = $"{path}\\Left";
                WriteItemObject(GetCellRepr(npath)?.ToPSObjectWithDirection(Direction.Left), npath, true);
            }
            if (GetMazeAt(coord).HasFlag(Direction.Right))
            {
                var npath = $"{path}\\Right";
                WriteItemObject(GetCellRepr(npath)?.ToPSObjectWithDirection(Direction.Right), npath, true);
            }
        }

        protected override void GetItem(string path)
        {
            WriteItemObject((GetCellRepr(path) ?? throw new InvalidOperationException("Path not exist.")).ToPSObject(), "\\" + path, true);
        }
    }
}
