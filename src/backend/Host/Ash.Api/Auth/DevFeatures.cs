namespace Ash.Api.Auth;

/// <summary>
/// Task 051: the features that exist for development and manual tests only — the X-Dev-User header (and the frontend's
/// dev login that sends it), POST /api/admin/jobs/{name}/run, the NoOp virus scanner. In Development they are on unless
/// switched off (DevFeatures:HeaderAuth / DevFeatures:JobRunEndpoint = false); outside Development they are never
/// registered, and <see cref="AssertSafe"/> refuses to start the app if the configuration tries to turn one on.
/// </summary>
public static class DevFeatures
{
    public const string HeaderAuthKey = "DevFeatures:HeaderAuth";
    public const string JobRunEndpointKey = "DevFeatures:JobRunEndpoint";

    public static bool HeaderAuthEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() && configuration.GetValue<bool?>(HeaderAuthKey) != false;

    public static bool JobRunEndpointEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() && configuration.GetValue<bool?>(JobRunEndpointKey) != false;

    /// <summary>The single startup assertion: outside Development no dev feature may be switched on.</summary>
    public static void AssertSafe(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var on = new List<string>();
        if (configuration.GetValue<bool?>(HeaderAuthKey) == true) on.Add(HeaderAuthKey);
        if (configuration.GetValue<bool?>(JobRunEndpointKey) == true) on.Add(JobRunEndpointKey);
        if (string.Equals(configuration["Scanner:Mode"], "NoOp", StringComparison.OrdinalIgnoreCase)) on.Add("Scanner:Mode=NoOp");
        if (on.Count > 0)
        {
            throw new InvalidOperationException(
                $"Development-only features are enabled in the {environment.EnvironmentName} environment: {string.Join(", ", on)}. The app refuses to start.");
        }
    }
}
