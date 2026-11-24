using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal sealed class MarkdownSelectionPerfFixture : Component
{
    private readonly MarkdownTextSelectionOptions _options = new();

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

    public override Element Render()
    {
        var messages = Enumerable.Range(0, 32).Select(index =>
        {
            Element content = Microsoft.UI.Reactor.Advanced.Factories.Markdown(
                Message(index), new MarkdownOptions { TextSelection = _options });
            if (index == 0)
                content = VStack(content).Set(MarkdownSelectionInputFixture.TracePointerRouting);
            Element scope = MarkdownTextSelection.Scope(_options, content);
            return index == 0 ? scope.AutomationId("SelectionPerfScope") : scope;
        }).ToArray();
        return ScrollViewer(VStack(messages)).Height(480).Width(640).AutomationId("SelectionPerfViewport");
    }
}
