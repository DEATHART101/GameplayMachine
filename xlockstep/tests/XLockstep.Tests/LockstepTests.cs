using Xunit;

namespace XLockstep.Tests
{
    public sealed class LockstepTests
    {
        [Fact]
        public void LateInputIsRetargetedToNextOpenTick()
        {
            var coordinator = new LockstepFrameCoordinator();
            coordinator.SealNextFrame();
            LockstepInputAcceptance accepted = coordinator.Accept(7, Command(1, 1, 1));
            Assert.Equal(2, accepted.AcceptedTick.Value);
            Assert.True(accepted.Retargeted);
            Assert.Single(coordinator.SealNextFrame().Commands);
        }

        [Fact]
        public void PersistentlyLatePlayerStillGetsEveryInputIntoAFrame()
        {
            var coordinator = new LockstepFrameCoordinator();
            for (int sequence = 1; sequence <= 4; sequence++)
            {
                coordinator.SealNextFrame();
                LockstepInputAcceptance accepted = coordinator.Accept(9, Command(sequence, sequence, 1));
                Assert.Equal(coordinator.NextOpenTick.Value, accepted.AcceptedTick.Value);
                Assert.Single(coordinator.SealNextFrame().Commands);
            }
        }

        [Fact]
        public void FrameCommandsHaveCanonicalPlayerAndSequenceOrder()
        {
            var coordinator = new LockstepFrameCoordinator();
            coordinator.Accept(2, Command(2, 2, 1));
            coordinator.Accept(1, Command(1, 1, 1));
            coordinator.Accept(1, Command(3, 2, 1));
            LockstepFrame frame = coordinator.SealNextFrame();
            Assert.Equal(new long[] { 1, 2, 2 }, System.Array.ConvertAll(frame.Commands, item => item.Sequence));
            Assert.Equal(new int[] { 1, 1, 2 }, System.Array.ConvertAll(frame.Commands, item => item.PlayerId));
        }

        [Fact]
        public void CoordinatorAcceptsZeroBasedPlayerSlot()
        {
            var coordinator = new LockstepFrameCoordinator();
            LockstepInputAcceptance accepted = coordinator.Accept(0, Command(1, 1, 1));

            Assert.Equal(1, accepted.AcceptedTick.Value);
            Assert.Equal(0, Assert.Single(coordinator.SealNextFrame().Commands).PlayerId);
        }

        [Fact]
        public void CoordinatorRejectsNegativePlayerSlot()
        {
            var coordinator = new LockstepFrameCoordinator();

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                coordinator.Accept(-1, Command(1, 1, 1)));
        }

