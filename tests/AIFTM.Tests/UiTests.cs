using Microsoft.Playwright;

namespace AIFTM.Tests;

/// <summary>
/// What the page actually does, in a browser, at a real size.
///
/// Each of these corresponds to something that was broken and found by hand. The point is not
/// coverage for its own sake — it is that none of them could have been caught by any other test in
/// this suite, because they are all consequences of CSS and of Alpine binding to the DOM.
/// </summary>
[Collection("ui")]
public class UiTests(UiFixture fx)
{
    [Fact]
    public async Task The_board_renders_its_stories()
    {
        // A smoke test that means more than it looks: the rows only exist if Alpine started, which
        // it will not do under the strict CSP if a binding uses syntax the CSP build rejects.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();
        Assert.Equal(2, await page.Locator(".story-row:visible").CountAsync());
        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task The_add_menu_opens_from_the_plus_button_on_a_phone()
    {
        // This is the bug. #addMenu is a child of .addwrap, and hiding .addwrap on mobile removed
        // the menu with it, so the bottom bar's + toggled state nothing could see. No C# test could
        // have seen that, because nothing about the markup or the CSS text is wrong on its face.
        var (page, errors) = await fx.NewPageAsync(390, 760);
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("#addMenu")).Not.ToBeVisibleAsync();
        await page.Locator(".mnav-add").ClickAsync();

        await Assertions.Expect(page.Locator("#addMenu")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#addMenu button", new() { HasTextString = "New epic" })).ToBeVisibleAsync();
        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task The_add_menu_still_opens_from_the_pill_on_a_desktop()
    {
        var (page, _) = await fx.NewPageAsync(1280, 800);
        await page.GotoAsync(fx.BaseUrl);

        await page.Locator("#btnAdd").ClickAsync();
        await Assertions.Expect(page.Locator("#addMenu")).ToBeVisibleAsync();

        // Anchored under the pill rather than floating at the bottom of the viewport.
        var menu = await page.Locator("#addMenu").BoundingBoxAsync();
        var pill = await page.Locator("#btnAdd").BoundingBoxAsync();
        Assert.True(menu!.Y > pill!.Y, "The desktop menu should sit below the Add pill.");
    }

    [Fact]
    public async Task The_logo_slot_becomes_a_real_link_once_a_logo_is_set()
    {
        try
        {
            // One test rather than two, because setting a logo changes shared state — split across two
            // tests, whichever ran first decided the other's result.
            //
            // The transition is the point: with nothing to link to it is the button that opens
            // Configure; with a logo it must be an anchor, because no amount of JavaScript makes a
            // <button> ctrl-clickable into a new tab.
            // Located by tag deliberately, and asserted with Expect so it retries: the config arrives
            // asynchronously, so checking the element the instant the page loads is a race.
            var (page, _) = await fx.NewPageAsync();
            await page.GotoAsync(fx.BaseUrl);
    
            await Assertions.Expect(page.Locator("button#logoSlot")).ToBeVisibleAsync();
            await page.Locator("#logoSlot").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/configure");
    
            await fx.SetLogoAsync();
            await page.GotoAsync(fx.BaseUrl);
    
            await Assertions.Expect(page.Locator("a#logoSlot")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#logoSlot")).ToHaveAttributeAsync("href", "/");
    
            // And prove the point: ctrl-click opens a second tab.
            await page.GotoAsync($"{fx.BaseUrl}/tooling");
            await Assertions.Expect(page.Locator("a#logoSlot")).ToBeVisibleAsync();
            var opened = await page.Context.RunAndWaitForPageAsync(async () =>
                await page.Locator("#logoSlot").ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] }));
            Assert.NotNull(opened);
        }
        finally
        {
            // Restores the no-logo state: with a logo set the slot is a link, not the "+" button.
            await fx.ClearLogoAsync();
        }
    }

