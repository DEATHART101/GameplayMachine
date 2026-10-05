using System;

namespace Test;

public class GameplayIntegerEncodingTests
{
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(-1, 1)]
    [TestCase(63, 1)]
    [TestCase(-64, 1)]
    [TestCase(64, 2)]
    [TestCase(-65, 2)]
    [TestCase(int.MaxValue, 5)]
    [TestCase(int.MinValue, 5)]
    public void VarInt32RoundTripsWithExpectedPayloadSize(int value, int expectedBytes)
    {
        byte[] bytes;
        using (var writer = new GameplaySaveWriter(Array.Empty<IGMSerializer>()))
        {
            writer.WriteVarInt32(value);
            bytes = writer.Finish();
        }

        Assert.That(bytes.Length, Is.EqualTo(expectedBytes + sizeof(uint)));
        using var reader = new GameplaySaveReader(bytes, Array.Empty<IGMSerializer>());
        Assert.That(reader.ReadVarInt32(), Is.EqualTo(value));
        reader.EnsureAt(reader.Length);
    }

    [TestCase(0L, 1)]
    [TestCase(63L, 1)]
    [TestCase(-64L, 1)]
    [TestCase(64L, 2)]
    [TestCase(long.MaxValue, 10)]
    [TestCase(long.MinValue, 10)]
    public void VarInt64RoundTripsWithExpectedPayloadSize(long value, int expectedBytes)
    {
        byte[] bytes;
        using (var writer = new GameplaySaveWriter(Array.Empty<IGMSerializer>()))
        {
            writer.WriteVarInt64(value);
            bytes = writer.Finish();
        }

        Assert.That(bytes.Length, Is.EqualTo(expectedBytes + sizeof(uint)));
        using var reader = new GameplaySaveReader(bytes, Array.Empty<IGMSerializer>());
        Assert.That(reader.ReadVarInt64(), Is.EqualTo(value));
        reader.EnsureAt(reader.Length);
    }

    [Test]
    public void NegativeObjectIDIsRejectedBeforeSerialization()
    {
        using var writer = new GameplaySaveWriter(Array.Empty<IGMSerializer>());
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            writer.WriteObjectID(new GObjectID { ID = -1 }));
        Assert.That(exception.Message, Does.Contain("Client-local GameplayObject ID"));
    }

    [Test]
    public void ResourceIDRoundTripsAsFixedSixteenByteSaveValue()
    {
        var expected = new ODResourceID(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        byte[] bytes;
        using (var writer = new GameplaySaveWriter(Array.Empty<IGMSerializer>()))
        {
            Assert.That(writer.Purpose, Is.EqualTo(GameplaySerializationPurpose.Save));
            writer.WriteResourceID(expected);
            bytes = writer.Finish();
        }

        Assert.That(bytes.Length, Is.EqualTo(16 + sizeof(uint)));
        Assert.That(bytes.Take(16), Is.EqualTo(new byte[]
        {
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
            0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
        }));
        using var reader = new GameplaySaveReader(bytes, Array.Empty<IGMSerializer>());
        Assert.That(reader.ReadResourceID(), Is.EqualTo(expected));
        reader.EnsureAt(reader.Length);
    }

    [Test]
    public void NetworkSerializationPurposeIsExposedToCustomCodecs()
    {
        using var writer = new GameplaySaveWriter(
            Array.Empty<IGMSerializer>(), GameplaySerializationPurpose.Network);
        Assert.That(writer.IsNetwork, Is.True);

        byte[] bytes = writer.Finish();
        using var reader = new GameplaySaveReader(
            bytes, Array.Empty<IGMSerializer>(), GameplaySerializationPurpose.Network);
        Assert.That(reader.IsNetwork, Is.True);
    }
}
