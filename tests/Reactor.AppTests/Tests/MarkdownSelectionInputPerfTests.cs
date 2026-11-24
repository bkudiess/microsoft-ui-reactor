using global::System.Windows.Automation;
using global::System.Windows.Automation.Text;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

public sealed partial class MarkdownSelectionInputProofTests
{
    [TestMethod]
    public void Selection_RealGestureDrawProfile()
    {
        SessionInteractivityGuard.EnsureInputInjectable(nameof(Selection_RealGestureDrawProfile));
        GetWindowThreadProcessId((nint)TestSession.HostHwnd, out uint windowPid);
        Assert.AreEqual((uint)TestSession.HostPid, windowPid);
        AcquireForeground();
        var window = AutomationElement.FromHandle((nint)TestSession.HostHwnd);
        AutomationElement Find(string id) => window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new InvalidOperationException($"Missing {id}");
        Wait(() => window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "FixtureStatus")) is not null,
            "fixture navigator ready");
        bool held = false;
        nint previousDpi = SetThreadDpiAwarenessContext(-4);
        Assert.AreNotEqual((nint)0, previousDpi);
        try
        {
            for (int repeat = 0; repeat < 3; repeat++)
            {
                GuardForeground();
                if (Find("FixtureStatus").Current.Name != "Ready")
                {
                    ((InvokePattern)Find("ResetFixture").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    Wait(() => Find("FixtureStatus").Current.Name == "Ready", "fixture reset");
                }
                ((InvokePattern)Find("Nav_Markdown_Selection_Perf").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                Wait(() => Find("FixtureStatus").Current.Name == "Loaded: Markdown_Selection_Perf", "perf fixture");
                var message = Find("SelectionPerfScope");
                var text = (TextPattern)message.GetCurrentPattern(TextPattern.Pattern);
                var viewport = Find("SelectionPerfViewport");
                var scroll = (ScrollPattern)viewport.GetCurrentPattern(ScrollPattern.Pattern);
                Assert.IsTrue(scroll.Current.VerticallyScrollable);
                Thread.Sleep((int)GetDoubleClickTime() + 1);
                var glyph = Glyph(text.DocumentRange, 1);
                var bounds = viewport.Current.BoundingRectangle;
                double x = bounds.Left + 80;
                double startY = glyph[1] + glyph[3] / 2;
                Assert.IsTrue(startY >= bounds.Top && startY < bounds.Bottom);
                Point(glyph[0] + glyph[2] / 2, startY);
                Mouse(2); held = true;
                Wait(() => message.Current.HelpText.Contains("captures=1", StringComparison.Ordinal), "captured press");
                var anchor = text.GetSelection().Single().Clone();
                for (int i = 1; i <= 16; i++)
                    Point(x, startY + (bounds.Bottom - startY - 5) * i / 16);
                var grown = text.GetSelection().Single().Clone();
                string grownText = grown.GetText(-1);
                Assert.IsTrue(grownText.Length > 100, $"incremental selection grew: {grownText.Length}");
                Assert.AreEqual(0, grown.CompareEndpoints(TextPatternRangeEndpoint.Start, anchor, TextPatternRangeEndpoint.Start),
                    "the press anchor remains fixed during growth");
                double initialScroll = scroll.Current.VerticalScrollPercent;
                Point(x, bounds.Bottom + 12);
                Wait(() => scroll.Current.VerticalScrollPercent > initialScroll + .01, "captured edge autoscroll");
                Point(x, startY + 10);
                Wait(() => text.GetSelection().Single().GetText(-1).Length < grownText.Length, "reversal shrinks selection");
                Mouse(4); held = false;
                Wait(() => message.Current.HelpText.Split(" | ")[^1].StartsWith("release ", StringComparison.Ordinal) &&
                    message.Current.HelpText.Split(" | ")[^1].Contains("captures=0", StringComparison.Ordinal),
                    "release clears native capture");
                var released = text.GetSelection().Single().Clone();
                double scrollAfterRelease = scroll.Current.VerticalScrollPercent;
                Thread.Sleep(SampleIntervalMs * 2);
                Assert.IsTrue(released.Compare(text.GetSelection().Single()), "release stops selection updates");
                Assert.AreEqual(scrollAfterRelease, scroll.Current.VerticalScrollPercent, ScrollPercentTolerance,
                    "release stops scrolling");
                Console.WriteLine($"Real gesture repeat={repeat} grown={grownText.Length} edgeScroll={scrollAfterRelease:F3}");
            }
        }
        finally
        {
            try
            {
                if (held) Send(new Input { Union = new InputUnion { Mouse = new MouseInput { Flags = 4 } } });
            }
            finally { Assert.AreNotEqual((nint)0, SetThreadDpiAwarenessContext(previousDpi)); }
        }
    }
}