    [Fact]
    public async Task Epic_header_release_and_version_slots_line_up_into_columns()
    {
        // A release (and version) is now shown on the epic header, not the story row — the sample
        // backlog gives epic 0 a release and leaves epic 1 without one, which is the case that used
        // to leave a slot collapsed and every control after it at a different x. The two-column
        // "cur-set" and "epic-count" widths themselves are not stable — curLabel text differs
        // between the current epic and the others — so the invariant checked here is the gap
        // between the epic-open button and cur-set: it is spanned entirely by the (possibly hidden)
        // version and release vtags, and must be identical whether they hold text or not.
        var (page, _) = await fx.NewPageAsync(1280, 800);
        await page.GotoAsync(fx.BaseUrl);

        var gaps = await page.Locator(".epic-head:visible").EvaluateAllAsync<double[]>(
            "els => els.map(e => Math.round(e.querySelector('.cur-set').getBoundingClientRect().left" +
            " - e.querySelector('.epic-open').getBoundingClientRect().right))");

        Assert.True(gaps.Length >= 2, "Expected at least two visible epic headers.");
        Assert.Single(gaps.Distinct());
    }

    [Fact]
    public async Task The_header_names_the_current_project_but_is_not_a_control()
    {
        // Orientation only — it says which board you are on. Switching is a page, not a header
        // click, because a name that silently navigates is not discoverable as a control.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator(".app-name")).ToContainTextAsync("UI Test");
        Assert.Equal("span", await page.Locator(".app-name").EvaluateAsync<string>(
            "e => e.tagName.toLowerCase()"));
        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Projects_is_reachable_from_the_bottom_bar_on_a_phone()
    {
        // The header is not a control any more, and the top-bar button is hidden at this width, so
        // the bottom bar is the only way in. It was not findable before.
        var (page, _) = await fx.NewPageAsync(390, 760);
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("#btnProjects")).Not.ToBeVisibleAsync();
        // Still five, so every label stays readable rather than ellipsising.
        Assert.Equal(5, await page.Locator(".mnav:visible").CountAsync());

        await page.Locator(".mnav:has-text('Projects')").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
        await Assertions.Expect(page.Locator(".projrow").First).ToBeVisibleAsync();

        // And Configure is still reachable, now via the project you picked.
        await page.Locator("button:has-text('Configure this project')").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/configure");
    }

