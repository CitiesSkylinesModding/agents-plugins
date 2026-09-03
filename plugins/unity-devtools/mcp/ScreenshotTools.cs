using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using JetBrains.Annotations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UnityDevtools.Sdb;

namespace UnityDevtools.Mcp;

/// <summary>
/// The screenshot tool: a one-liner over <see cref="Screenshot"/>, which owns the whole capture.
/// Only the image-block construction lives here, so the sdb library never references the MCP SDK.
/// The blocks are returned as themselves rather than through a record the SDK renders as text:
/// that path serializes the image into the result as base64 prose and nothing fails, so the
/// mistake reaches the caller looking like a successful capture.
/// </summary>
[McpServerToolType]
[UsedImplicitly]
public sealed class ScreenshotTools(UnitySession session) {
  [McpServerTool(Name = "screenshot")]
  [Description(
    """
    See the game: capture the frame the renderer is drawing, everything a player at that machine
    would see, and return it as an inline PNG image. Needs no ECS world.
    NOT a read-only observation: a capture is a request that only a rendered frame fulfils, so this
    lets the game run up to about 0.2 s before returning. It leaves your held suspend windows
    exactly as it found them, but the game does advance inside them, so read what you still need
    from a frozen state before capturing.
    It refuses while a breakpoint/step/exception pause holds the game, without disturbing it:
    releasing that pause is the caller's decision, not this tool's.
    To photograph a particular view, aim the game's camera through eval first, then capture.
    """
  )]
  [UsedImplicitly]
  public IReadOnlyList<ContentBlock> Capture() {
    return ToolGuard.Run(() => {
        var capture = Screenshot.From(session);

        var width = capture.Width.ToString(CultureInfo.InvariantCulture);
        var height = capture.Height.ToString(CultureInfo.InvariantCulture);
        var ran = capture.Ran.TotalSeconds.ToString("0.0#", CultureInfo.InvariantCulture);

        // The dimensions say what the image costs this caller's context, and the run time what the
        // capture cost their game -- which is the number that matters inside a suspend window.
        var note = $"{width}x{height} px PNG; the game ran {ran} s to render it.";

        // The fault says which of the two it was, so this says neither: one leaves the game
        // running with the caller's window gone, the other leaves it holding a suspension this
        // call could not give back.
        if (capture.HoldFault is {} fault) {
          note += $" WARNING: the capture could not give its suspension back ({fault}). Read " +
            "status before your next write rather than assuming the game is where you left it.";
        }

        if (capture.Paused) {
          note += " A breakpoint/step/exception pause hit during that window: inspect it with " +
            "the debug tools, or release it with debug_step action=resume. If debug_pause_state " +
            "reports none, the pump was still classifying one that has since resumed itself, " +
            "and the game is running.";
        }

        return (IReadOnlyList<ContentBlock>) [
          new TextContentBlock {
            Text = note
          },
          ImageContentBlock.FromBytes(capture.Png, "image/png")
        ];
      }
    );
  }
}
