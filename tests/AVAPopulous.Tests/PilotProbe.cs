using AVAPopulous.Engine;

namespace AVAPopulous.Tests;

/// <summary>How the computer god gets on (set POPULOUS_PROBE to run it; POPULOUS_PROBE=lazy does nothing at all).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_god_does()
    {
        if (Environment.GetEnvironmentVariable("POPULOUS_PROBE") is not { } mode) return;
        int last = int.TryParse(Environment.GetEnvironmentVariable("POPULOUS_WORLDS"), out int n) ? n : 6;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new Pilot();
        int first = int.TryParse(Environment.GetEnvironmentVariable("POPULOUS_FROM"), out int f0) ? f0 : 1;
        for (int level = first; level <= last; level++)
        {
            var w = new World();
            Sim.Start(w, level);
            var casts = new Dictionary<string, int>();
            int t = 0;
            string trace = "";
            for (; t < 60 * 60 * 12 && w.Winner < 0; t++)
            {
                if (mode != "lazy")
                {
                    var c = p.Decide(w);
                    var d = new Deed(c.Point?.X ?? 0, c.Point?.Y ?? 0, c.Raise, c.Lower, c.Cast);
                    if ((c.Raise || c.Lower || c.Cast != Power.None) && Sim.Apply(w, World.Good, d) && c.Cast != Power.None)
                        casts[c.Cast.ToString()] = casts.GetValueOrDefault(c.Cast.ToString()) + 1;
                }
                Sim.Step(w);
                if (t % (60 * 30) == 0)
                    trace += $" {t / 60}s:{w.Population(0):0}/{w.Population(1):0}({w.Towns.Count(x => x.Side == 0)}/{w.Towns.Count(x => x.Side == 1)})";
            }
            output.WriteLine($"world {level}: {(w.Winner == 0 ? "WON" : w.Winner == 1 ? "LOST" : "undecided")} after {t / 60}s; good {w.Population(0):0} ({w.Towns.Count(x => x.Side == 0)} towns), evil {w.Population(1):0} ({w.Towns.Count(x => x.Side == 1)}); casts {string.Join(",", casts.Select(kv => kv.Key + "=" + kv.Value))}");
            output.WriteLine("   " + trace);
        }
        output.WriteLine($"{sw.ElapsedMilliseconds} ms");
    }
}