        [Fact]
        public void ProtocolRoundTripsFrame()
        {
            var frame = new LockstepFrame
            {
                Tick = new LockstepTick(9),
                Commands = new[] { Command(4, 3, 9).CloneForPlayer(2, new LockstepTick(9)) },
            };
            byte[] bytes = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Frame,
                Frame = frame,
            });
            LockstepMessage decoded = LockstepProtocol.Read(bytes);
            Assert.Equal(9, decoded.Frame!.Tick.Value);
            Assert.Equal(2, decoded.Frame.Commands[0].PlayerId);
            Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Frame.Commands[0].Payload);
        }

        [Fact]
        public void ProtocolRoundTripsRetargetedAcceptance()
        {
            byte[] bytes = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.InputAccepted,
                CommandId = 12,
                RequestedTick = new LockstepTick(2),
                Tick = new LockstepTick(5),
            });

            LockstepMessage decoded = LockstepProtocol.Read(bytes);
            Assert.Equal(2, decoded.RequestedTick.Value);
            Assert.Equal(5, decoded.Tick.Value);
        }

        [Fact]
        public void ProtocolRoundTripsConfirmedEmptyTickRange()
        {
            byte[] bytes = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.AdvanceTicks,
                FromTick = new LockstepTick(12),
                Tick = new LockstepTick(19),
            });

            LockstepMessage decoded = LockstepProtocol.Read(bytes);
            Assert.Equal(12, decoded.FromTick.Value);
            Assert.Equal(19, decoded.Tick.Value);
        }

        [Fact]
        public void ProtocolRoundTripsFullStateHashAudit()
        {
            byte[] bytes = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.StateHash,
                PlayerId = 2,
                Tick = new LockstepTick(300),
                StateHash = 0x123456789ABCDEF0UL,
                StateHashKind = LockstepStateHashKind.FullAudit,
            });

            LockstepMessage decoded = LockstepProtocol.Read(bytes);
            Assert.Equal(2, decoded.PlayerId);
            Assert.Equal(300, decoded.Tick.Value);
            Assert.Equal(0x123456789ABCDEF0UL, decoded.StateHash);
            Assert.Equal(LockstepStateHashKind.FullAudit, decoded.StateHashKind);
        }

        [Fact]
        public void ProtocolRoundTripsRelayHandshakeWithoutGameState()
        {
            LockstepMessage hello = LockstepProtocol.Read(LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Hello,
                SchemaId = 11,
                ResourceCatalogId = 22,
                InterfaceTypeIds = new ulong[] { 3, 7, 19 },
                Data = new byte[] { 4, 5 },
            }));
            Assert.Equal(new ulong[] { 3, 7, 19 }, hello.InterfaceTypeIds);

            LockstepMessage welcome = LockstepProtocol.Read(LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Welcome,
                PlayerId = 2,
                PlayerCount = 4,
                Tick = LockstepTick.Zero,
                InputDelay = 3,
                MaximumEmptyTicks = 128,
                StateHashIntervalTicks = 30,
                FullStateHashIntervalTicks = 300,
            }));
            Assert.Equal(2, welcome.PlayerId);
            Assert.Equal(4, welcome.PlayerCount);
            Assert.Equal(30, welcome.StateHashIntervalTicks);
            Assert.Equal(300, welcome.FullStateHashIntervalTicks);
            Assert.Empty(welcome.Data);

            Assert.Equal(LockstepMessageKind.Ready,
                LockstepProtocol.Read(LockstepProtocol.Write(new LockstepMessage
                {
                    Kind = LockstepMessageKind.Ready,
                })).Kind);
        }

        [Fact]
        public void ConfirmedRangeIsSmallerThanMultipleEmptyFrames()
        {
            int separateBytes = 0;
            for (int tick = 1; tick <= 10; tick++)
            {
                separateBytes += LockstepProtocol.Write(new LockstepMessage
                {
                    Kind = LockstepMessageKind.Frame,
                    Frame = new LockstepFrame { Tick = new LockstepTick(tick) },
                }).Length;
            }
            int rangeBytes = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.AdvanceTicks,
                FromTick = LockstepTick.Zero,
                Tick = new LockstepTick(10),
            }).Length;

            Assert.True(rangeBytes * 5 < separateBytes,
                $"Expected material compression, range={rangeBytes}, frames={separateBytes}");
        }

        [Fact]
        public void CoordinatorReportsWhetherNextInputDrivenFrameCanBeSealed()
        {
            var coordinator = new LockstepFrameCoordinator();
            Assert.False(coordinator.HasCommandsForNextFrame);
            coordinator.Accept(1, Command(1, 1, 1));
            Assert.True(coordinator.HasCommandsForNextFrame);
            coordinator.SealNextFrame();
            Assert.False(coordinator.HasCommandsForNextFrame);
        }

        [Fact]
        public void InputDrivenAcceptanceForcesFutureRequestOntoNextOpenTick()
        {
            var coordinator = new LockstepFrameCoordinator();
            LockstepInputAcceptance accepted = coordinator.Accept(1, Command(1, 1, 100),
                forceNextOpenTick: true);

            Assert.Equal(100, accepted.RequestedTick.Value);
            Assert.Equal(1, accepted.AcceptedTick.Value);
            Assert.True(coordinator.HasCommandsForNextFrame);
        }

        [Fact]
        public void Fixed64ArithmeticIsDeterministic()
        {
            Fixed64 left = Fixed64.Parse("1.25");
            Fixed64 right = Fixed64.Parse("2.5");
            Assert.Equal("3.125", (left * right).ToString());
            Assert.Equal("0.5", (left / right).ToString());
        }

        [Fact]
        public void DeterministicCollectionsEnumerateCanonically()
        {
            var set = new DeterministicSet<string> { "z", "a", "m" };
            var map = new DeterministicMap<int, string> { [7] = "g", [1] = "a", [3] = "c" };

            Assert.Equal(new[] { "a", "m", "z" }, set);
            Assert.Equal(new[] { 1, 3, 7 }, System.Linq.Enumerable.Select(map, pair => pair.Key));
        }

        [Fact]
        public void DeterministicCollectionsDoNotSortAgainWhileEnumerating()
        {
            var set = new DeterministicSet<CountingKey>
            {
                new CountingKey(7),
                new CountingKey(1),
                new CountingKey(3),
            };
            var map = new DeterministicMap<CountingKey, string>
            {
                [new CountingKey(7)] = "g",
                [new CountingKey(1)] = "a",
                [new CountingKey(3)] = "c",
            };

            CountingKey.ComparisonCount = 0;
            Assert.Equal(new[] { 1, 3, 7 }, System.Linq.Enumerable.Select(set, value => value.Value));
            Assert.Equal(new[] { 1, 3, 7 }, System.Linq.Enumerable.Select(map, pair => pair.Key.Value));
            Assert.Equal(0, CountingKey.ComparisonCount);
        }

        [Fact]
        public void DeterministicCollectionsKeepOrderWhenMutatedThroughInterfaces()
        {
            System.Collections.Generic.ISet<int> set = new DeterministicSet<int>();
            System.Collections.Generic.IDictionary<int, string> map = new DeterministicMap<int, string>();

            set.Add(5);
            set.UnionWith(new[] { 3, 7, 1 });
            set.Remove(3);
            map.Add(5, "e");
            map[1] = "a";
            map[3] = "c";
            map.Remove(5);

            Assert.Equal(new[] { 1, 5, 7 }, set);
            Assert.Equal(new[] { 1, 3 }, map.Keys);
            Assert.Equal(new[] { 1, 3 }, System.Linq.Enumerable.Select(map, pair => pair.Key));
        }

        private readonly struct CountingKey : System.IComparable<CountingKey>
        {
            public CountingKey(int value) => Value = value;

            public static int ComparisonCount { get; set; }
            public int Value { get; }

            public int CompareTo(CountingKey other)
            {
                ComparisonCount++;
                return Value.CompareTo(other.Value);
            }
        }

        private static LockstepCommand Command(long commandId, long sequence, long tick) => new LockstepCommand
        {
            CommandId = commandId,
            Sequence = sequence,
            RequestedTick = new LockstepTick(tick),
            TypeId = 42,
            Payload = new byte[] { 1, 2, 3 },
        };
    }
}
