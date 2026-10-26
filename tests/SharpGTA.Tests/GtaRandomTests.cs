using System;
using SharpGTA.Runtime;

namespace SharpGTA.Tests
{
    public sealed class GtaRandomTests
    {
        [Test]
        public void OriginalSequenceMatchesIndependentUncheckedFormula()
        {
            GtaRandom random = new GtaRandom();
            uint expectedState = 1u;
            Assert.Equal(1u, random.State);

            for (int index = 0; index < 12; index++)
            {
                expectedState = unchecked(expectedState * 1103515245u + 12345u);
                int expectedValue = (int)((expectedState >> 16) & 0x7fffu);
                Assert.Equal(expectedValue, random.Next());
                Assert.Equal(expectedState, random.State);
            }
        }

        [Test]
        public void ConstructorSeedAndStateControlTheSequence()
        {
            GtaRandom expected = new GtaRandom(123456789u);
            GtaRandom random = new GtaRandom();
            random.Seed(123456789u);
            Assert.Equal(123456789u, random.State);

            for (int index = 0; index < 4; index++)
                Assert.Equal(expected.Next(), random.Next());

            random.State = 1u;
            GtaRandom original = new GtaRandom();
            Assert.Equal(original.Next(), random.Next());

            uint savedState = random.State;
            int next = random.Next();
            random.State = savedState;
            Assert.Equal(next, random.Next());

            random.Seed(0u);
            Assert.Equal(0u, random.State);
            GtaRandom zeroSeeded = new GtaRandom(0u);
            Assert.Equal(zeroSeeded.Next(), random.Next());
        }

        [Test]
        public void NextMaxExclusiveUsesModuloAndValidatesRange()
        {
            GtaRandom bounded = new GtaRandom(987654321u);
            GtaRandom unbounded = new GtaRandom(987654321u);
            for (int index = 0; index < 20; index++)
            {
                int expected = unbounded.Next() % 37;
                int actual = bounded.Next(37);
                Assert.Equal(expected, actual);
                Assert.True(actual >= 0 && actual < 37);
            }

            Assert.Equal(0, bounded.Next(1));
            uint state = bounded.State;
            Assert.Throws<ArgumentOutOfRangeException>(delegate { bounded.Next(0); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { bounded.Next(-1); });
            Assert.Equal(state, bounded.State);
        }
    }
}
