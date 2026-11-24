using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed partial class MarkdownResponsivenessProbe(Harness harness) : SelfTestFixtureBase(harness)
{
    public override TimeSpan FixtureTimeout => TimeSpan.FromMinutes(5);
    private readonly List<PerfSample> _results = [];
    private readonly List<double> _hitTestMs = [];
    private readonly List<double> _selectMs = [];
    private readonly List<double> _refreshMs = [];
    private readonly List<int> _offsets = [];
    private const int Operations = 48;
    private const int Messages = 32;
    internal sealed record PerfSample(string Phase, int Repeat, bool Selection, int Messages, int Operations,
        double Scale, List<double> LatencyMs, List<double> SynchronousWorkMs, List<double> RenderingIntervalsMs,
        double ElapsedFrameMs, int Over16_67, int Over33_33,
        List<double> HitTestMs, List<double> SelectMs, List<double> RefreshMs, List<int> Offsets);

    private static string Message(int index) => $"""
        ## Message {index}

        A mixed transcript with **important text**, *emphasis*, `inline code`,
        and a [reference](https://example.com). Unicode: café — 日本語.

        > Quoted context with enough prose to wrap across the viewport.

        - First item with **bold details**
        - Second item with nested text and `code`

        | Column | Value |
        | --- | --- |
        | latency | measured |
        | state | streaming |

        ```csharp
        for (int i = 0; i < 20; i++) Console.WriteLine(i);
        ```

        {string.Join("\n\n", Enumerable.Range(0, 5).Select(i =>
            $"Paragraph {i}: " + string.Join(" ", Enumerable.Repeat(
                "A longer wrapped paragraph exercises selection hit testing and native text layout.", 4))))}
        """;

    internal sealed class Probe(bool enabled)
    {
        internal bool Enabled = enabled;
        internal TextBox Composer = null!;
        internal TextBlock Typed = null!;
        internal TextBlock Streamed = null!;
        internal ScrollViewer Scroll = null!;
        internal MarkdownSelectionHost? Selection;
        internal Action<int> Stream = null!;
        internal Element[] Static = [];
    }

    internal sealed class StreamMessage : Component<Probe>
    {
        public override Element Render()
        {
            var (revision, setRevision) = UseState(0);
            Props.Stream = value => setRevision(value);
            var options = Props.Enabled ? new MarkdownTextSelectionOptions() : null;
            Element content = Microsoft.UI.Reactor.Advanced.Factories.Markdown(
                Message(Messages - 1) + $"\n\nStreaming token {revision}",
                new MarkdownOptions { TextSelection = options });
            if (options is not null) content = MarkdownTextSelection.Scope(options, content);
            return VStack(TextBlock($"stream {revision}").OnMountAdd(fe => Props.Streamed = (TextBlock)fe), content);
        }
    }

    internal sealed class Transcript : Component<Probe>
    {
        public override Element Render()
        {
            var (typed, setTyped) = UseState("");
            return VStack(
                TextBox(typed, value => setTyped(value)).OnMountAdd(fe => Props.Composer = (TextBox)fe),
                TextBlock($"typed {typed}").OnMountAdd(fe => Props.Typed = (TextBlock)fe),
                ScrollViewer(VStack([.. Props.Static, Component<StreamMessage, Probe>(Props)]))
                    .Height(480).OnMountAdd(fe => Props.Scroll = (ScrollViewer)fe)).Width(640);
        }
    }

    public override async Task RunAsync()
    {
        for (int repeat = 0; repeat < 3; repeat++)
        {
            // Alternate mode order to reduce systematic warm-up/order bias.
            foreach (bool enabled in repeat % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var probe = new Probe(enabled);
                probe.Static = Enumerable.Range(0, Messages - 1).Select(index =>
                {
                    var options = enabled ? new MarkdownTextSelectionOptions() : null;
                    Element content = Microsoft.UI.Reactor.Advanced.Factories.Markdown(
                        Message(index), new MarkdownOptions { TextSelection = options });
                    if (options is not null)
                    {
                        content = MarkdownTextSelection.Scope(options, content);
                        if (index == 0) content = content.OnMountAdd(fe => probe.Selection = (MarkdownSelectionHost)fe);
                    }
                    return content;
                }).ToArray();
                var host = H.CreateHost();
                host.Mount(_ => Component<Transcript, Probe>(probe));
                await Harness.Render();
                probe.Composer.Focus(FocusState.Programmatic);
                for (int warm = 0; warm < 8; warm++)
                {
                    probe.Composer.Text = $"warm {warm}";
                    await Harness.Render();
                }

                await Measure("typing-idle", repeat, probe, i => probe.Composer.Text = $"typed {i}",
                    i => probe.Typed.Text == $"typed typed {i}");
                await Measure("scroll-idle", repeat, probe,
                    i => probe.Scroll.ChangeView(null, (i % 24) * 24, null, true),
                    i => Math.Abs(probe.Scroll.VerticalOffset - (i % 24) * 24) < 1);
                await Measure("streaming-idle", repeat, probe, i => probe.Stream(i + 1),
                    i => probe.Streamed.Text == $"stream {i + 1}");
                if (probe.Selection is { } selection)
                {
                    probe.Scroll.ChangeView(null, 0, null, true);
                    await Harness.Render();
                    selection.Select(0, 12);
                    await Measure("synthetic-hit-test-selection", repeat, probe, i =>
                    {
                        long refreshStart = Stopwatch.GetTimestamp();
                        selection.Refresh();
                        _refreshMs.Add(Stopwatch.GetElapsedTime(refreshStart).TotalMilliseconds);
                        var viewport = selection.Viewport();
                        long hitStart = Stopwatch.GetTimestamp();
                        int offset = selection.HitTest(new Point(
                            viewport.Left + 80 + i % 12 * 8, viewport.Top + 70 + i % 8 * 18));
                        _hitTestMs.Add(Stopwatch.GetElapsedTime(hitStart).TotalMilliseconds);
                        _offsets.Add(offset);
                        long selectStart = Stopwatch.GetTimestamp();
                        selection.SelectRefreshed(0, offset);
                        _selectMs.Add(Stopwatch.GetElapsedTime(selectStart).TotalMilliseconds);
                    }, _ => true);
                    await Measure("synthetic-edge-scroll", repeat, probe, _ =>
                    {
                        var viewport = selection.ScrollViewport();
                        selection.ScrollDrag(selection.RootPoint(
                            new Point(viewport.Left + viewport.Width / 2, viewport.Bottom + 12)));
                    }, _ => true);
                    selection.Select(0, 0);
                }
                host.Dispose();
                await Harness.Render();
            }
        }
        var output = Environment.GetEnvironmentVariable("REACTOR_MARKDOWN_PERF_OUTPUT")
            ?? throw new InvalidOperationException("REACTOR_MARKDOWN_PERF_OUTPUT must name the output file.");
        global::System.IO.File.WriteAllText(output, JsonSerializer.Serialize(_results,
            MarkdownPerfJsonContext.Default.ListPerfSample));
        H.Check("MarkdownPerf_CollectedRepeatedSamples", _results.Count == 24);
    }

    private async Task Measure(string phase, int repeat, Probe probe, Action<int> action, Func<int, bool> ready)
    {
        var latency = new List<double>();
        var work = new List<double>();
        var frames = new List<double>();
        _hitTestMs.Clear();
        _selectMs.Clear();
        _refreshMs.Clear();
        _offsets.Clear();
        long lastFrame = 0, pendingStart = 0;
        int index = -1;
        TaskCompletionSource? acknowledgement = null;
        void OnFrame(object? sender, object args)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastFrame != 0) frames.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
            lastFrame = now;
            if (acknowledgement is not null && ready(index))
            {
                latency.Add(Stopwatch.GetElapsedTime(pendingStart, now).TotalMilliseconds);
                acknowledgement.TrySetResult();
                acknowledgement = null;
            }
        }
        CompositionTarget.Rendering += OnFrame;
        try
        {
            for (index = 0; index < Operations; index++)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                acknowledgement = completion;
                pendingStart = Stopwatch.GetTimestamp();
                action(index);
                work.Add(Stopwatch.GetElapsedTime(pendingStart).TotalMilliseconds);
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(16);
            }
        }
        finally { CompositionTarget.Rendering -= OnFrame; }
        if (frames.Count < Operations / 2 || latency.Count != Operations)
            throw new InvalidOperationException($"Insufficient frame signal for {phase}: frames={frames.Count}, operations={latency.Count}.");
        _results.Add(new PerfSample(phase, repeat, probe.Enabled, Messages, Operations,
            probe.Composer.XamlRoot.RasterizationScale, latency, work, frames, frames.Sum(),
            frames.Count(value => value > 1000.0 / 60), frames.Count(value => value > 1000.0 / 30),
            [.. _hitTestMs], [.. _selectMs], [.. _refreshMs], [.. _offsets]));
        Console.WriteLine($"# MarkdownPerf {phase} repeat={repeat} selection={probe.Enabled} frames={frames.Count}");
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(List<MarkdownResponsivenessProbe.PerfSample>))]
    internal partial class MarkdownPerfJsonContext : JsonSerializerContext;
}
