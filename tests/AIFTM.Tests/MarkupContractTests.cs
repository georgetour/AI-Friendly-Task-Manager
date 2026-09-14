using System.Text.RegularExpressions;

namespace AIFTM.Tests;

/// <summary>
/// Markup facts that a change can quietly undo, and that no server test would notice.
///
/// Each of these was a real finding from clicking through every screen. They are asserted against
/// the shipped files rather than a rendered page because the point is the source contract: a
/// button cannot open in a new tab however it is styled, and an icon with no accessible name is
/// unnamed no matter what the tooltip says.
/// </summary>
public class MarkupContractTests
{
    private static string Read(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AIFTM.Api", "wwwroot", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"wwwroot/{name} not found — expected under the repo root.");
    }

    [Fact]
    public void The_logo_is_a_link_so_it_can_be_opened_in_a_new_tab()
    {
        // It used to be a <button>, which no amount of JavaScript can make ctrl-clickable.
        var html = Read("index.html");

        var logo = Regex.Match(html, @"<a[^>]*id=""logoSlot""[^>]*>", RegexOptions.Singleline);
        Assert.True(logo.Success, "The logo must be an <a> — a <button> cannot open in a new tab.");
        // Bound, not literal: served from a folder, the Overview is not at "/". That the rendered
        // href is right is asserted in the browser, in UiTests.
        Assert.Contains(@"x-bind:href=""homeUrl""", logo.Value);
    }

    [Fact]
    public void A_plain_click_on_the_logo_is_handled_but_modified_clicks_are_left_to_the_browser()
    {
        // Intercepting every click would break the thing the <a> was introduced for.
        var js = Read("app.js");
        var body = Regex.Match(js, @"logoNav\(e\)\{.*?\n    \}", RegexOptions.Singleline).Value;

        Assert.False(string.IsNullOrEmpty(body), "logoNav is what keeps in-app navigation instant.");
        foreach (var modifier in new[] { "metaKey", "ctrlKey", "shiftKey", "altKey", "button" })
            Assert.Contains(modifier, body);
    }

    [Theory]
    [InlineData("btnSync")]
    [InlineData("btnStage")]
    [InlineData("btnTheme")]
    public void Every_icon_only_button_has_an_accessible_name(string id)
    {
        // Below 900px these lose their visible label, and a phone has no hover — so a title
        // attribute alone leaves them unidentifiable.
        var html = Read("index.html");
        var button = Regex.Match(html, $@"<button[^>]*id=""{id}""[^>]*>", RegexOptions.Singleline);

        Assert.True(button.Success, $"Could not find #{id} in index.html.");
        Assert.Contains("aria-label=", button.Value);
    }

    [Fact]
    public void The_release_slot_is_hidden_rather_than_removed()
    {
        // A story no longer carries a release — the epic does — so the slot that used to hide
        // rather than collapse moved to the epic header. Whether the columns actually line up is
        // asserted in a browser, in UiTests — measuring rendered positions rather than matching
        // stylesheet text. This only pins the markup decision behind it: hidden when empty, never
        // removed.
        var html = Read("index.html");

        Assert.Contains(@"x-bind:class=""section.releaseSlotClass""", html);
        Assert.Contains(@"x-bind:class=""section.versionSlotClass""", html);
        Assert.DoesNotContain(@"<span class=""vtag"" x-show=""section.release""", html);
    }

    [Fact]
    public void The_story_row_no_longer_binds_a_release()
    {
        var html = Read("index.html");

        Assert.DoesNotContain("story.release", html, StringComparison.Ordinal);
        Assert.DoesNotContain("story.releaseSlotClass", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_epic_header_shows_the_release()
    {
        var html = Read("index.html");

        Assert.Contains("section.releaseLabel", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_epic_forms_offer_a_version_field_and_a_release_picker()
    {
        // Without this, an epic filed under the wrong release by conversion could only be moved by
        // hand-editing the file.
        var html = Read("index.html");

        var addForm = Regex.Match(html, @"page === 'add-epic'.*?</form>", RegexOptions.Singleline).Value;
        var editForm = Regex.Match(html, @"page === 'edit-epic'.*?</form>", RegexOptions.Singleline).Value;

        foreach (var form in new[] { addForm, editForm })
        {
            Assert.Contains(@"name=""version""", form, StringComparison.Ordinal);
            Assert.Contains(@"<select", form, StringComparison.Ordinal);
            Assert.Contains(@"name=""release""", form, StringComparison.Ordinal);
        }
    }
}
