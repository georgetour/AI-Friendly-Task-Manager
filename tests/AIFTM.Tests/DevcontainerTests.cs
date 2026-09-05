using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIFTM.Tests;

/// <summary>
/// The Codespaces entry point. With no screenshots in the README, the badge is the only way anyone
/// sees this app before cloning it, so what happens when it is clicked is worth pinning.
///
/// These assert configuration text rather than behaviour, which is usually a poor test — but the
/// files *are* the deliverable here, and a test run cannot launch a Codespace. Each assertion
/// corresponds to something that went wrong in a real one.
/// </summary>
public class DevcontainerTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".devcontainer"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(".devcontainer not found above the test output.");
    }

    /// <summary>The script with its comments removed. What the script *does* is the subject here —
    /// the comments explain the mistakes it avoids, and mention them by name, so matching against
    /// the whole file finds the words in the explanation rather than in the code.</summary>
    private static string Script() =>
        Regex.Replace(File.ReadAllText(Path.Combine(Root(), ".devcontainer", "start.sh")),
                      @"^\s*#.*$", "", RegexOptions.Multiline);

    /// <summary>Strips // comments so the JSONC the devcontainer spec allows can be parsed.</summary>
    private static JsonDocument Config() =>
        JsonDocument.Parse(Regex.Replace(File.ReadAllText(Path.Combine(Root(), ".devcontainer", "devcontainer.json")),
                                         @"^\s*//.*$", "", RegexOptions.Multiline));

    [Fact]
    public void The_app_is_started_bound_to_every_interface()
    {
        // It shipped binding to localhost. ASPNETCORE_URLS in containerEnv does not survive
        // `dotnet run`, which applies launchSettings.json and its applicationUrl of
        // http://localhost:5249 — so the port forwarder had nothing to reach.
        var script = Script();

        Assert.Contains("--urls", script);
        Assert.Contains("0.0.0.0", script);
        Assert.DoesNotContain("--urls \"http://localhost", script);
    }

    [Fact]
    public void Attaching_twice_does_not_start_a_second_copy()
    {
        // postAttachCommand runs on every attach, and a browser reconnecting counts. Unguarded,
        // the second run bound a port the first was holding and died with a page of stack trace —
        // which is what someone opening the badge actually saw.
        var script = Script();

        Assert.Contains("curl", script);
        Assert.Contains("already running", script);
        // Not pgrep: this script's own command line contains the project name, so a process check
        // would match itself and never start the app.
        Assert.DoesNotContain("pgrep", script);
    }

    [Fact]
    public void The_forwarded_address_is_printed_so_there_is_always_a_way_in()
    {
        // The preview pane is the intended entry, but a visitor who does not get one must not be
        // left hunting through a Ports tab for a port number.
        var script = Script();

        Assert.Contains("CODESPACE_NAME", script);
        Assert.Contains("GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN", script);
    }

    [Fact]
    public void The_forwarded_port_is_offered_as_a_button_rather_than_opened_automatically()
    {
        // Both alternatives were tried and both failed in a real Codespace.
        //
        //   openBrowser  asks for a new tab; browsers block programmatic popups, so a visitor was
        //                left with an editor and a README and no sign the app was running.
        //   openPreview  renders in an iframe, and the app sends frame-ancestors 'none' — so the
        //                pane shows a broken page no matter how many times it is refreshed.
        //
        // notify puts a button in front of the user. Clicking it is a gesture no blocker
        // interferes with, and it opens a real tab, where frame-ancestors does not apply.
        var attrs = Config().RootElement.GetProperty("portsAttributes").GetProperty("5249");

        Assert.Equal("notify", attrs.GetProperty("onAutoForward").GetString());
    }

    [Fact]
    public void The_app_still_forbids_being_framed()
    {
        // The reason openPreview cannot be used. If this header is ever relaxed, the choice above
        // is worth revisiting — and if it is relaxed by accident, this says so.
        var program = File.ReadAllText(Path.Combine(Root(), "src", "AIFTM.Api", "Program.cs"));

        Assert.Contains("frame-ancestors 'none'", program);
    }

    [Fact]
    public void The_port_it_forwards_is_the_port_it_starts_on()
    {
        // Two numbers that have to agree; nothing but this notices when they stop.
        var forwarded = Config().RootElement.GetProperty("forwardPorts")
            .EnumerateArray().Select(p => p.GetInt32()).ToArray();
        var started = int.Parse(Regex.Match(Script(), @"PORT=(\d+)").Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains(started, forwarded);
    }

    [Fact]
    public void Opening_a_codespace_restores_only_what_it_needs_to_run()
    {
        // Restoring the solution also pulls Playwright and the test SDK, which nobody opening a
        // Codespace to look at the board is waiting for on purpose.
        var create = Config().RootElement.GetProperty("postCreateCommand").GetString()!;

        Assert.Contains("src/AIFTM.Api", create);
    }
}
