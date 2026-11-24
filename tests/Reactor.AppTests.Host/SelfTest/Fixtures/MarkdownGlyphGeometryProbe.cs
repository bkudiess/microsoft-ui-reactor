using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class MarkdownGlyphGeometryProbe(Harness harness) : SelfTestFixtureBase(harness)
{
    public override TimeSpan FixtureTimeout => TimeSpan.FromMinutes(2);
    public override async Task RunAsync()
    {
        var options = new MarkdownTextSelectionOptions();
        string paragraph = string.Join(" ", Enumerable.Repeat(
            "A longer wrapped paragraph exercises selection hit testing, office affinity ffi, and 123-45 native text layout.", 4));
        string text = $"## Heading\n\n{paragraph}\n\n**bold** with _emphasis_ and `code`\n\n" +
            $"unicode e\u0301, \U0001F469\u200D\U0001F4BB, \u05d0\u05d1\u05d2\n\n{paragraph}";
        MarkdownSelectionHost? scope = null;
        ScrollViewer? scroll = null;
        var host = H.CreateHost();
        host.Mount(_ => ScrollViewer(MarkdownTextSelection.Scope(options,
            Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, new MarkdownOptions { TextSelection = options }))
            .OnMountAdd(fe => scope = (MarkdownSelectionHost)fe)).Width(640).Height(480)
            .OnMountAdd(fe => scroll = (ScrollViewer)fe));
        await Harness.Render();
        var selection = scope ?? throw new InvalidOperationException("Selection scope not mounted");
        selection.Refresh();
        var viewport = selection.Viewport();
        int samples = 0, right = 0, rightBad = 0, same = 0, different = 0;
        foreach (var entry in selection.Document.Entries)
        {
            for (int offset = entry.Start; offset < entry.End;)
            {
                int next = selection.Document.MoveGrapheme(offset, 1);
                var start = selection.CharacterBounds(offset);
                var following = selection.CharacterBounds(next);
                var original = selection.GlyphBounds(offset, next);
                var substitute = MarkdownSelectionHost.GlyphRectangle(start, following);
                bool equal = original == substitute;
                if (equal) same++; else different++;
                if (start.X >= viewport.Right)
                {
                    right++;
                    var clipped = MarkdownSelectionHost.Intersect(original, viewport);
                    if (clipped.Width > 0 && clipped.Height > 0) rightBad++;
                }
                samples++;
                offset = Math.Max(offset + 1, next);
            }
        }
        Console.WriteLine($"# GlyphProbe samples={samples} right={right} rightVisible={rightBad} forwardSame={same} forwardDifferent={different} viewport={viewport}");
        H.Check("GlyphProbe_EnoughSamples", samples > 600);
        int maxSkipped = 0;
        int hitCases = 0, hitMismatches = 0;
        foreach (string font in new[] { "Segoe UI", "Times New Roman", "Consolas" })
        foreach (bool rtl in new[] { false, true })
        {
            foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
            {
                rich.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                rich.FontFamily = new FontFamily(font);
            }
            foreach (var alignment in new[] { TextAlignment.Left, TextAlignment.Right })
            {
                foreach (var rich in MarkdownSelectionHost.Descendants(selection).OfType<RichTextBlock>())
                    rich.TextAlignment = alignment;
            foreach (int width in new[] { 320, 640 })
            {
                scroll!.Width = width;
                await Harness.Render();
                foreach (int scrollY in new[] { 0, 80 })
                {
                    scroll.ChangeView(null, scrollY, null, true);
                    await Harness.Render();
                    selection.Refresh();
                    var area = selection.Viewport();
                    for (double y = area.Top - 5; y <= area.Bottom + 5; y += 17)
                    for (double x = area.Left - 5; x <= area.Right + 5; x += 31)
                    {
                        var point = new Point(x, y);
                        selection.ProbeDisableHitOptimization = true;
                        var expectedHit = selection.ProbeHitTest(point);
                        selection.ProbeDisableHitOptimization = false;
                        var actualHit = selection.ProbeHitTest(point);
                        hitCases++;
                        if (expectedHit != actualHit)
                        {
                            if (hitMismatches < 10)
                                Console.WriteLine($"# Hit mismatch font={font} rtl={rtl} alignment={alignment} width={width} scroll={scrollY} x={x} y={y} baseline={expectedHit} candidate={actualHit}");
                            hitMismatches++;
                        }
                    }
                    if (font == "Segoe UI" && !rtl && alignment == TextAlignment.Left && width == 640 && scrollY == 0)
                    {
                        foreach (var entry in selection.Document.Entries)
                        for (int offset = entry.Start; offset < entry.End; offset = Math.Max(offset + 1, selection.Document.MoveGrapheme(offset, 1)))
                        {
                            var rect = selection.CharacterBounds(offset);
                            if (rect.X < area.Left || rect.X > area.Right || rect.Y < area.Top || rect.Y > area.Bottom) continue;
                            foreach (double dx in new[] { -.5, 0, .5 })
                            foreach (double dy in new[] { -.01, .01, rect.Height / 2, rect.Height - .01, rect.Height + .01 })
                            {
                                var point = new Point(rect.X + dx, rect.Y + dy);
                                selection.ProbeDisableHitOptimization = true;
                                var expectedHit = selection.ProbeHitTest(point);
                                selection.ProbeDisableHitOptimization = false;
                                var actualHit = selection.ProbeHitTest(point);
                                hitCases++;
                                if (expectedHit != actualHit)
                                {
                                    if (hitMismatches < 10)
                                        Console.WriteLine($"# Hit boundary mismatch offset={offset} x={point.X} y={point.Y} baseline={expectedHit} candidate={actualHit}");
                                    hitMismatches++;
                                }
                            }
                        }
                    }
                    foreach (int anchor in new[] { 0, 37, 200 })
                    {
                        for (int end = 50; end <= selection.Document.Text.Length; end += 75)
                        {
                            selection.ProbeDisableClipOptimization = true;
                            selection.SelectRefreshed(anchor, end);
                            var baseline = selection.ProbeSelectionRects.ToArray();
                            selection.ProbeDisableClipOptimization = false;
                            selection.SelectRefreshed(anchor, end);
                            var fast = selection.ProbeSelectionRects.ToArray();
                            H.Check($"GlyphProbe_Parity_{font}_{rtl}_{alignment}_{width}_{scrollY}_{anchor}_{end}", baseline.SequenceEqual(fast));
                            maxSkipped = Math.Max(maxSkipped, selection.ProbeSkippedGlyphs);
                            if (rtl) H.Check($"GlyphProbe_RtlDoesNotSkip_{font}_{alignment}_{width}_{scrollY}_{anchor}_{end}", selection.ProbeSkippedGlyphs == 0);
                        }
                        }
                    }
                }
            }
        }
        H.Check("GlyphProbe_OptimizationExercised", maxSkipped > 100, $"skipped={maxSkipped}");
        Console.WriteLine($"# Hit comparison cases={hitCases} mismatches={hitMismatches} native={selection.ProbeNativeHitCount}");
        H.Check("GlyphProbe_HitParity", hitCases > 1000 && hitMismatches == 0,
            $"cases={hitCases} mismatches={hitMismatches}");
        H.Check("GlyphProbe_NativeHitExercised", selection.ProbeNativeHitCount > 0,
            $"native={selection.ProbeNativeHitCount}");
        host.Dispose();
        await Harness.Render();
    }
}
