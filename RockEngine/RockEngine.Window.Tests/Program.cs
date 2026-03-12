using RockEngine.Window.Tests;

try
{
    // Configure test window settings
    Environment.SetEnvironmentVariable("ROCKENGINE_TEST_MODE", "true");

    using var app = new AssetSystemTestApplication();
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Test failed: {ex}");
    Console.WriteLine(ex.StackTrace);
    throw;
}