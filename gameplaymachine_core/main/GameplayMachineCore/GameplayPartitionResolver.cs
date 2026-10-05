using System;
using System.Collections.Generic;
using System.Linq;

namespace GMCore
{
    public readonly struct GameplayPartitionCoordinate : IEquatable<GameplayPartitionCoordinate>
    {
        public GameplaySpatialSpaceID Space { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public GameplayPartitionCoordinate(int x, int z)
            : this(GameplaySpatialSpaceID.Default, x, 0, z) { }

        public GameplayPartitionCoordinate(GameplaySpatialSpaceID space, int x, int y, int z)
        {
            Space = space;
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(GameplayPartitionCoordinate other) =>
            Space.Equals(other.Space) && X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is GameplayPartitionCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Space, X, Y, Z);
    }

    public interface IGameplayPartitionResolver
    {
        void Query(GameplayMachine machine, Position center, float range, ISet<PartitionID> results);
        bool TryResolve(GameplayMachine machine, Position position, out PartitionID partition);
    }

    public sealed class GameplayGridPartitionResolver : IGameplayPartitionResolver
    {
        public bool IncludeY { get; }
        public Float3 CellSize { get; }

        public GameplayGridPartitionResolver(float cellSize, bool includeY = false)
            : this(new Float3(cellSize, cellSize, cellSize), includeY) { }

        public GameplayGridPartitionResolver(Float3 cellSize, bool includeY = false)
        {
            if (!IsPositiveFinite(cellSize.X) || !IsPositiveFinite(cellSize.Z) ||
                includeY && !IsPositiveFinite(cellSize.Y))
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            CellSize = cellSize;
            IncludeY = includeY;
        }

        public void Query(GameplayMachine machine, Position center, float range, ISet<PartitionID> results)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (!IsPositiveFinite(range)) throw new ArgumentOutOfRangeException(nameof(range));

            float rangeSquared = range * range;
            int centerX = Cell(center.X, CellSize.X);
            int centerY = IncludeY ? Cell(center.Y, CellSize.Y) : 0;
            int centerZ = Cell(center.Z, CellSize.Z);
            int radiusX = checked((int)MathF.Ceiling(range / CellSize.X) + 1);
            int radiusY = IncludeY ? checked((int)MathF.Ceiling(range / CellSize.Y) + 1) : 0;
            int radiusZ = checked((int)MathF.Ceiling(range / CellSize.Z) + 1);
            for (long x = Math.Max(int.MinValue, (long)centerX - radiusX);
                 x <= Math.Min(int.MaxValue, (long)centerX + radiusX); x++)
            for (long y = Math.Max(int.MinValue, (long)centerY - radiusY);
                 y <= Math.Min(int.MaxValue, (long)centerY + radiusY); y++)
            for (long z = Math.Max(int.MinValue, (long)centerZ - radiusZ);
                 z <= Math.Min(int.MaxValue, (long)centerZ + radiusZ); z++)
            {
                var coordinate = new GameplayPartitionCoordinate(center.Space, (int)x, (int)y, (int)z);
                float minX = coordinate.X * CellSize.X;
                float minZ = coordinate.Z * CellSize.Z;
                float dx = DistanceToInterval(center.X, minX, minX + CellSize.X);
                float dz = DistanceToInterval(center.Z, minZ, minZ + CellSize.Z);
                float dy = 0;
                if (IncludeY)
                {
                    float minY = coordinate.Y * CellSize.Y;
                    dy = DistanceToInterval(center.Y, minY, minY + CellSize.Y);
                }
                if (dx * dx + dy * dy + dz * dz <= rangeSquared)
                {
                    if (IncludeY)
                    {
                        if (machine.TryGetPartitionAtCoordinate(coordinate, out PartitionID partition))
                            results.Add(partition);
                    }
                    else
                    {
                        foreach (PartitionID partition in machine.GetPartitionsAtColumn(
                                     coordinate.Space, coordinate.X, coordinate.Z))
                            results.Add(partition);
                    }
                }
            }
        }

        public bool TryResolve(GameplayMachine machine, Position position, out PartitionID partition)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            var coordinate = new GameplayPartitionCoordinate(position.Space,
                Cell(position.X, CellSize.X),
                IncludeY ? Cell(position.Y, CellSize.Y) : 0,
                Cell(position.Z, CellSize.Z));
            if (IncludeY)
                return machine.TryGetPartitionAtCoordinate(coordinate, out partition);
            foreach (PartitionID candidate in machine.GetPartitionsAtColumn(
                         coordinate.Space, coordinate.X, coordinate.Z).OrderBy(id => id.Value))
            {
                partition = candidate;
                return true;
            }
            partition = default;
            return false;
        }

        private static float DistanceToInterval(float value, float minimum, float maximum) =>
            value < minimum ? minimum - value : value > maximum ? value - maximum : 0;

        private static bool IsPositiveFinite(float value) =>
            value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        private static int Cell(float value, float size) => checked((int)MathF.Floor(value / size));
    }
}
