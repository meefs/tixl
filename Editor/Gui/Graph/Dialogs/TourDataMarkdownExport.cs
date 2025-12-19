#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Graph.Dialogs;

internal static class TourDataMarkdownExport
{
    /// <summary>
    /// Parses mark down description for tour data matching symbols and tour points.
    /// </summary>
    /// <remarks>
    /// Copy sample markdown from existing symbols with tours.
    /// </remarks>
    internal static bool TryPasteTourData(SymbolUi compositionUi)
    {
        var markdown = ImGui.GetClipboardText();
        _state = States.SymbolStarted;

        var span = markdown.AsSpan();
        var lineIndex = 0;
        var i = 0;

        var id = compositionUi.Symbol.Id.ShortenGuid();

        while (i < span.Length)
        {
            var lineStart = i;

            // find end of line
            while (i < span.Length && span[i] != '\n')
                i++;

            var line = span.Slice(lineStart, i - lineStart);
            lineIndex++;

            // handle CRLF
            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];

            // classify
            if (line.StartsWith("## ".AsSpan()))
            {
                if (_state != States.SymbolStarted)
                {
                    Log.Warning($"Invalid format in line {lineIndex}");
                    return false;
                }

                var tourPointHeader = line[3..];
                _state = States.TourPointStarted;
            }
            else if (line.StartsWith("# ".AsSpan()))
            {
                var symbolHeader = line[2..];
                _state = States.SymbolStarted;
                TryStartTourForSymbol(line, markdown, i, compositionUi);
            }
            else if (line.IsEmpty)
            {
                
            }
            else
            {
                if (_state != States.TourPointStarted)
                {
                    Log.Warning($"Invalid Format: Content may not start without introducing ## tourPointHeader Line {lineIndex}");
                    return false;
                }

                var lineContent = line;
            }

            i++; // skip '\n'
        }

        return false;
    }

    private static bool TryStartTourForSymbol(ReadOnlySpan<char> line, string input, int i, SymbolUi compositionUi)
    {
        if (!TryGetIdString(line, out var idString))
            return false;

        Log.Debug($" Found {idString} in line");
        
        if (compositionUi.Symbol.Id.ShortenGuid() == idString)
        {
            Log.Debug("Matches symbolId " + compositionUi.Symbol.Id);
            return true;
        }

        foreach (var  childUi in compositionUi.ChildUis.Values)
        {
            var testId = childUi.SymbolChild.Symbol.Id.ShortenGuid();
            if (idString.SequenceEqual(testId.AsSpan()))
            {
                Log.Debug("Matches symbolId child" + childUi.SymbolChild.Symbol.Id);
                //return true;
            }
        }
        
        return true;
    }

    /** // Find symbol id " &abc1234" **/
    private static bool TryGetIdString(ReadOnlySpan<char> line, out ReadOnlySpan<char> id)
    {
        id = ReadOnlySpan<char>.Empty;

        var idStartIndex = line.IndexOf(" &");
        if (idStartIndex == -1)
            return false;

        var maxLength = 7;
        if (line.Length < idStartIndex + maxLength + 2)
            return false;

        if (line[idStartIndex + 2] == ' ')
            return false;

        //var endIndex = idStartIndex + 2;
        var length = 0;
        while (length < maxLength && line[idStartIndex + 2 + length] != ' ')
            length++;

        id = line.Slice(idStartIndex +2, length);
        return true;
    }

    private static States _state;

    private enum States
    {
        SymbolStarted,
        TourPointStarted,
        TourPointContent,
    }
}