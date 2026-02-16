using Microsoft.Playwright;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Klik voor Wonen Automation Starting ===");
        Console.WriteLine($"Run started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Get credentials from environment variables
        var username = Environment.GetEnvironmentVariable("KLIKVOORWONEN_USERNAME");
        var password = Environment.GetEnvironmentVariable("KLIKVOORWONEN_PASSWORD");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("ERROR: KLIKVOORWONEN_USERNAME and KLIKVOORWONEN_PASSWORD environment variables must be set!");
            Environment.Exit(1);
        }

        try
        {
            // Initialize Playwright
            using var playwright = await Playwright.CreateAsync();

            // Launch browser (headless in production)
            var headless = Environment.GetEnvironmentVariable("HEADLESS") != "false";
            Console.WriteLine($"Launching browser (headless: {headless})...");

            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = headless
            });

            var page = await browser.NewPageAsync();

            // Set viewport for consistent rendering
            await page.SetViewportSizeAsync(1920, 1080);

            // Navigate to Klik voor Wonen
            Console.WriteLine("Navigating to Klik voor Wonen...");
            await page.GotoAsync("https://www.klikvoorwonen.nl");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Accept cookies
            Console.WriteLine("Accepting cookies...");
            try
            {
                await page.ClickAsync("#klaro > div > div > div > div > div > button", new() { Timeout = 5000 });
                await Task.Delay(1000);
                Console.WriteLine("✓ Cookies accepted");
            }
            catch
            {
                Console.WriteLine("(No cookie banner found or already accepted)");
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Click login button
            Console.WriteLine("Looking for login button...");


            try
            {
                // Try to find and click login link/button
                await page.ClickAsync("text=Inloggen", new() { Timeout = 5000 });
            }
            catch
            {
                // Alternative selectors
                await page.ClickAsync("a[href*='login']", new() { Timeout = 5000 });
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Fill in login form
            Console.WriteLine("Filling in credentials...");
            await page.FillAsync("input[name='username'], input[type='email'], input#username", username);
            await page.FillAsync("input[name='password'], input[type='password'], input#password", password);

            // Submit login
            Console.WriteLine("Submitting login...");
            await page.ClickAsync("#Login > div > zds-form > zds-button");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Wait a bit for login to complete
            await Task.Delay(2000);

            // Check if login was successful
            var currentUrl = page.Url;
            Console.WriteLine($"Current URL after login: {currentUrl}");
            Console.WriteLine("✓ Login successful!");

            // Navigate to available properties page
            Console.WriteLine("\nNavigating to properties page...");
            await page.GotoAsync("https://www.klikvoorwonen.nl/aanbod/nu-te-huur/huurwoningen#?gesorteerd-op=zoekprofiel", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            // Retry logic to wait for properties to load
            IReadOnlyList<IElementHandle> propertySections;
            int maxRetries = 5;
            int retryCount = 0;

            while (true)
            {
                await Task.Delay(1000); // Wait 1 second
                propertySections = await page.QuerySelectorAllAsync("section.list-item");

                if (propertySections.Count > 0)
                {
                    Console.WriteLine($"Found {propertySections.Count} properties on the page (attempt {retryCount + 1})");
                    break;
                }

                retryCount++;
                if (retryCount >= maxRetries)
                {
                    Console.Error.WriteLine($"ERROR: No properties found after {maxRetries} retries. Exiting.");
                    Environment.Exit(1);
                }

                Console.WriteLine($"No properties found yet, retrying... (attempt {retryCount}/{maxRetries})");
            }

            int reactedCount = 0;
            int alreadyReactedCount = 0;
            int errorCount = 0;

            // Process each property
            for (int i = 0; i < propertySections.Count; i++)
            {
                try
                {
                    // Re-query the sections each time since we navigate away and back
                    var sections = await page.QuerySelectorAllAsync("section.list-item");
                    if (i >= sections.Count) break;

                    var section = sections[i];

                    // Check if already reacted by looking for "Je hebt al gereageerd" text
                    var sectionText = await section.TextContentAsync();

                    if (sectionText?.Contains("Je hebt al gereageerd") == true)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Already reacted - skipping");
                        alreadyReactedCount++;
                        continue;
                    }

                    // Get the link to the property detail page
                    var linkElement = await section.QuerySelectorAsync("a[ng-href*='/details/']");
                    if (linkElement == null)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ No detail link found - skipping");
                        errorCount++;
                        continue;
                    }

                    var detailUrl = await linkElement.GetAttributeAsync("href");
                    if (string.IsNullOrEmpty(detailUrl))
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Invalid detail URL - skipping");
                        errorCount++;
                        continue;
                    }

                    // Make sure it's a full URL
                    if (!detailUrl.StartsWith("http"))
                    {
                        detailUrl = "https://www.klikvoorwonen.nl" + detailUrl;
                    }

                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Opening property details...");

                    // Navigate to the detail page
                    await page.GotoAsync(detailUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                    await Task.Delay(2000); // Give extra time for Angular to render

                    // Scroll down to make sure the button is visible
                    await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
                    await Task.Delay(500);

                    // Click the "Reageer" button
                    try
                    {
                        // Wait for the button to be visible first
                        await page.WaitForSelectorAsync("input.reageer-button[value='Reageer']", new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

                        // Click it
                        await page.ClickAsync("input.reageer-button[value='Reageer']", new() { Timeout = 5000 });
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✓ Reaction submitted!");
                        reactedCount++;
                        await Task.Delay(1500); // Wait for popup/confirmation
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Could not click Reageer button: {ex.Message}");
                        errorCount++;
                    }

                    // Go back to the properties list
                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] Returning to property list...");
                    await page.GotoAsync("https://www.klikvoorwonen.nl/aanbod/nu-te-huur/huurwoningen#?gesorteerd-op=zoekprofiel", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

                    // Retry logic when returning to properties list
                    retryCount = 0;
                    while (true)
                    {
                        await Task.Delay(1000); // Wait 1 second
                        var currentSections = await page.QuerySelectorAllAsync("section.list-item");

                        if (currentSections.Count > 0)
                        {
                            break;
                        }

                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            Console.Error.WriteLine($"ERROR: No properties found after {maxRetries} retries when returning to list. Exiting.");
                            Environment.Exit(1);
                        }

                        Console.WriteLine($"  No properties found yet, retrying... (attempt {retryCount}/{maxRetries})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{i + 1}/{propertySections.Count}] ✗ Error processing property: {ex.Message}");
                    errorCount++;

                    // Try to go back to the list
                    try
                    {
                        await page.GotoAsync("https://www.klikvoorwonen.nl/aanbod/nu-te-huur/huurwoningen#?gesorteerd-op=zoekprofiel", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

                        // Retry logic when returning to properties list after error
                        retryCount = 0;
                        while (true)
                        {
                            await Task.Delay(1000); // Wait 1 second
                            var currentSections = await page.QuerySelectorAllAsync("section.list-item");

                            if (currentSections.Count > 0)
                            {
                                break;
                            }

                            retryCount++;
                            if (retryCount >= maxRetries)
                            {
                                Console.Error.WriteLine($"ERROR: No properties found after {maxRetries} retries when returning to list. Exiting.");
                                Environment.Exit(1);
                            }

                            Console.WriteLine($"  No properties found yet, retrying... (attempt {retryCount}/{maxRetries})");
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
                await page.ScreenshotAsync(new()
                {
                    Path = "final-state.png",
                    FullPage = true
                });
            }
            catch
            {
                // Screenshot not critical
            }

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

            // Logout (optional)
            try
            {
                await page.ClickAsync("text=Uitloggen", new() { Timeout = 3000 });
                Console.WriteLine("✓ Logged out successfully");
            }
            catch
            {
                Console.WriteLine("(Logout button not found, skipping...)");
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
}