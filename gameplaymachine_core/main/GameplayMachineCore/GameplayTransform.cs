using System;

namespace GMCore
{
    [Serializable]
    public struct Float3 : IEquatable<Float3>
    {
        public float X;
        public float Y;
        public float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Float3 Zero => default;
        public static Float3 One => new Float3(1, 1, 1);
        public bool Equals(Float3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Float3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(Float3 left, Float3 right) => left.Equals(right);
        public static bool operator !=(Float3 left, Float3 right) => !left.Equals(right);
    }

    [Serializable]
    public struct Rotation : IEquatable<Rotation>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Rotation(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static Rotation Identity => new Rotation(0, 0, 0, 1);
        public bool Equals(Rotation other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public override bool Equals(object obj) => obj is Rotation other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public static bool operator ==(Rotation left, Rotation right) => left.Equals(right);
        public static bool operator !=(Rotation left, Rotation right) => !left.Equals(right);
    }

    [Serializable]
    public struct Position : IEquatable<Position>
    {
        public GameplaySpatialSpaceID Space;
        public float X;
        public float Y;
        public float Z;

        public Position(float x, float y, float z)
            : this(GameplaySpatialSpaceID.Default, x, y, z) { }

        public Position(GameplaySpatialSpaceID space, float x, float y, float z)
        {
            Space = space;
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(Position other) =>
            Space.Equals(other.Space) && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Position other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Space, X, Y, Z);
        public static bool operator ==(Position left, Position right) => left.Equals(right);
        public static bool operator !=(Position left, Position right) => !left.Equals(right);
    }

    [Serializable]
    public struct Transform : IEquatable<Transform>
    {
        public Position Position;
        public Rotation Rotation;
        public Float3 Scale;

        public Transform(Position position, Rotation rotation, Float3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public static Transform Identity => new Transform(default, Rotation.Identity, Float3.One);
        public bool Equals(Transform other) =>
            Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale);
        public override bool Equals(object obj) => obj is Transform other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);
        public static bool operator ==(Transform left, Transform right) => left.Equals(right);
        public static bool operator !=(Transform left, Transform right) => !left.Equals(right);
    }
}
