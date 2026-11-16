using System.Security.Claims;
using Ash.Modules.Identity.Application.Auth;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>
/// Task 051: the subject and e-mail of each token shape AKSHI's providers issue — B2C local and federated accounts,
/// Entra ID members and B2B guests — and the startup validation of Auth:Providers.
/// </summary>
public class IdentityClaimMapTests
{
    private static readonly AuthProviderOptions B2C = new() { Name = "b2c", Kind = AuthProviderKinds.B2C };
    private static readonly AuthProviderOptions Entra = new() { Name = "entra", Kind = AuthProviderKinds.EntraId };

    [Fact]
    public void B2C_local_account_takes_oid_and_the_first_of_the_emails_array()
    {
        var mapped = IdentityClaimMap.Map([new("oid", "oid-1"), new("sub", "sub-1"), new("emails", "[\"Drita@Avokatura.gov.al\"]")], B2C);

        mapped.ShouldBe(new MappedIdentity("oid-1", "drita@avokatura.gov.al", false));
    }

    [Fact]
    public void B2C_federated_account_reads_emails_as_separate_claims()
    {
        Claim[] claims = [new("oid", "oid-2"), new("idp", "https://login.microsoftonline.com/gov/v2.0"),
            new("emails", "user@institucion.gov.al"), new("emails", "second@example.test")];

        IdentityClaimMap.Map(claims, B2C).ShouldBe(new MappedIdentity("oid-2", "user@institucion.gov.al", false));
    }

    [Fact]
    public void EntraId_member_takes_oid_and_the_email_optional_claim()
    {
        Claim[] claims = [new("oid", "oid-3"), new("tid", "t"), new("email", "jurist@ministria.gov.al"),
            new("preferred_username", "jurist@ministria.gov.al")];

        IdentityClaimMap.Map(claims, Entra).ShouldBe(new MappedIdentity("oid-3", "jurist@ministria.gov.al", false));
    }

    [Fact]
    public void EntraId_b2b_guest_without_email_falls_back_to_the_user_name_and_says_so()
    {
        // a guest's upn is "name_other.al#EXT#@tenant.onmicrosoft.com" — the preferred_username is the real address
        Claim[] claims = [new("oid", "oid-4"), new("tid", "t"), new("preferred_username", "Guest@Other.al"),
            new("upn", "guest_other.al#EXT#@tenant.onmicrosoft.com")];

        IdentityClaimMap.Map(claims, Entra).ShouldBe(new MappedIdentity("oid-4", "guest@other.al", true));
    }

    [Fact]
    public void Without_oid_the_subject_is_sub_and_a_user_name_that_is_no_email_is_ignored()
    {
        IdentityClaimMap.Map([new("sub", "sub-5"), new("preferred_username", "not-an-email")], Entra)
            .ShouldBe(new MappedIdentity("sub-5", null, false));
    }

    [Fact]
    public void A_configured_claim_map_overrides_the_defaults()
    {
        var custom = new AuthProviderOptions
        {
            Name = "x", Kind = AuthProviderKinds.EntraId,
            ClaimMap = new ClaimMapOptions { Subject = ["sub"], Email = ["mail"] },
        };

        IdentityClaimMap.Map([new("oid", "oid-6"), new("sub", "sub-6"), new("mail", "a@b.al")], custom)
            .ShouldBe(new MappedIdentity("sub-6", "a@b.al", false));
    }

    [Fact]
    public void Validation_refuses_no_provider_missing_fields_and_entra_without_tenants()
    {
        new AuthProvidersOptions().Validate().ShouldContain(e => e.Contains("at least one enabled provider"));

        var errors = new AuthProvidersOptions
        {
            Providers = [new AuthProviderOptions { Name = "entra", Kind = AuthProviderKinds.EntraId, Enabled = true }],
        }.Validate();
        errors.ShouldContain(e => e.Contains("Authority or MetadataAddress"));
        errors.ShouldContain(e => e.Contains("ClientId"));
        errors.ShouldContain(e => e.Contains("Audience"));
        errors.ShouldContain(e => e.Contains("ValidIssuers"));
        errors.ShouldContain(e => e.Contains("AllowedTenantIds"));

        new AuthProvidersOptions
        {
            Providers =
            [
                new AuthProviderOptions
                {
                    Name = "b2c", Kind = AuthProviderKinds.B2C, Enabled = true, Authority = "https://x", ClientId = "c",
                    Audience = "a", ValidIssuers = ["https://x/v2.0"], Scopes = ["a/access"], DisplayName = "Hyr",
                },
            ],
        }.Validate().ShouldBeEmpty();
    }
}