    [Fact]
    public async Task The_logo_slot_does_something_even_when_already_on_Configure()
    {
        // Found by clicking every control on every screen: on Configure this button took you to the
        // page you were already on, so it did nothing at all.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/configure");

        await page.Locator("#logoSlot").ClickAsync();

        await Assertions.Expect(page.Locator("#cfgLogo")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Open_on_the_current_project_shows_its_board_rather_than_doing_nothing()
    {
        // It shipped disabled: a button labelled "Open" that could not be pressed. A control with
        // no effect is worse than no control, because it reads as the app ignoring you.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/projects");

        var open = page.Locator(".projrow.on .projacts .btn");
        await Assertions.Expect(open).ToBeEnabledAsync();

        await open.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/");
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Clicking_a_project_row_does_not_switch_project()
    {
        // Switching reloads the whole board, which is too much to happen from a stray tap on a
        // name. It is a button you choose, not the row you happened to touch.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/projects");

        await page.Locator(".projrow .projname").First.ClickAsync();
        await page.Locator(".projrow .projpath").First.ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
        await Assertions.Expect(page.Locator(".projrow.on")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Removing_a_project_makes_you_type_its_name_and_says_the_files_are_safe()
    {
        var (page, _) = await fx.NewPageAsync();
        try
        {
            // Add one to remove, so the fixture's own project is never the subject.
            await page.GotoAsync($"{fx.BaseUrl}/add-project");
            await page.Locator("#projBacklog").FillAsync(fx.UncreatedProjectPath);
            await page.Locator("button:has-text('Add project')").ClickAsync();
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(2);

            var name = await page.Locator(".projrow.on .projname").InnerTextAsync();
            await page.Locator(".projrow.on .iconbin").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/remove-project");

            // The reassurance is the point of the page — it must actually be on it.
            await Assertions.Expect(page.Locator(".safenote")).ToContainTextAsync("not touched");
            await Assertions.Expect(page.Locator(".safenote")).ToContainTextAsync("bring it back");

            // A near-miss is refused rather than accepted.
            await page.Locator("#rmConfirm").FillAsync(name.ToLowerInvariant() + "x");
            await page.Locator("button:has-text('Remove project')").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/remove-project");
            await Assertions.Expect(page.Locator("#rmConfirm ~ .field-err")).ToBeVisibleAsync();

            await page.Locator("#rmConfirm").FillAsync(name);
            await page.Locator("button:has-text('Remove project')").ClickAsync();

            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(1);
            Assert.True(File.Exists(fx.UncreatedProjectPath),
                "Removing a project must not delete its backlog — that is what the note promises.");
        }
        finally
        {
            await fx.UsePrimaryProjectAsync();
        }
    }

    [Fact]
    public async Task The_current_project_is_labelled_in_words_not_only_by_colour()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/projects");

        await Assertions.Expect(page.Locator(".projrow.on .projbadge")).ToHaveTextAsync("Current");
    }

    [Fact]
    public async Task Adding_a_project_happens_on_its_own_page_and_returns_to_the_list()
    {
        // The form used to sit under the list, where its fields read as editing the selected
        // project rather than creating a new one.
        var (page, _) = await fx.NewPageAsync();
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/projects");
            // Not absent from the DOM — every page lives in index.html and is toggled with x-show —
            // but not on screen, which is what "the form is not on this page" means to a reader.
            await Assertions.Expect(page.Locator("#projBacklog")).Not.ToBeVisibleAsync();

            await page.Locator("button:has-text('Add a project')").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/add-project");

            await page.Locator("#projBacklog").FillAsync(fx.UncreatedProjectPath);
            await page.Locator("button:has-text('Add project')").ClickAsync();

            // Back to the list, with the new project created, listed and marked Current.
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(2);
            await Assertions.Expect(page.Locator(".projrow.on .projbadge")).ToHaveCountAsync(1);
            Assert.True(File.Exists(fx.UncreatedProjectPath), "Adding a project should create its backlog.");
        }
        finally
        {
            // Adding switches to the new project, and one app serves every test in this collection.
            await fx.UsePrimaryProjectAsync();
        }
    }

    [Fact]
    public async Task Text_and_controls_are_bigger_on_a_phone_than_on_a_desktop()
    {
        // px does not scale with the screen, so the desktop sizes were the phone sizes. Asserted
        // by measuring what the browser computes rather than by matching stylesheet text.
        var (desktop, _) = await fx.NewPageAsync(1280, 800);
        await desktop.GotoAsync($"{fx.BaseUrl}/add-project");
        var deskInput = await desktop.Locator("#projBacklog").EvaluateAsync<string>(
            "e => getComputedStyle(e).fontSize");

        var (phone, _) = await fx.NewPageAsync(390, 760);
        await phone.GotoAsync($"{fx.BaseUrl}/add-project");
        var phoneInput = await phone.Locator("#projBacklog").EvaluateAsync<string>(
            "e => getComputedStyle(e).fontSize");
        var phoneBody = await phone.Locator("body").EvaluateAsync<string>("e => getComputedStyle(e).fontSize");

        Assert.True(Px(phoneInput) > Px(deskInput), "Inputs should be larger on a phone.");
        Assert.True(Px(phoneInput) >= 16, "16px is the comfortable minimum for a touch control.");
        Assert.True(Px(phoneBody) >= 15, "Body text should be raised on a phone.");
    }

    private static double Px(string computed) => double.Parse(computed.Replace("px", ""),
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task An_empty_epic_offers_a_way_to_add_the_first_story()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/empty-epic");

        await Assertions.Expect(page.Locator(".empty button")).ToBeVisibleAsync();
        await page.Locator(".empty button").ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/add-story");
    }

    [Fact]
    public async Task The_summary_counts_the_whole_backlog_and_only_shows_it_on_the_Overview()
    {
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        // Two stories, one Done and one Not Yet Started — and no chip for the six statuses nobody
        // is using, because a row of zeroes says nothing and costs the whole width.
        await Assertions.Expect(page.Locator(".summary-total")).ToContainTextAsync("2");
        await Assertions.Expect(page.Locator(".summary-chips .chip")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator(".summary")).ToContainTextAsync("Done");

        // Inside an epic the same strip would be counting the whole backlog while looking like it
        // counts what is on screen.
        await page.GotoAsync($"{fx.BaseUrl}/tooling");
        await Assertions.Expect(page.Locator(".summary")).Not.ToBeVisibleAsync();

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Search_finds_a_story_by_its_task_text_and_opens_it()
    {
        // The reason search reads every story folder rather than filtering the board the browser
        // already holds: "pelican" appears in a task and nowhere else.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        // Wait for the board before clicking. The panel is the last thing in index.html, so a click
        // landing mid-parse opens a panel whose input Alpine has not registered yet — which no
        // person can outrun, but a test with no human in it does every time.
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

        await page.Locator("#btnSearch").ClickAsync();
        await Assertions.Expect(page.Locator("#searchBox")).ToBeFocusedAsync();

        await page.Locator("#searchBox").FillAsync("pelican");
        await Assertions.Expect(page.Locator(".searchhit")).ToHaveCountAsync(1);

        // The excerpt says which part of the story matched, and the matched word is marked.
        await Assertions.Expect(page.Locator(".searchhit .hit-kind")).ToHaveTextAsync("task");
        await Assertions.Expect(page.Locator(".searchhit .mark").First).ToHaveTextAsync("pelican");

        await page.Locator(".searchhit").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/tooling/backlog-board?h=pelican");
        await Assertions.Expect(page.Locator(".searchpanel")).Not.ToBeVisibleAsync();

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Search_opens_on_slash_and_the_keyboard_alone_reaches_a_story()
    {
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("/");
        await Assertions.Expect(page.Locator("#searchBox")).ToBeFocusedAsync();

        // The "/" that opened the panel must not also land in the box.
        await Assertions.Expect(page.Locator("#searchBox")).ToHaveValueAsync("");

        await page.Keyboard.TypeAsync("write back");
        await Assertions.Expect(page.Locator(".searchhit")).ToHaveCountAsync(1);

        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Locator(".searchhit.on")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/tooling/status-write-back?h=write%20back");
        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Search_finds_an_epic_by_name_and_opens_its_page()
    {
        // An epic used to be findable only through a story that happened to be in it, so searching
        // its name listed its stories and never the epic itself.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

        await page.Locator("#btnSearch").ClickAsync();
        await page.Locator("#searchBox").FillAsync("empty epic");

        var hit = page.Locator(".searchhit");
        await Assertions.Expect(hit).ToHaveCountAsync(1);
        await Assertions.Expect(hit.Locator(".hit-badge")).ToHaveTextAsync("EPIC");
        // No code and no status: an epic has neither, and an empty chip reads as missing data.
        await Assertions.Expect(hit.Locator(".hit-code")).Not.ToBeVisibleAsync();
        await Assertions.Expect(hit.Locator(".hit-epic")).ToHaveTextAsync("0 stories");

        await hit.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/empty-epic?h=empty%20epic");
        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task The_term_you_searched_for_is_marked_on_the_page_you_land_on()
    {
        // The MkDocs behaviour: the word stays highlighted where you arrive, so you can see why
        // this story was the answer without reading it top to bottom.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

        await page.Locator("#btnSearch").ClickAsync();
        await page.Locator("#searchBox").FillAsync("pelican");
        await page.Locator(".searchhit").ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/tooling/backlog-board?h=pelican");
        // Marked inside the task it came from, not merely somewhere on the page.
        await Assertions.Expect(page.Locator(".detail .mark").First).ToHaveTextAsync("pelican");

        // It travels in the URL, so a refresh lands in the same condition rather than plain.
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".detail .mark").First).ToHaveTextAsync("pelican");

        // And it is left behind on the next navigation, rather than following you around.
        await page.Locator(".crumb-link").First.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/");
        await Assertions.Expect(page.Locator(".mark:visible")).ToHaveCountAsync(0);

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Search_announces_the_current_result_without_taking_focus_out_of_the_box()
    {
        // The combobox pattern. Moving real focus onto each result would take it out of the input
        // you are still typing in, so the arrow keys move aria-activedescendant instead — which is
        // the only thing that tells a screen reader which result is current.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

        await page.Locator("#btnSearch").ClickAsync();
        await page.Locator("#searchBox").FillAsync("us-");
        await Assertions.Expect(page.Locator(".searchhit")).ToHaveCountAsync(2);

        // Nothing is current until an arrow key says so.
        await Assertions.Expect(page.Locator("#searchBox")).ToHaveAttributeAsync("aria-activedescendant", "");

        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Locator("#searchBox")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#searchBox")).ToHaveAttributeAsync("aria-activedescendant", "sr0");
        await Assertions.Expect(page.Locator("#sr0")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#sr1")).ToHaveAttributeAsync("aria-selected", "false");

        // And the count is said, not only shown, because the list is not what has focus.
        await Assertions.Expect(page.Locator(".searchpanel [role=status]")).ToHaveTextAsync("2 results for us-");

        // Tab stays inside the dialog rather than walking into the page behind the veil.
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.Locator(".searchpanel").EvaluateAsync<bool>(
            "panel => panel.contains(document.activeElement)"), "Tab should not leave an open dialog.");

        // Closing gives focus back to what opened it, rather than dropping it on the body.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#btnSearch")).ToBeFocusedAsync();

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task Opening_a_result_moves_focus_to_the_page_it_opened()
    {
        // Nothing moves focus in a single-page app on its own, so a keyboard would be left where a
        // dialog used to be and Tab would start again from the top of the page.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);
        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

        await page.Locator("#btnSearch").ClickAsync();
        await page.Locator("#searchBox").FillAsync("pelican");
        await page.Locator(".searchhit").ClickAsync();

        await Assertions.Expect(page.Locator(".detail h1")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task The_description_card_is_headed_by_its_file_and_says_Description_once()
    {
        // It used to be headed "Description" — one of the three sections the file holds — and then
        // the file's own "## Description" heading said the same word a line below it.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/tooling/backlog-board");

        // h1 story title → h2 the file → h3 its sections, which is also a correct outline.
        await Assertions.Expect(page.Locator(".detail .cardhead h2")).ToHaveTextAsync("board/SKILL.md");
        await Assertions.Expect(page.Locator(".detail .md h3", new() { HasTextString = "Description" }))
            .ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".detail :text-is('Description')")).ToHaveCountAsync(1);

        // And the frontmatter is machine metadata for an agent, not something to read on a page.
        await Assertions.Expect(page.Locator(".detail .md")).Not.ToContainTextAsync("name:");

        // The editor still opens on the whole file: what is on disk has to stand on its own.
        await page.Locator("button:has-text('Edit description')").ClickAsync();
        // ToHaveValue, not ToContainText: a textarea driven by x-model carries its content as the
        // value property, and its textContent is empty.
        await Assertions.Expect(page.Locator(".detail .editor"))
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("name: board"));
        // Saving re-renders from the draft, and that path used to skip the trimming the load path
        // does — so saving brought the frontmatter and the title back until the next reload.
        // Saved unchanged on purpose: one app serves this collection, and the file is shared.
        await page.Locator(".detail .cardhead button:has-text('Save')").ClickAsync();
        await Assertions.Expect(page.Locator(".detail .md")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".detail .md")).Not.ToContainTextAsync("name:");
        await Assertions.Expect(page.Locator(".detail :text-is('Description')")).ToHaveCountAsync(1);

        UiFixture.AssertNoConsoleErrors(errors);
    }

    [Fact]
    public async Task The_current_epic_is_something_you_set_from_the_badge_that_shows_it()
    {
        // It was derived-only, with no control anywhere — so the badge looked like a setting you
        // could not find. The badge is the button, as designed.
        var (page, errors) = await fx.NewPageAsync();
        try
        {
            await page.GotoAsync(fx.BaseUrl);
            await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();

            // Two epics: Tooling holds all the work, so it is current by inference to begin with.
            var tooling = page.Locator(".epic-head", new() { HasTextString = "Tooling" }).Locator(".cur-set");
            var empty = page.Locator(".epic-head", new() { HasTextString = "Empty Epic" }).Locator(".cur-set");

            await Assertions.Expect(tooling).ToHaveTextAsync("CURRENT EPIC");
            // And the others offer, rather than showing nothing at all.
            await Assertions.Expect(empty).ToHaveTextAsync("Set as current");

            await empty.ClickAsync();

            await Assertions.Expect(empty).ToHaveTextAsync("CURRENT EPIC");
            await Assertions.Expect(tooling).ToHaveTextAsync("Set as current");
            await Assertions.Expect(page.Locator(".toast")).ToContainTextAsync("Empty Epic");

            // Written through, like every other click here: a reload has to agree.
            await page.ReloadAsync();
            await Assertions.Expect(empty).ToHaveTextAsync("CURRENT EPIC");

            // And it is the same control on the epic's own page, which is where you are when you
            // decide that this is what you are working on.
            await page.GotoAsync($"{fx.BaseUrl}/tooling");
            var onPage = page.Locator("#epicView .cur-set");
            await Assertions.Expect(onPage).ToHaveTextAsync("Set as current");
            await onPage.ClickAsync();
            await Assertions.Expect(onPage).ToHaveTextAsync("CURRENT EPIC");

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            // One app serves this collection, and this writes to the shared backlog.
            await fx.ClearCurrentEpicAsync();
        }
    }

    [Fact]
    public async Task Tasks_can_be_reordered_by_dragging_the_handle()
    {
        // Before this there was no way to move a task: you deleted it and typed it again.
        var (page, errors) = await fx.NewPageAsync();
        try
        {
            await SeedThreeTasksAsync(page);

            var rows = page.Locator("[data-list=tasks] .lrow[data-i] .txt");
            await Assertions.Expect(rows).ToHaveTextAsync(["First", "Second", "Third"]);

            // Drag the first row's handle past the midpoint of the third.
            var grip = page.Locator("[data-list=tasks] .lrow[data-i='0'] .grip");
            var target = await page.Locator("[data-list=tasks] .lrow[data-i='2']").BoundingBoxAsync();
            var start = await grip.BoundingBoxAsync();

            await page.Mouse.MoveAsync(start!.X + start.Width / 2, start.Y + start.Height / 2);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(target!.X + 20, target.Y + target.Height - 2, new() { Steps = 10 });
            await page.Mouse.UpAsync();

            await Assertions.Expect(rows).ToHaveTextAsync(["Second", "Third", "First"]);

            // Written through like every other click here, so a reload has to agree.
            await page.ReloadAsync();
            await Assertions.Expect(rows).ToHaveTextAsync(["Second", "Third", "First"]);

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            await fx.RestoreSampleTasksAsync();
        }
    }

    [Fact]
    public async Task Reordering_works_from_the_keyboard_and_keeps_hold_of_what_moved()
    {
        // A list you can only reorder by dragging is a list some people cannot reorder at all.
        var (page, errors) = await fx.NewPageAsync();
        try
        {
            await SeedThreeTasksAsync(page);
            var rows = page.Locator("[data-list=tasks] .lrow[data-i] .txt");

            await page.Locator("[data-list=tasks] .lrow[data-i='2'] .grip").FocusAsync();
            await page.Keyboard.PressAsync("ArrowUp");
            await Assertions.Expect(rows).ToHaveTextAsync(["First", "Third", "Second"]);

            // Focus follows the row that moved, so pressing again moves the same one rather than
            // whatever has taken its place.
            await page.Keyboard.PressAsync("ArrowUp");
            await Assertions.Expect(rows).ToHaveTextAsync(["Third", "First", "Second"]);

            // And it stops at the end rather than wrapping around to the bottom.
            await page.Keyboard.PressAsync("ArrowUp");
            await Assertions.Expect(rows).ToHaveTextAsync(["Third", "First", "Second"]);

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            await fx.RestoreSampleTasksAsync();
        }
    }

    [Fact]
    public async Task Test_cases_reorder_too_and_not_just_tasks()
    {
        // A separate list, a separate container and separate handlers — so a typo in the second one
        // would not show up in any of the task tests.
        var (page, errors) = await fx.NewPageAsync();
        try
        {
            await fx.SetTestCasesAsync("US-01", ["Alpha", "Beta"]);
            await page.GotoAsync($"{fx.BaseUrl}/tooling/backlog-board");

            var rows = page.Locator("[data-list=testCases] .lrow[data-i] .txt");
            await Assertions.Expect(rows).ToHaveTextAsync(["Alpha", "Beta"]);

            await page.Locator("[data-list=testCases] .lrow[data-i='1'] .grip").FocusAsync();
            await page.Keyboard.PressAsync("ArrowUp");

            await Assertions.Expect(rows).ToHaveTextAsync(["Beta", "Alpha"]);
            await Assertions.Expect(page.Locator(".toast")).ToContainTextAsync("Test case moved");

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            await fx.SetTestCasesAsync("US-01", []);
        }
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task A_real_sentence_gets_most_of_a_phone_screen_to_be_read_in(int width)
    {
        // A row on a phone spends its width on everything except the words: card padding, a drag
        // handle, a checkbox or a status chip, the gaps between them, and a delete button. At 390px
        // that left 222px for a task and about 180px for a test case, so a real sentence broke into
        // a ragged column a few words wide and read as though it had been cut off.
        //
        // Driven with touch on purpose: the handle and the delete button are permanently visible
        // where there is no hover to reveal them, so a phone lays this row out differently from a
        // narrow desktop window — which is why looking at a resized browser missed it.
        var (page, _) = await fx.NewPageAsync(width, 820, touch: true);
        try
        {
            await fx.SetTasksAsync("US-01", ["Install post-install CLI tools (`dotnet tool install --global dotnet-ef`; Azurite via Docker image or `npm i -g azurite`)"]);
            await fx.SetTestCasesAsync("US-01", ["Run `dotnet --version && node --version && npm --version` — each prints a version line, no \"not recognized\" error"]);
            await page.GotoAsync($"{fx.BaseUrl}/tooling/backlog-board");

            var task = await page.Locator("[data-list=tasks] .lrow .txt").First.BoundingBoxAsync();
            var test = await page.Locator("[data-list=testCases] .lrow .txt").First.BoundingBoxAsync();

            // A test case is a two-line list item on a phone — what it checks, then its state — so
            // its sentence gets nearly the whole width. A task keeps its checkbox in front of the
            // words, so it gets a fair share rather than all of it.
            Assert.True(test!.Width >= width * 0.8,
                        $"A test case had {test.Width:0}px of {width} to be read in.");
            Assert.True(task!.Width >= width * 0.45,
                        $"A task had {task.Width:0}px of {width} to be read in.");

            // And the words come first, above the status — the point of the arrangement.
            var chip = await page.Locator("[data-list=testCases] .lrow .chip").First.BoundingBoxAsync();
            Assert.True(test.Y < chip!.Y, "A test case should read as its sentence, then its state.");

            // And none of it is bought by pushing the page sideways.
            Assert.False(await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth"),
                "Nothing should scroll sideways at any width.");
        }
        finally
        {
            await fx.RestoreSampleTasksAsync();
            await fx.SetTestCasesAsync("US-01", []);
        }
    }

    [Fact]
    public async Task A_replys_answer_belongs_to_the_story_it_was_asked_about()
    {
        // Reported as "I ticked a task and every test case turned green". What had actually
        // happened was that the tick's reply arrived after the next story had opened and painted
        // the first story's tasks and test cases onto it — so the page showed a finished story
        // under another story's title.
        var (page, _) = await fx.NewPageAsync();
        try
        {
            await fx.SetTasksAsync("US-01", ["A task belonging to the first story"]);
            await fx.SetTestCasesAsync("US-01", ["A test belonging to the first story"]);

            await page.GotoAsync($"{fx.BaseUrl}/tooling/backlog-board");
            await Assertions.Expect(page.Locator("[data-list=tasks] .lrow .txt")).ToHaveCountAsync(1);

            // Hold the reply back so the navigation certainly wins the race, the way a slow disk or
            // a big backlog does on a real machine.
            await page.RouteAsync("**/api/story/US-01/tasks", async route =>
            {
                await Task.Delay(1200);
                await route.ContinueAsync();
            });

            await page.Locator("[data-list=tasks] .lrow .box").ClickAsync();
            await page.Locator(".crumb-link").First.ClickAsync();
            await page.Locator(".story-row .aiftm-open").Nth(1).ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/tooling/status-write-back");

            // The other story has no tasks and no test cases of its own, and must still have none
            // once the first story's reply turns up.
            await page.WaitForTimeoutAsync(1800);
            await Assertions.Expect(page.Locator("[data-list=tasks] .lrow[data-i]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-list=testCases] .lrow[data-i]")).ToHaveCountAsync(0);
        }
        finally
        {
            await page.UnrouteAllAsync();
            await fx.RestoreSampleTasksAsync();
            await fx.SetTestCasesAsync("US-01", []);
        }
    }

    [Fact]
    public async Task Marking_a_story_Done_over_unfinished_work_asks_first()
    {
        // It asks rather than refuses: doing this is usually a slip and occasionally a decision.
        // Only on the story page, because that is the only place the story's folder is loaded —
        // the board holds the index alone and would have to open every folder to know.
        var (page, errors) = await fx.NewPageAsync();
        try
        {
            await fx.SetTasksAsync("US-02", ["Something still to do"]);
            await fx.SetTestCasesAsync("US-02", ["Something nobody ran"]);
            await page.GotoAsync($"{fx.BaseUrl}/tooling/status-write-back");
            await Assertions.Expect(page.Locator("[data-list=tasks] .lrow[data-i]")).ToHaveCountAsync(1);

            await page.Locator(".detail-bar .chip.lg").ClickAsync();
            await page.Locator(".pop .pop-row:has-text('Done')").ClickAsync();

            // It says what is outstanding, in both lists, rather than "are you sure?".
            var dialog = page.Locator("dialog[open]");
            await Assertions.Expect(dialog).ToContainTextAsync("1 task is not ticked");
            await Assertions.Expect(dialog).ToContainTextAsync("1 test case has not passed");

            // Cancel means no: the status is untouched.
            await dialog.Locator("button:has-text('Cancel')").ClickAsync();
            await Assertions.Expect(page.Locator(".detail-bar .chip.lg")).ToContainTextAsync("Not Yet Started");

            // Confirming goes through — it is a warning, not a rule.
            await page.Locator(".detail-bar .chip.lg").ClickAsync();
            await page.Locator(".pop .pop-row:has-text('Done')").ClickAsync();
            await page.Locator("dialog[open] button:has-text('Mark Done')").ClickAsync();
            await Assertions.Expect(page.Locator(".detail-bar .chip.lg")).ToContainTextAsync("Done");

            UiFixture.AssertNoConsoleErrors(errors);
        }
        finally
        {
            await fx.SetTasksAsync("US-02", []);
            await fx.SetTestCasesAsync("US-02", []);
            await fx.SetStatusAsync("US-02", "Not Yet Started");
        }
    }

    [Fact]
    public async Task A_story_with_nothing_outstanding_is_marked_Done_without_being_asked()
    {
        // A warning that fires when there is nothing to warn about is a click everyone learns to
        // dismiss without reading, which costs the warning its only job.
        var (page, _) = await fx.NewPageAsync();
        try
        {
            await fx.SetTasksAsync("US-02", []);
            await fx.SetTestCasesAsync("US-02", []);
            await page.GotoAsync($"{fx.BaseUrl}/tooling/status-write-back");

            await page.Locator(".detail-bar .chip.lg").ClickAsync();
            await page.Locator(".pop .pop-row:has-text('Done')").ClickAsync();

            await Assertions.Expect(page.Locator(".detail-bar .chip.lg")).ToContainTextAsync("Done");
            await Assertions.Expect(page.Locator("dialog[open]")).ToHaveCountAsync(0);
        }
        finally
        {
            await fx.SetStatusAsync("US-02", "Not Yet Started");
        }
    }

    /// <summary>Three tasks with tellable names, put there through the app's own endpoint.</summary>
    private async Task SeedThreeTasksAsync(IPage page)
    {
        await fx.SetTasksAsync("US-01", ["First", "Second", "Third"]);
        await page.GotoAsync($"{fx.BaseUrl}/tooling/backlog-board");
        await Assertions.Expect(page.Locator("[data-list=tasks] .lrow[data-i]")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task Escape_closes_search_and_typing_a_slash_into_a_field_does_not_open_it()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/add-story");

        // A path is mostly slashes. Opening search over the field being typed into would make the
        // form unusable.
        await page.Locator("#storyTitle").FillAsync("");
        await page.Locator("#storyTitle").PressSequentiallyAsync("a/b");
        await Assertions.Expect(page.Locator(".searchpanel")).Not.ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#storyTitle")).ToHaveValueAsync("a/b");

        await page.GotoAsync(fx.BaseUrl);
        await page.Locator("#btnSearch").ClickAsync();
        await Assertions.Expect(page.Locator(".searchpanel")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".searchpanel")).Not.ToBeVisibleAsync();
    }
}
