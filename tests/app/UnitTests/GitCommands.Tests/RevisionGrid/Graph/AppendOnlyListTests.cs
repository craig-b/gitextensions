using GitUI.UserControls.RevisionGrid.Graph;

namespace GitCommandsTests.RevisionGrid.Graph;

public class AppendOnlyListTests
{
    [Test]
    public void Add_and_index_and_enumerate()
    {
        AppendOnlyList<string> list = new();

        list.Count.Should().Be(0);
        list.Add("a");
        list.Add("b");
        list.Add("c");

        list.Count.Should().Be(3);
        list[0].Should().Be("a");
        list[2].Should().Be("c");
        list.Should().Equal("a", "b", "c");
    }

    [Test]
    public void EnsureCapacity_keeps_existing_elements()
    {
        AppendOnlyList<string> list = new(capacity: 1);
        list.Add("a");

        list.EnsureCapacity(100);

        list.Count.Should().Be(1);
        list[0].Should().Be("a");
    }

    [Test]
    public void Published_count_never_exposes_an_unwritten_slot()
    {
        // The publication contract the graph's render path depends on: any index below an
        // observed Count reads a fully written element, even while the single writer appends
        // and the backing array grows. List<T>.Add violates this (it publishes the size before
        // the slot), which crashed the client's render thread against the cache pump.
        const int total = 200_000;
        AppendOnlyList<object> list = new();
        Exception? readerFailure = null;

        using CancellationTokenSource done = new();
        Thread reader = new(() =>
        {
            try
            {
                while (!done.Token.IsCancellationRequested)
                {
                    int count = list.Count;
                    if (count > 0)
                    {
                        // The most recently published slots are the racy ones.
                        list[count - 1].Should().NotBeNull();
                        list[count / 2].Should().NotBeNull();
                        list[0].Should().NotBeNull();
                    }
                }
            }
            catch (Exception ex)
            {
                readerFailure = ex;
            }
        });

        reader.Start();
        for (int i = 0; i < total; i++)
        {
            list.Add(new object());
        }

        done.Cancel();
        reader.Join();

        readerFailure.Should().BeNull();
        list.Count.Should().Be(total);
    }
}
