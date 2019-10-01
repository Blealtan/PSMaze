using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;

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
    public class AdjMazeCell
    {
        public int X { get; }
        public int Y { get; }
        public Direction Direction { get; }
        internal AdjMazeCell(int x, int y, Direction dir) { X = x; Y = y; Direction = dir; }

        internal PSObject ToPSObject()
        {
            var pso = new PSObject(this);
            pso.Members.Add(
                new PSMemberSet("PSStandardMembers", new[]
                { new PSPropertySet("DefaultDisplayPropertySet", new[] { "X", "Y", "Direction" }) }));
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
            _Maze = new Direction[128, 64];
            for (int y = 0; y < MazeCol; ++y)
                for (int x = 0; x < MazeRow; ++x)
                    GetMazeAt((x, y)) = Direction.None;

            Random random = new Random("token".GetHashCode()); // TODO: Change this to actual token

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
            return path.Split('\\').Select(sd => sd.ToLower() switch
            {
                var x when x == "up" || x == "down" || x == "left" || x == "right" => true,
                _ => false
            }).Aggregate((b1, b2) => b1 && b2);
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
                WriteItemObject(new AdjMazeCell(coord.x, coord.y - 1, Direction.Up).ToPSObject(), $"{path}\\Up", true);
            if (GetMazeAt(coord).HasFlag(Direction.Down))
                WriteItemObject(new AdjMazeCell(coord.x, coord.y + 1, Direction.Down).ToPSObject(), $"{path}\\Down", true);
            if (GetMazeAt(coord).HasFlag(Direction.Left))
                WriteItemObject(new AdjMazeCell(coord.x - 1, coord.y, Direction.Left).ToPSObject(), $"{path}\\Left", true);
            if (GetMazeAt(coord).HasFlag(Direction.Right))
                WriteItemObject(new AdjMazeCell(coord.x + 1, coord.y, Direction.Right).ToPSObject(), $"{path}\\Right", true);
        }

        protected override void GetItem(string path)
        {
            var coord = GetCoordinate(path) ?? throw new InvalidOperationException("Path not exist.");
            WriteItemObject(new AdjMazeCell(coord.x, coord.y, Direction.None).ToPSObject(), $"{path}", true);
        }
    }
}
