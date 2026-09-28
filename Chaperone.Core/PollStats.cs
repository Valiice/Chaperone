namespace Chaperone.Core;

/// <summary>Ring buffer of poll costs for the debug tab. Fixed size, no allocations after construction.</summary>
public sealed class PollStats
{
    private const int Capacity = 64;
    private const long WindowMs = 1000;

    private readonly long[] times = new long[Capacity];
    private readonly double[] costs = new double[Capacity];
    private int next;
    private int count;
    private long latestMs;

    public double Last { get; private set; }

    public void Record(long nowMs, double micros)
    {
        this.times[this.next] = nowMs;
        this.costs[this.next] = micros;
        this.next = (this.next + 1) % Capacity;
        this.count = Math.Min(this.count + 1, Capacity);
        this.latestMs = nowMs;
        this.Last = micros;
    }

    public double AverageLastSecond
    {
        get
        {
            double sum = 0;
            var n = 0;
            for (var i = 0; i < this.count; i++)
            {
                if (this.latestMs - this.times[i] <= WindowMs)
                {
                    sum += this.costs[i];
                    n++;
                }
            }

            return n == 0 ? 0 : sum / n;
        }
    }

    public double MaxLastSecond
    {
        get
        {
            double max = 0;
            for (var i = 0; i < this.count; i++)
            {
                if (this.latestMs - this.times[i] <= WindowMs && this.costs[i] > max)
                {
                    max = this.costs[i];
                }
            }

            return max;
        }
    }
}
