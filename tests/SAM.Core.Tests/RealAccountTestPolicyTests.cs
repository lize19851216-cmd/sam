using SAM.Core;
using Xunit;

namespace SAM.Core.Tests;

public sealed class RealAccountTestPolicyTests
{
    [Fact]
    public void Validate_account_name_trims_a_single_non_simulated_account()
    {
        Assert.Equal("user_owned_account", RealAccountTestPolicy.ValidateAccountName("  user_owned_account  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mock_0001")]
    [InlineData("MOCK_0001")]
    [InlineData("name\nwith-control")]
    public void Validate_account_name_rejects_unsafe_or_simulated_values(string accountName)
    {
        Assert.Throws<ArgumentException>(() => RealAccountTestPolicy.ValidateAccountName(accountName));
    }

    [Fact]
    public void Validate_account_name_rejects_values_over_the_length_limit()
    {
        Assert.Throws<ArgumentException>(() => RealAccountTestPolicy.ValidateAccountName(new string('a', RealAccountTestPolicy.MaximumAccountNameLength + 1)));
    }

    [Fact]
    public void External_broker_requires_exactly_one_non_simulated_account()
    {
        RealAccountTestPolicy.EnsureSingleExternalTestAccount([new Account { AccountName = "user_owned_account" }]);

        Assert.Throws<InvalidOperationException>(() => RealAccountTestPolicy.EnsureSingleExternalTestAccount([]));
        Assert.Throws<InvalidOperationException>(() => RealAccountTestPolicy.EnsureSingleExternalTestAccount([
            new Account { AccountName = "account_one" },
            new Account { AccountName = "account_two" }
        ]));
        Assert.Throws<ArgumentException>(() => RealAccountTestPolicy.EnsureSingleExternalTestAccount([new Account { AccountName = "mock_0001" }]));
    }
}
