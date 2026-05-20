using Microsoft.Playwright;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Klik voor Wonen Automation Starting ===");
        Console.WriteLine($"Run started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var username = Environment.GetEnvironmentVariable("KLIKVOORWONEN_USERNAME");
        var password = Environment.GetEnvironmentVariable("KLIKVOORWONEN_PASSWORD");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("ERROR: KLIKVOORWONEN_USERNAME and KLIKVOORWONEN_PASSWORD environment variables must be set!");
            Environment.Exit(1);
        }

        try
        {
            using var playwright = await Playwright.CreateAsync();

            var headless = Environment.GetEnvironmentVariable("HEADLESS") != "false";
            Console.WriteLine($"Launching browser (headless: {headless})...");

            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = headless,
                Args = headless ? Array.Empty<string>() : new[] { "--start-maximized" }
            });
            var page = await browser.NewPageAsync(new()
            {
                ViewportSize = headless ? new ViewportSize { Width = 1920, Height = 1080 } : ViewportSize.NoViewport
            });

            // Navigate and wait for the page to fully settle before interacting
            Console.WriteLine("Navigating to Klik voor Wonen...");
            await page.GotoAsync("https://www.klikvoorwonen.nl", new() { WaitUntil = WaitUntilState.NetworkIdle });

            // Accept cookies — wait for Cookiebot banner, then click "Alles toestaan"
            Console.WriteLine("Accepting cookies...");
            try
            {
                await page.WaitForSelectorAsync("#CybotCookiebotDialogBodyLevelButtonLevelOptinAllowAll", new() { Timeout = 5000 });
                await page.ClickAsync("#CybotCookiebotDialogBodyLevelButtonLevelOptinAllowAll", new() { Timeout = 3000 });
                Console.WriteLine("✓ Cookies accepted");
            }
            catch
            {
                Console.WriteLine("(No cookie banner found or already accepted)");
            }

            // Click login button
            Console.WriteLine("Looking for login button...");
            try
            {
                await page.ClickAsync("text=Inloggen", new() { Timeout = 5000 });
            }
            catch
            {
                await page.ClickAsync("a[href*='login']", new() { Timeout = 5000 });
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Fill in login form
            Console.WriteLine("Filling in credentials...");
            await page.FillAsync("input[name='username'], input[type='email'], input#username", username);
            await page.FillAsync("input[name='password'], input[type='password'], input#password", password);

            // Submit login — wait for the URL to reach /portaal/ which confirms successful login
            Console.WriteLine("Submitting login...");
            await page.ClickAsync("#Login > div > zds-form > zds-button");
            await page.WaitForURLAsync("**/portaal/**", new() { Timeout = 15000 });

            // Verify login actually succeeded
            var currentUrl = page.Url;
            Console.WriteLine($"Current URL after login: {currentUrl}");
            if (!currentUrl.Contains("/portaal/"))
            {
                Console.Error.WriteLine($"ERROR: Login failed — expected to land on /portaal/ but got: {currentUrl}");
                Environment.Exit(1);
            }
            Console.WriteLine("✓ Login successful!");

            // Navigate to properties
            Console.WriteLine("\nNavigating to properties page...");
            await NavigateToPropertiesList(page);

            var propertySections = await WaitForProperties(page);
            if (propertySections == null)
            {
                Console.Error.WriteLine("ERROR: No properties found after retries. Exiting.");
                Environment.Exit(1);
            }

            int reactedCount = 0;
            int alreadyReactedCount = 0;
            int errorCount = 0;

            for (int i = 0; i < propertySections.Count; i++)
            {
                try
                {
                    // Re-query sections each iteration since we navigate away and back
                    var sections = await page.QuerySelectorAllAsync("section.list-item");
                    if (i >= sections.Count) break;

                    var section = sections[i];
                    var sectionText = await section.TextContentAsync();

                    if (sectionText?.Contains("Je hebt al gereageerd") == true)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Already reacted - skipping");
                        alreadyReactedCount++;
                        continue;
                    }
                    if (sectionText?.Contains("Motivatie") == true)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Motivation - skipping");
                        alreadyReactedCount++;
                        continue;
                    }

                    // Get detail page link — Angular compiles ng-href into href, prefer href with ng-href as fallback
                    var linkElement = await section.QuerySelectorAsync("a[ng-href*='/details/']");
                    if (linkElement == null)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ No detail link found - skipping");
                        errorCount++;
                        continue;
                    }

                    var detailUrl = await linkElement.GetAttributeAsync("href")
                                 ?? await linkElement.GetAttributeAsync("ng-href");
                    if (string.IsNullOrEmpty(detailUrl))
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Invalid detail URL - skipping");
                        errorCount++;
                        continue;
                    }

                    if (!detailUrl.StartsWith("http"))
                        detailUrl = "https://www.klikvoorwonen.nl" + detailUrl;

                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Opening property details...");
                    await page.GotoAsync(detailUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                    await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");

                    try
                    {
                        // Wait for button to appear, then click
                        await page.WaitForSelectorAsync("input.reageer-button[value='Reageer']", new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                        await page.ClickAsync("input.reageer-button[value='Reageer']", new() { Timeout = 5000 });
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✓ Reaction submitted!");
                        reactedCount++;
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Could not click Reageer button: {ex.Message}");
                        errorCount++;
                    }

                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Returning to property list...");
                    await NavigateToPropertiesList(page);
                    if (await WaitForProperties(page) == null)
                    {
                        Console.Error.WriteLine("ERROR: Lost property list after returning. Stopping.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Error processing property: {ex.Message}");
                    errorCount++;

                    try
                    {
                        await NavigateToPropertiesList(page);
                        if (await WaitForProperties(page) == null)
                        {
                            Console.WriteLine("  ✗ Could not return to property list - stopping");
                            break;
                        }
                    }
                    catch
                    {
                        Console.WriteLine("  ✗ Could not return to property list - stopping");
                        break;
                    }
                }
            }

            // Take a final screenshot
            Console.WriteLine("\nTaking final screenshot...");
            try
            {
                await page.ScreenshotAsync(new() { Path = "final-state.png", FullPage = true });
            }
            catch { }

            // Log summary
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("AUTOMATION SUMMARY");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"Total properties found:    {propertySections.Count}");
            Console.WriteLine($"Successfully reacted:      {reactedCount}");
            Console.WriteLine($"Already reacted (skipped): {alreadyReactedCount}");
            Console.WriteLine($"Errors/could not process:  {errorCount}");
            Console.WriteLine($"Run completed at:          {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine(new string('=', 50) + "\n");

            // Logout by navigating directly to the logout URL
            try
            {
                await page.GotoAsync("https://www.klikvoorwonen.nl/portaal/mijn-klik-voor-wonen/mijn-gegevens/uitloggen?logintype=logout", new() { WaitUntil = WaitUntilState.Load, Timeout = 10000 });
                Console.WriteLine("✓ Logged out successfully");
            }
            catch
            {
                Console.WriteLine("(Logout skipped)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n=== ERROR ===");
            Console.Error.WriteLine($"An error occurred: {ex.Message}");
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }

        Console.WriteLine("Automation completed successfully!");
    }

    static Task NavigateToPropertiesList(IPage page) =>
        page.GotoAsync(
            "https://www.klikvoorwonen.nl/aanbod/nu-te-huur/huurwoningen#?gesorteerd-op=zoekprofiel",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

    static async Task<IReadOnlyList<IElementHandle>?> WaitForProperties(IPage page, int maxRetries = 5)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            await Task.Delay(1000);
            var sections = await page.QuerySelectorAllAsync("section.list-item");

            if (sections.Count > 0)
            {
                // Wait for Angular to finish rendering links within sections
                // Non-fatal: sections with no links are valid (e.g. all already reacted)
                try
                {
                    await page.WaitForSelectorAsync(
                        "section.list-item a[ng-href*='/details/']",
                        new() { Timeout = 5000 });
                }
                catch { }

                Console.WriteLine($"Found {sections.Count} properties on the page (attempt {attempt + 1})");
                return sections;
            }

            Console.WriteLine($"No properties found yet, retrying... (attempt {attempt + 1}/{maxRetries})");
        }

        return null;
    }
}
